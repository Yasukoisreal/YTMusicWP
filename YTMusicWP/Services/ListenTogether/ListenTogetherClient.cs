using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace YTMusicWP.Services.ListenTogether
{
    public class ListenTogetherClient : IDisposable
    {
        public const string DefaultServerUrl = "wss://metroserverx.meowery.eu/ws";

        private MessageWebSocket _webSocket;
        private readonly ServerClock _serverClock = new ServerClock();
        private CancellationTokenSource _cts;

        private TaskCompletionSource<ServerCapabilities> _handshakeTcs;
        private string _sessionToken;
        private long _pingSequence;
        private int _reconnectAttempts;
        private int _reconnectDelayMs = 1000;
        private bool _isDisposed;
        private bool _isExplicitDisconnect;

        public ServerClock Clock => _serverClock;

        public bool IsConnected => _webSocket != null && _handshakeTcs != null && _handshakeTcs.Task.IsCompleted && !_handshakeTcs.Task.IsFaulted;

        public string ServerUrl { get; set; } = DefaultServerUrl;

        public event Action<ServerCapabilities> Connected;
        public event Action<string, bool> Disconnected; // reason, willRetry
        public event Action ClockReady;
        public event Action<string, byte[]> MessageReceived;

        public long? ServerNow() => _serverClock.Now();

        public long PositionAt(long position, long effectiveAtServerTime, bool isPlaying)
        {
            return _serverClock.PositionAt(position, effectiveAtServerTime, isPlaying);
        }

        public async Task ConnectAsync()
        {
            _isExplicitDisconnect = false;
            _reconnectAttempts = 0;
            _reconnectDelayMs = 1000;
            await StartConnectionAsync();
        }

        private async Task StartConnectionAsync()
        {
            CleanUpSocket();
            _cts = new CancellationTokenSource();
            _handshakeTcs = new TaskCompletionSource<ServerCapabilities>();

            try
            {
                _webSocket = new MessageWebSocket();
                _webSocket.Control.MessageType = SocketMessageType.Binary;
                _webSocket.MessageReceived += OnWebSocketMessageReceived;
                _webSocket.Closed += OnWebSocketClosed;

                string url = string.IsNullOrWhiteSpace(ServerUrl) ? DefaultServerUrl : ServerUrl.Trim();
                Debug.WriteLine("[ListenTogetherClient] Connecting to " + url);

                await _webSocket.ConnectAsync(new Uri(url));
                _serverClock.Reset();

                // 1. Send client capabilities handshake immediately
                var clientCaps = new ClientCapabilities
                {
                    SupportsProtobuf = true,
                    SupportsCompression = false,
                    ClientVersion = "YTMusicWP-2.2"
                };
                byte[] capsPayload = ProtobufCodec.EncodeClientCapabilities(clientCaps);
                await SendRawAsync(MessageTypes.ClientCapabilities, capsPayload);

                // 2. Await server capabilities with 10-second timeout
                var timeoutTask = Task.Delay(10000, _cts.Token);
                var completedTask = await Task.WhenAny(_handshakeTcs.Task, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("Listen Together handshake timed out after 10 seconds");
                }

                var serverCaps = await _handshakeTcs.Task;
                Debug.WriteLine("[ListenTogetherClient] Handshake complete! Server version: " + serverCaps.ServerVersion);

                // 3. If resuming a session, send reconnect
                if (!string.IsNullOrEmpty(_sessionToken))
                {
                    Debug.WriteLine("[ListenTogetherClient] Resuming room with session token...");
                    await SendRawAsync(MessageTypes.Reconnect, ProtobufCodec.EncodeReconnect(_sessionToken));
                }

                _reconnectAttempts = 0;
                _reconnectDelayMs = 1000;
                Connected?.Invoke(serverCaps);

                // 4. Start Ping loop in background
                var ignored = RunPingLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogetherClient] Connection error: " + ex.Message);
                ScheduleReconnect("Connection failed: " + ex.Message);
            }
        }

        private async Task RunPingLoopAsync(CancellationToken token)
        {
            int pingsSent = 0;
            try
            {
                while (!token.IsCancellationRequested && IsConnected)
                {
                    long seq = ++_pingSequence;
                    byte[] pingBytes = ProtobufCodec.EncodePing(_serverClock.ElapsedRealtime, seq);
                    bool ok = await SendRawAsync(MessageTypes.Ping, pingBytes);
                    if (!ok) break;

                    pingsSent++;
                    int delayMs = (pingsSent < 3) ? 1000 : 15000;
                    await Task.Delay(delayMs, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogetherClient] Ping loop error: " + ex.Message);
            }
        }

        public async Task<bool> SendRawAsync(string msgType, byte[] payload)
        {
            if (_webSocket == null) return false;
            try
            {
                byte[] envelopeBytes = ProtobufCodec.EncodeEnvelope(msgType, payload, false);
                using (var writer = new DataWriter(_webSocket.OutputStream))
                {
                    writer.WriteBytes(envelopeBytes);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogetherClient] SendRawAsync failed for " + msgType + ": " + ex.Message);
                return false;
            }
        }

        private void OnWebSocketMessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            try
            {
                using (var reader = args.GetDataReader())
                {
                    byte[] data = new byte[reader.UnconsumedBufferLength];
                    reader.ReadBytes(data);

                    var env = ProtobufCodec.DecodeEnvelope(data);
                    HandleEnvelope(env);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogetherClient] Frame decode error: " + ex.Message);
            }
        }

        private void HandleEnvelope(Envelope env)
        {
            if (env == null || string.IsNullOrEmpty(env.Type)) return;

            switch (env.Type)
            {
                case MessageTypes.ServerCapabilities:
                    var caps = ProtobufCodec.DecodeServerCapabilities(env.Payload);
                    _handshakeTcs?.TrySetResult(caps);
                    return;

                case MessageTypes.Pong:
                    var pong = ProtobufCodec.DecodePong(env.Payload);
                    bool isFirst = _serverClock.RecordPong(pong.ClientTime, pong.ServerReceiveTime, pong.ServerSendTime);
                    if (isFirst)
                    {
                        Debug.WriteLine("[ListenTogetherClient] Server clock calibrated");
                        ClockReady?.Invoke();
                    }
                    return;

                case MessageTypes.RoomCreated:
                    var rc = ProtobufCodec.DecodeRoomCreated(env.Payload);
                    _sessionToken = rc.SessionToken;
                    break;

                case MessageTypes.JoinApproved:
                    var ja = ProtobufCodec.DecodeJoinApproved(env.Payload);
                    _sessionToken = ja.SessionToken;
                    break;

                case MessageTypes.Kicked:
                    _sessionToken = null;
                    break;

                case MessageTypes.Error:
                    var err = ProtobufCodec.DecodeError(env.Payload);
                    Debug.WriteLine("[ListenTogetherClient] Server error: " + err.Code + " - " + err.Message);
                    if (err.Code == "unsupported_client")
                    {
                        _isExplicitDisconnect = true; // Non-recoverable
                    }
                    break;
            }

            MessageReceived?.Invoke(env.Type, env.Payload);
        }

        private void OnWebSocketClosed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            Debug.WriteLine("[ListenTogetherClient] WebSocket closed: " + args.Reason);
            if (!_isExplicitDisconnect)
            {
                ScheduleReconnect(args.Reason);
            }
            else
            {
                Disconnected?.Invoke(args.Reason, false);
            }
        }

        private void ScheduleReconnect(string reason)
        {
            if (_isExplicitDisconnect || _isDisposed)
            {
                Disconnected?.Invoke(reason, false);
                return;
            }

            if (_reconnectAttempts >= 5)
            {
                Debug.WriteLine("[ListenTogetherClient] Reconnection attempts exhausted");
                _sessionToken = null;
                Disconnected?.Invoke("Could not reconnect after 5 attempts", false);
                return;
            }

            _reconnectAttempts++;
            Disconnected?.Invoke(reason, true);

            var timer = new System.Threading.Timer(async _ =>
            {
                Debug.WriteLine("[ListenTogetherClient] Reconnecting attempt " + _reconnectAttempts + "...");
                await StartConnectionAsync();
            }, null, _reconnectDelayMs, Timeout.Infinite);

            _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, 30000);
        }

        public void Disconnect()
        {
            _isExplicitDisconnect = true;
            _sessionToken = null;
            _reconnectAttempts = 0;
            CleanUpSocket();
            Disconnected?.Invoke("Disconnected by user", false);
        }

        private void CleanUpSocket()
        {
            try { _cts?.Cancel(); } catch { }
            if (_webSocket != null)
            {
                try
                {
                    _webSocket.MessageReceived -= OnWebSocketMessageReceived;
                    _webSocket.Closed -= OnWebSocketClosed;
                    _webSocket.Close(1000, "Closed");
                }
                catch { }
                _webSocket = null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Disconnect();
        }
    }
}
