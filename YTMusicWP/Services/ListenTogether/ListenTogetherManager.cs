using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace YTMusicWP.Services.ListenTogether
{
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed
    }

    public class RoomMember
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
        public bool IsHost { get; set; }
        public bool IsConnected { get; set; }
        public bool IsBuffering { get; set; }

        public string Initial => string.IsNullOrWhiteSpace(Username) ? "?" : Username.Trim().Substring(0, 1).ToUpper();

        public string StatusText
        {
            get
            {
                if (IsHost) return "Host";
                if (IsBuffering) return "Buffering…";
                if (IsConnected) return "In sync";
                return "Disconnected";
            }
        }
    }

    public class PendingJoinRequest
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
        public string Initial => string.IsNullOrWhiteSpace(Username) ? "?" : Username.Trim().Substring(0, 1).ToUpper();
    }

    public class ListenTogetherManager
    {
        private static ListenTogetherManager _instance;
        public static ListenTogetherManager Instance => _instance ?? (_instance = new ListenTogetherManager());

        private readonly ListenTogetherClient _client = new ListenTogetherClient();

        public ListenTogetherClient Client => _client;

        // State
        public ConnectionState Connection { get; private set; } = ConnectionState.Disconnected;
        public string ServerVersion { get; private set; } = "";
        public string RoomCode { get; private set; }
        public string SelfUserId { get; private set; } = "";
        public string SelfUsername { get; set; } = "Lumia";
        public bool IsHost { get; private set; }
        public bool InRoom => !string.IsNullOrEmpty(RoomCode);

        public TrackInfo CurrentTrack { get; private set; }
        public bool IsPlaying { get; private set; }
        public long Position { get; private set; }
        public List<TrackInfo> Queue { get; private set; } = new List<TrackInfo>();
        public List<string> WaitingFor { get; private set; } = new List<string>();
        public long LastActionServerTime { get; private set; }

        public string PendingJoinCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool AutoApproveJoins { get; set; } = false;
        public bool AutoApproveSuggestions { get; set; } = false;
        public bool SyncVolume { get; set; } = true;

        public ObservableCollection<RoomMember> Members { get; } = new ObservableCollection<RoomMember>();
        public ObservableCollection<PendingJoinRequest> JoinRequests { get; } = new ObservableCollection<PendingJoinRequest>();

        public event Action StateChanged;
        public event Action<PlaybackActionPayload> RemotePlaybackActionReceived;
        public event Action<TrackInfo> RemoteTrackChanged;

        private ListenTogetherManager()
        {
            _client.Connected += OnClientConnected;
            _client.Disconnected += OnClientDisconnected;
            _client.ClockReady += OnClientClockReady;
            _client.MessageReceived += OnClientMessageReceived;
        }

        public async Task ConnectAsync()
        {
            SetState(() =>
            {
                Connection = ConnectionState.Connecting;
                ErrorMessage = null;
            });
            await _client.ConnectAsync();
        }

        public void Disconnect()
        {
            _client.Disconnect();
            SetState(() =>
            {
                Connection = ConnectionState.Disconnected;
                RoomCode = null;
                IsHost = false;
                SelfUserId = "";
                Members.Clear();
                JoinRequests.Clear();
                CurrentTrack = null;
                WaitingFor.Clear();
                PendingJoinCode = null;
                ErrorMessage = null;
            });
        }

        public async Task CreateRoomAsync(string username)
        {
            SelfUsername = string.IsNullOrWhiteSpace(username) ? "Lumia" : username.Trim();
            byte[] payload = ProtobufCodec.EncodeCreateRoom(SelfUsername);
            await _client.SendRawAsync(MessageTypes.CreateRoom, payload);
        }

        public async Task JoinRoomAsync(string roomCode, string username)
        {
            SelfUsername = string.IsNullOrWhiteSpace(username) ? "Lumia" : username.Trim();
            string code = (roomCode ?? "").Trim().ToUpper();
            SetState(() =>
            {
                PendingJoinCode = code;
                ErrorMessage = null;
            });

            byte[] payload = ProtobufCodec.EncodeJoinRoom(code, SelfUsername);
            bool sent = await _client.SendRawAsync(MessageTypes.JoinRoom, payload);
            if (!sent)
            {
                SetState(() =>
                {
                    PendingJoinCode = null;
                    ErrorMessage = "Not connected to server";
                });
            }
        }

        public void CancelJoin()
        {
            SetState(() => { PendingJoinCode = null; });
        }

        public async Task LeaveRoomAsync()
        {
            await _client.SendRawAsync(MessageTypes.LeaveRoom, new byte[0]);
            SetState(() =>
            {
                RoomCode = null;
                IsHost = false;
                Members.Clear();
                JoinRequests.Clear();
                CurrentTrack = null;
                WaitingFor.Clear();
                PendingJoinCode = null;
            });
        }

        public async Task ApproveJoinAsync(string userId)
        {
            byte[] payload = ProtobufCodec.EncodeApproveJoin(userId);
            await _client.SendRawAsync(MessageTypes.ApproveJoin, payload);
            SetState(() =>
            {
                var req = JoinRequests.FirstOrDefault(r => r.UserId == userId);
                if (req != null) JoinRequests.Remove(req);
            });
        }

        public async Task RejectJoinAsync(string userId, string reason = "Declined by host")
        {
            byte[] payload = ProtobufCodec.EncodeRejectJoin(userId, reason);
            await _client.SendRawAsync(MessageTypes.RejectJoin, payload);
            SetState(() =>
            {
                var req = JoinRequests.FirstOrDefault(r => r.UserId == userId);
                if (req != null) JoinRequests.Remove(req);
            });
        }

        public async Task KickUserAsync(string userId, string reason = "")
        {
            byte[] payload = ProtobufCodec.EncodeKickUser(userId, reason);
            await _client.SendRawAsync(MessageTypes.KickUser, payload);
        }

        public async Task TransferHostAsync(string newHostId)
        {
            byte[] payload = ProtobufCodec.EncodeTransferHost(newHostId);
            await _client.SendRawAsync(MessageTypes.TransferHost, payload);
        }

        public async Task SendPlaybackActionAsync(string action, string trackId, long position, TrackInfo trackInfo, List<TrackInfo> queue = null, string queueTitle = "")
        {
            var p = new PlaybackActionPayload
            {
                Action = action,
                TrackId = trackId,
                Position = position,
                TrackInfo = trackInfo,
                Queue = queue ?? new List<TrackInfo>(),
                QueueTitle = queueTitle,
                CapturedAtServerTime = _client.ServerNow() ?? 0L
            };
            byte[] payload = ProtobufCodec.EncodePlaybackAction(p);
            await _client.SendRawAsync(MessageTypes.PlaybackAction, payload);
        }

        public async Task SendQueueAsync(List<TrackInfo> queue, string queueTitle = "")
        {
            var p = new PlaybackActionPayload
            {
                Action = PlaybackActions.SyncQueue,
                Queue = queue ?? new List<TrackInfo>(),
                QueueTitle = queueTitle,
                CapturedAtServerTime = _client.ServerNow() ?? 0L
            };
            byte[] payload = ProtobufCodec.EncodePlaybackAction(p);
            await _client.SendRawAsync(MessageTypes.PlaybackAction, payload);
        }

        public async Task ReportBufferReadyAsync(string trackId)
        {
            byte[] payload = ProtobufCodec.EncodeBufferReady(trackId);
            await _client.SendRawAsync(MessageTypes.BufferReady, payload);
        }

        public async Task RequestSyncAsync()
        {
            await _client.SendRawAsync(MessageTypes.RequestSync, new byte[0]);
        }

        public void ClearError()
        {
            SetState(() => { ErrorMessage = null; });
        }

        public long PositionAt(long position, bool isPlaying)
        {
            return _client.PositionAt(position, LastActionServerTime, isPlaying);
        }

        private void OnClientConnected(ServerCapabilities caps)
        {
            SetState(() =>
            {
                Connection = ConnectionState.Connected;
                ServerVersion = caps.ServerVersion;
                ErrorMessage = null;
            });
        }

        private void OnClientDisconnected(string reason, bool willRetry)
        {
            SetState(() =>
            {
                if (willRetry)
                {
                    Connection = ConnectionState.Connecting;
                }
                else
                {
                    Connection = ConnectionState.Failed;
                    ErrorMessage = reason ?? "Connection lost";
                    RoomCode = null;
                    IsHost = false;
                    Members.Clear();
                    JoinRequests.Clear();
                }
            });
        }

        private void OnClientClockReady()
        {
            SetState(() => { });
        }

        private void OnClientMessageReceived(string msgType, byte[] payload)
        {
            switch (msgType)
            {
                case MessageTypes.RoomCreated:
                    var rc = ProtobufCodec.DecodeRoomCreated(payload);
                    SetState(() =>
                    {
                        RoomCode = rc.RoomCode;
                        SelfUserId = rc.UserId;
                        IsHost = true;
                        Members.Clear();
                        Members.Add(new RoomMember
                        {
                            UserId = rc.UserId,
                            Username = SelfUsername,
                            IsHost = true,
                            IsConnected = true
                        });
                    });
                    break;

                case MessageTypes.JoinApproved:
                    var ja = ProtobufCodec.DecodeJoinApproved(payload);
                    SetState(() =>
                    {
                        RoomCode = ja.RoomCode;
                        SelfUserId = ja.UserId;
                        IsHost = false;
                        PendingJoinCode = null;
                        Members.Clear();
                        if (ja.State != null)
                        {
                            foreach (var u in ja.State.Users)
                            {
                                Members.Add(new RoomMember
                                {
                                    UserId = u.UserId,
                                    Username = u.Username,
                                    IsHost = u.IsHost,
                                    IsConnected = u.IsConnected
                                });
                            }
                            CurrentTrack = ja.State.CurrentTrack;
                            IsPlaying = ja.State.IsPlaying;
                            Position = ja.State.Position;
                            Queue = ja.State.Queue ?? new List<TrackInfo>();

                            if (CurrentTrack != null)
                            {
                                RemoteTrackChanged?.Invoke(CurrentTrack);
                            }
                            if (IsPlaying)
                            {
                                RemotePlaybackActionReceived?.Invoke(new PlaybackActionPayload
                                {
                                    Action = PlaybackActions.Play,
                                    Position = Position,
                                    TrackInfo = CurrentTrack
                                });
                            }
                        }
                    });
                    var ignored = RequestSyncAsync();
                    break;

                case MessageTypes.JoinRejected:
                    var jr = ProtobufCodec.DecodeJoinRejected(payload);
                    SetState(() =>
                    {
                        PendingJoinCode = null;
                        ErrorMessage = string.IsNullOrEmpty(jr.Reason) ? "The host declined your request" : jr.Reason;
                    });
                    break;

                case MessageTypes.JoinRequest:
                    var jreq = ProtobufCodec.DecodeJoinRequest(payload);
                    if (AutoApproveJoins)
                    {
                        var ign = ApproveJoinAsync(jreq.UserId);
                    }
                    else
                    {
                        SetState(() =>
                        {
                            if (!JoinRequests.Any(r => r.UserId == jreq.UserId))
                            {
                                JoinRequests.Add(new PendingJoinRequest
                                {
                                    UserId = jreq.UserId,
                                    Username = jreq.Username
                                });
                            }
                        });
                    }
                    break;

                case MessageTypes.UserJoined:
                    var uj = ProtobufCodec.DecodeUserJoined(payload);
                    SetState(() =>
                    {
                        if (!Members.Any(m => m.UserId == uj.UserId))
                        {
                            Members.Add(new RoomMember
                            {
                                UserId = uj.UserId,
                                Username = uj.Username,
                                IsHost = false,
                                IsConnected = true
                            });
                        }
                    });
                    break;

                case MessageTypes.UserLeft:
                    var ul = ProtobufCodec.DecodeUserLeft(payload);
                    SetState(() =>
                    {
                        var m = Members.FirstOrDefault(x => x.UserId == ul.UserId);
                        if (m != null) Members.Remove(m);
                    });
                    break;

                case MessageTypes.UserDisconnected:
                    var ud = ProtobufCodec.DecodeUserDisconnected(payload);
                    SetState(() =>
                    {
                        var m = Members.FirstOrDefault(x => x.UserId == ud.UserId);
                        if (m != null) m.IsConnected = false;
                    });
                    break;

                case MessageTypes.UserReconnected:
                    var ur = ProtobufCodec.DecodeUserReconnected(payload);
                    SetState(() =>
                    {
                        var m = Members.FirstOrDefault(x => x.UserId == ur.UserId);
                        if (m != null) m.IsConnected = true;
                    });
                    break;

                case MessageTypes.HostChanged:
                    var hc = ProtobufCodec.DecodeHostChanged(payload);
                    SetState(() =>
                    {
                        IsHost = (hc.NewHostId == SelfUserId);
                        foreach (var m in Members)
                        {
                            m.IsHost = (m.UserId == hc.NewHostId);
                        }
                    });
                    break;

                case MessageTypes.Kicked:
                    var kick = ProtobufCodec.DecodeKicked(payload);
                    SetState(() =>
                    {
                        RoomCode = null;
                        IsHost = false;
                        Members.Clear();
                        JoinRequests.Clear();
                        ErrorMessage = string.IsNullOrEmpty(kick.Reason) ? "You were removed from the room" : kick.Reason;
                    });
                    break;

                case MessageTypes.BufferWait:
                    var bw = ProtobufCodec.DecodeBufferWait(payload);
                    SetState(() =>
                    {
                        WaitingFor = bw.WaitingFor ?? new List<string>();
                        foreach (var m in Members)
                        {
                            m.IsBuffering = WaitingFor.Contains(m.UserId);
                        }
                    });
                    break;

                case MessageTypes.BufferComplete:
                    SetState(() =>
                    {
                        WaitingFor.Clear();
                        foreach (var m in Members)
                        {
                            m.IsBuffering = false;
                        }
                    });
                    break;

                case MessageTypes.SyncState:
                    var ss = ProtobufCodec.DecodeSyncState(payload);
                    SetState(() =>
                    {
                        CurrentTrack = ss.CurrentTrack ?? CurrentTrack;
                        IsPlaying = ss.IsPlaying;
                        Position = ss.Position;
                        if (ss.Queue != null && ss.Queue.Count > 0)
                        {
                            Queue = ss.Queue;
                        }
                        if (ss.CurrentTrack != null)
                        {
                            RemoteTrackChanged?.Invoke(ss.CurrentTrack);
                        }
                        if (ss.IsPlaying)
                        {
                            RemotePlaybackActionReceived?.Invoke(new PlaybackActionPayload
                            {
                                Action = PlaybackActions.Play,
                                Position = ss.Position,
                                TrackInfo = ss.CurrentTrack,
                                CapturedAtServerTime = ss.LastUpdate
                            });
                        }
                    });
                    break;

                case MessageTypes.SyncPlayback:
                case MessageTypes.PlaybackAction:
                    var act = ProtobufCodec.DecodePlaybackAction(payload);
                    SetState(() =>
                    {
                        var trackToUse = act.TrackInfo;
                        if (trackToUse == null && !string.IsNullOrEmpty(act.TrackId))
                        {
                            trackToUse = Queue.FirstOrDefault(t => t.Id == act.TrackId);
                            if (trackToUse == null)
                            {
                                trackToUse = new TrackInfo { Id = act.TrackId, Title = "Loading track..." };
                            }
                        }

                        CurrentTrack = trackToUse ?? CurrentTrack;
                        if (string.Equals(act.Action, PlaybackActions.Play, StringComparison.OrdinalIgnoreCase)) IsPlaying = true;
                        else if (string.Equals(act.Action, PlaybackActions.Pause, StringComparison.OrdinalIgnoreCase)) IsPlaying = false;

                        if (!string.Equals(act.Action, PlaybackActions.SyncQueue, StringComparison.OrdinalIgnoreCase))
                        {
                            Position = act.Position;
                        }
                        if (act.Queue != null && act.Queue.Count > 0)
                        {
                            Queue = act.Queue;
                        }
                        LastActionServerTime = act.CapturedAtServerTime;

                        RemotePlaybackActionReceived?.Invoke(act);
                        if (trackToUse != null)
                        {
                            RemoteTrackChanged?.Invoke(trackToUse);
                        }
                    });
                    break;

                case MessageTypes.Error:
                    var err = ProtobufCodec.DecodeError(payload);
                    if (err.Code != "rate_limited")
                    {
                        SetState(() =>
                        {
                            PendingJoinCode = null;
                            ErrorMessage = string.IsNullOrEmpty(err.Message) ? err.Code : err.Message;
                        });
                    }
                    break;
            }
        }

        private async void SetState(Action action)
        {
            if (CoreApplication.MainView != null && CoreApplication.MainView.CoreWindow != null)
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        action();
                        StateChanged?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[ListenTogetherManager] SetState error: " + ex.Message);
                    }
                });
            }
            else
            {
                try
                {
                    action();
                    StateChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[ListenTogetherManager] SetState error: " + ex.Message);
                }
            }
        }
    }
}
