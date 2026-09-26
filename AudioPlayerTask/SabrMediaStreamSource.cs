using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.Web.Http;
using YTMusicWP.Services;

namespace AudioPlayerTask
{
    /// <summary>
    /// Manages YouTube SABR (Server-side Adaptive Bitrate) / UMP (Universal Media Player) streaming
    /// directly into WinRT MediaStreamSource for continuous, memory-efficient playback on WP8.1.
    /// </summary>
    internal sealed class SabrMediaStreamSource : IDisposable
    {
        private static readonly byte[] BOX_TRUN = new byte[] { (byte)'t', (byte)'r', (byte)'u', (byte)'n' };
        private static readonly byte[] BOX_MDAT = new byte[] { (byte)'m', (byte)'d', (byte)'a', (byte)'t' };

        private MediaStreamSource _mss;
        private readonly object _queueLock = new object();
        private readonly Queue<MediaStreamSample> _sampleQueue = new Queue<MediaStreamSample>();

        private struct PendingRequest
        {
            public MediaStreamSourceSampleRequest Request;
            public MediaStreamSourceSampleRequestDeferral Deferral;
        }
        private readonly List<PendingRequest> _pendingRequests = new List<PendingRequest>();
        private readonly Dictionary<int, MemoryStream> _pendingSegments = new Dictionary<int, MemoryStream>();

        private class ParsedMediaHeader
        {
            public int HeaderId;
            public bool IsInitSeg;
            public int SequenceNumber = -1;
            public long StartMs;
            public long DurationMs;
        }
        private readonly Dictionary<int, ParsedMediaHeader> _pendingMediaHeaders = new Dictionary<int, ParsedMediaHeader>();

        private string _serverAbrUrl;
        private byte[] _ustreamerConfig;
        private string _userAgent;
        private string _clientName;
        private int _clientNameInt = 3;
        private string _clientVersion;
        private string _poToken;
        private byte[] _poTokenBytes;
        private byte[] _playbackCookieBytes = null;
        private Action<string> _logFunc;

        private long _sampleIndex = 0;
        private long _currentPositionMs = 0;
        private int _requestNumber = 0;
        private int _chunkParsedSamples = 0;
        private float _playbackRate = 1.0f;
        private bool _isDisposed = false;
        private int _lastSpsCode = 0;
        private int _consecutiveEmptyChunks = 0;
        private bool _formatsInitialized = false;
        private readonly HashSet<int> _downloadedSequences = new HashSet<int>();
        private int _firstMediaHeaderSeqNum = -1;
        private int _lastMediaHeaderSeqNum = -1;
        private long _lastMediaHeaderStartMs = 0;
        private long _lastMediaHeaderDurationMs = 0;
        private int _lastEnqueuedLiveSeqNum = -1;
        private int _liveHeadSeq = -1;
        private long _liveHeadTimeMs = -1;
        private const long LIVE_EDGE_SENTINEL_MS = 9007199254740991L; // Number.MAX_SAFE_INTEGER in googlevideo
        private long _liveHeadSeekTimeMs = 0;
        private int _serverBackoffMs = 0;
        private DateTime _lastSuccessfulChunkTime = DateTime.MinValue;
        private readonly bool _isLiveStream;

        private CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _streamingTask = null;
        private HttpClient _httpClient;

        public MediaStreamSource StreamSource { get { return _mss; } }
        public bool IsDisposed { get { return _isDisposed; } }
        public string LastError { get; private set; }

        public double BufferedSeconds
        {
            get
            {
                lock (_queueLock)
                {
                    return _sampleQueue.Count * (1024.0 / 44100.0);
                }
            }
        }

        public SabrMediaStreamSource(
            string serverAbrUrl,
            byte[] ustreamerConfig,
            string userAgent = null,
            string clientName = null,
            string clientVersion = null,
            string poToken = null,
            Action<string> logFunc = null,
            bool isLiveStream = false)
        {
            _serverAbrUrl = serverAbrUrl;
            _ustreamerConfig = ustreamerConfig;
            _userAgent = string.IsNullOrEmpty(userAgent)
                ? "Mozilla/5.0 (Windows Phone 8.1; ARM; Trident/7.0; Touch; rv:11.0; IEMobile/11.0) like iPhone"
                : userAgent;
            _clientName = clientName;
            _clientVersion = clientVersion;
            _poToken = poToken;
            _logFunc = logFunc;
            _isLiveStream = isLiveStream || IsLiveStreamUrl(serverAbrUrl);

            if (!string.IsNullOrEmpty(_poToken))
            {
                _poTokenBytes = MiniProtoWriter.Base64UrlDecode(_poToken);
            }

            int clientNameInt = 3;
            if (!string.IsNullOrEmpty(_clientName))
            {
                if (!int.TryParse(_clientName, out clientNameInt))
                {
                    switch (_clientName.ToUpperInvariant())
                    {
                        case "WEB": clientNameInt = 1; break;
                        case "MWEB": clientNameInt = 2; break;
                        case "ANDROID": clientNameInt = 3; break;
                        case "IOS": clientNameInt = 5; break;
                        case "TVHTML5": clientNameInt = 16; break;
                        case "ANDROID_VR": clientNameInt = 28; break;
                        case "WEB_REMIX": clientNameInt = 67; break;
                        case "VISIONOS": clientNameInt = 101; break;
                        default: clientNameInt = 3; break;
                    }
                }
            }
            _clientNameInt = clientNameInt;
            Log("Initialized for client=" + (_clientName ?? "?") + " (id=" + _clientNameInt + "), poToken=" + (!string.IsNullOrEmpty(_poToken) ? (_poToken.Length + " chars") : "NONE"));

            // Shared HTTP filter that ignores legacy SSL handshake anomalies and disables response caching
            var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter();
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Untrusted);
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Expired);
            filter.CacheControl.ReadBehavior = Windows.Web.Http.Filters.HttpCacheReadBehavior.MostRecent;
            filter.CacheControl.WriteBehavior = Windows.Web.Http.Filters.HttpCacheWriteBehavior.NoCache;
            _httpClient = new HttpClient(filter);

            // Audio encoding descriptor: AAC-ADTS 44.1kHz Stereo 128kbps (itag 140 baseline)
            var encodingProps = AudioEncodingProperties.CreateAacAdts(44100, 2, 128000);
            var streamDescriptor = new AudioStreamDescriptor(encodingProps);

            _mss = new MediaStreamSource(streamDescriptor);
            _mss.CanSeek = !_isLiveStream;
            _mss.BufferTime = TimeSpan.FromSeconds(2);

            _mss.SampleRequested += Mss_SampleRequested;
            _mss.Starting += Mss_Starting;
            _mss.Closed += Mss_Closed;
        }

        private void Log(string msg)
        {
            if (_logFunc != null)
            {
                _logFunc("[SabrMSS] " + msg);
            }
            else
            {
                Debug.WriteLine("[SabrMSS] " + msg);
            }
        }

        private void Mss_Starting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            var request = args.Request;
            if (!_isLiveStream && request.StartPosition.HasValue && request.StartPosition.Value > TimeSpan.FromMilliseconds(500))
            {
                TimeSpan startPos = request.StartPosition.Value;
                Log("Mss_Starting seek to " + startPos.TotalSeconds.ToString("F1") + "s");

                lock (_queueLock)
                {
                    _sampleQueue.Clear();
                    _sampleIndex = (long)(startPos.TotalSeconds * (44100.0 / 1024.0));
                    _currentPositionMs = (long)startPos.TotalMilliseconds;
                }

                // Restart background streaming loop at the requested seek position
                RestartStreamingAt(_currentPositionMs);
                request.SetActualStartPosition(startPos);
            }
            else
            {
                request.SetActualStartPosition(TimeSpan.Zero);
            }
        }

        private void Mss_SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            try
            {
                if (_isDisposed)
                {
                    try { args.Request.Sample = null; } catch { }
                    return;
                }
                var request = args.Request;

                lock (_queueLock)
                {
                    if (_sampleQueue.Count > 0)
                    {
                        var sample = _sampleQueue.Dequeue();
                        request.Sample = sample;
                        _currentPositionMs = (long)sample.Timestamp.TotalMilliseconds;
                        return;
                    }

                    if ((_cts != null && _cts.IsCancellationRequested) || _isDisposed || (_streamingTask != null && _streamingTask.IsCompleted))
                    {
                        // No more samples and streaming loop ended -> EOS
                        request.Sample = null;
                        return;
                    }

                    // Buffer is draining, hold deferral until next UMP MEDIA part is parsed
                    _pendingRequests.Add(new PendingRequest { Request = request, Deferral = request.GetDeferral() });
                }
            }
            catch (Exception ex)
            {
                Log("SampleRequested error: " + ex.Message);
            }
        }

        private void Mss_Closed(MediaStreamSource sender, MediaStreamSourceClosedEventArgs args)
        {
            Log("Mss_Closed: " + args.Request.Reason);
            Dispose();
        }

        public async Task<bool> PreloadInitialChunkAsync(CancellationToken ct)
        {
            try
            {
                Log("Preloading initial SABR chunk rn=0...");
                int beforeCount = 0;
                lock (_queueLock) { beforeCount = _sampleQueue.Count; }

                int samples0 = await FetchChunkAsync(0, ct).ConfigureAwait(false);
                if (samples0 > 0)
                {
                    _requestNumber = 1;
                    _formatsInitialized = true;
                    _lastSuccessfulChunkTime = DateTime.UtcNow;
                    Log("Preload done: parsed " + samples0 + " samples (~" + BufferedSeconds.ToString("F1") + "s buffered)");

                    if (_isLiveStream)
                    {
                        // For live streams, immediately preload rn=1 over HTTP Keep-Alive.
                        // This provides ~10s of audio cushion (430 samples) before starting playback,
                        // preventing Media Foundation audio sink clock-drift acceleration (catch-up "giật tua").
                        try
                        {
                            int samples1 = await FetchChunkAsync(1, ct).ConfigureAwait(false);
                            if (samples1 > 0)
                            {
                                _requestNumber = 2;
                                _lastSuccessfulChunkTime = DateTime.UtcNow;
                                Log("Live stream rn=1 preloaded: total " + (samples0 + samples1) + " samples (~" + BufferedSeconds.ToString("F1") + "s buffered)");
                            }
                        }
                        catch (Exception ex1)
                        {
                            Log("Warning: rn=1 preload failed (" + ex1.Message + "), will stream in background loop");
                        }
                    }

                    return true;
                }
                else if (samples0 == 0 && string.IsNullOrEmpty(LastError))
                {
                    Log("rn=0 delivered init segment. Preloading rn=1 for audio samples...");
                    int samples1 = await FetchChunkAsync(1, ct).ConfigureAwait(false);
                    if (samples1 > 0)
                    {
                        _requestNumber = 2;
                        _formatsInitialized = true;
                        _lastSuccessfulChunkTime = DateTime.UtcNow;
                        Log("Preload done at rn=1: parsed " + samples1 + " samples (~" + BufferedSeconds.ToString("F1") + "s buffered)");

                        if (_isLiveStream)
                        {
                            try
                            {
                                int samples2 = await FetchChunkAsync(2, ct).ConfigureAwait(false);
                                if (samples2 > 0)
                                {
                                    _requestNumber = 3;
                                    _lastSuccessfulChunkTime = DateTime.UtcNow;
                                    Log("Live stream rn=2 preloaded: total " + (samples1 + samples2) + " samples (~" + BufferedSeconds.ToString("F1") + "s buffered)");
                                }
                            }
                            catch (Exception ex2)
                            {
                                Log("Warning: rn=2 preload failed (" + ex2.Message + "), will stream in background loop");
                            }
                        }

                        return true;
                    }
                    LastError = "No audio samples received in initial chunks";
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log("Preload exception: " + ex.Message);
            }
            return false;
        }

        public void StartStreaming(float playbackRate = 1.0f)
        {
            _playbackRate = playbackRate;
            if (_streamingTask == null || _streamingTask.IsCompleted)
            {
                var token = _cts.Token;
                _streamingTask = Task.Run(() => StreamingLoopAsync(token), token);
            }
        }

        private void RestartStreamingAt(long positionMs)
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); _cts.Dispose(); } catch { }
            }
            _cts = new CancellationTokenSource();
            _currentPositionMs = positionMs;
            _requestNumber = 0;
            _lastSpsCode = 0;
            _consecutiveEmptyChunks = 0;
            _formatsInitialized = false;
            _downloadedSequences.Clear();
            _firstMediaHeaderSeqNum = -1;
            _lastMediaHeaderSeqNum = -1;
            _lastMediaHeaderStartMs = 0;
            _lastMediaHeaderDurationMs = 0;
            _lastEnqueuedLiveSeqNum = -1;
            _liveHeadSeq = -1;
            _liveHeadTimeMs = -1;
            _liveHeadSeekTimeMs = 0;
            _serverBackoffMs = 0;
            _pendingMediaHeaders.Clear();
            _lastSuccessfulChunkTime = DateTime.MinValue;

            var token = _cts.Token;
            _streamingTask = Task.Run(() => StreamingLoopAsync(token), token);
        }

        private async Task<int> FetchChunkAsync(int rn, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_serverAbrUrl))
            {
                LastError = "Server ABR URL is null or empty";
                Log(LastError);
                return -1;
            }

            lock (_queueLock) { _chunkParsedSamples = 0; }

            // Build target URL with request number sequence (rn), ump=1, srfvp=1, and pot=<token>
            string requestUrl = _serverAbrUrl;
            string sep = requestUrl.Contains("?") ? "&" : "?";
            requestUrl += sep + "rn=" + rn + "&ump=1&srfvp=1";
            if (!string.IsNullOrEmpty(_poToken))
            {
                requestUrl += "&pot=" + Uri.EscapeDataString(_poToken);
            }

            // Compute fetched position from _sampleIndex (what we've already enqueued),
            // NOT _currentPositionMs (what the player has consumed).
            // This tells the server the correct offset so it sends the NEXT chunk, not a repeat.
            long fetchedPositionMs;
            lock (_queueLock)
            {
                fetchedPositionMs = (long)(_sampleIndex * 1024.0 / 44100.0 * 1000.0);
            }

            int startSeq = _firstMediaHeaderSeqNum;
            int endSeq = _lastMediaHeaderSeqNum;
            long segStartTimeMs = 0;
            long segDurationMs = fetchedPositionMs;
            long requestPlayerTimeMs;

            if (_isLiveStream)
            {
                // In live streams, tell YouTube to start/stream at the live edge.
                // Initial request uses LIVE_EDGE_SENTINEL_MS (9007199254740991) matching googlevideo/YouTube.js
                // so the server serves live edge audio instead of 50-minute-old DVR history.
                // Once streaming, track the latest segment start time.
                if (_lastMediaHeaderStartMs > 0)
                {
                    requestPlayerTimeMs = _lastMediaHeaderStartMs;
                }
                else if (_liveHeadSeekTimeMs > 0)
                {
                    requestPlayerTimeMs = _liveHeadSeekTimeMs;
                }
                else
                {
                    requestPlayerTimeMs = LIVE_EDGE_SENTINEL_MS;
                }

                if (_lastMediaHeaderSeqNum >= 0)
                {
                    startSeq = _lastMediaHeaderSeqNum;
                    endSeq = _lastMediaHeaderSeqNum;
                    segStartTimeMs = _lastMediaHeaderStartMs;
                    segDurationMs = _lastMediaHeaderDurationMs > 0 ? _lastMediaHeaderDurationMs : 5000;
                }
            }
            else
            {
                requestPlayerTimeMs = fetchedPositionMs;
            }

            // Build Protobuf VideoPlaybackAbrRequest
            byte[] requestBody = MiniProtoWriter.BuildAudioAbrRequest(
                _ustreamerConfig,
                requestPlayerTimeMs,
                _playbackRate,
                140, // 140 = AAC 128kbps itag
                _clientNameInt,
                !string.IsNullOrEmpty(_clientVersion) ? _clientVersion : "19.29.35",
                _poTokenBytes,
                _playbackCookieBytes,
                _formatsInitialized,
                segDurationMs,
                startSeq,
                endSeq,
                segStartTimeMs);

            var resolvedInfo = await SecureDnsResolver.RewriteUrlAsync(requestUrl).ConfigureAwait(false);

            using (var req = new HttpRequestMessage(HttpMethod.Post, new Uri(resolvedInfo.Url)))
            {
                if (resolvedInfo.WasResolved && !string.IsNullOrEmpty(resolvedInfo.OriginalHost))
                {
                    req.Headers.Host = new Windows.Networking.HostName(resolvedInfo.OriginalHost);
                }

                req.Headers.TryAppendWithoutValidation("User-Agent", _userAgent);
                req.Headers.TryAppendWithoutValidation("Accept", "application/vnd.yt-ump");
                req.Headers.TryAppendWithoutValidation("Accept-Encoding", "identity");

                req.Headers.TryAppendWithoutValidation("X-YouTube-Client-Name", _clientNameInt.ToString());
                if (!string.IsNullOrEmpty(_clientVersion))
                    req.Headers.TryAppendWithoutValidation("X-YouTube-Client-Version", _clientVersion);

                // Attach protobuf body
                req.Content = new HttpBufferContent(requestBody.AsBuffer());
                req.Content.Headers.TryAppendWithoutValidation("Content-Type", "application/x-protobuf");

                using (var resp = await _httpClient.SendRequestAsync(req, HttpCompletionOption.ResponseHeadersRead).AsTask(ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        LastError = "HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase;
                        Log("HTTP " + (int)resp.StatusCode + " on rn=" + rn);
                        return -1;
                    }

                    // Stream-read UMP parts directly from network response stream
                    using (var winrtStream = await resp.Content.ReadAsInputStreamAsync().AsTask(ct).ConfigureAwait(false))
                    using (var netStream = winrtStream.AsStreamForRead())
                    {
                        await UmpParser.ProcessStreamAsync(netStream, OnUmpPartReceived, ct).ConfigureAwait(false);
                    }

                    // Flush any completed media segments that were missing a trailing MEDIA_END
                    FlushPendingSegments();
                }
            }

            int parsed = 0;
            lock (_queueLock) { parsed = _chunkParsedSamples; }
            return parsed;
        }

        private async Task StreamingLoopAsync(CancellationToken ct)
        {
            Log("Starting Sabr streaming loop at rn=" + _requestNumber + ", pos=" + _currentPositionMs + "ms");

            while (!ct.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    // Flow control: keep ~15s buffer in RAM (~650 samples, ~300KB)
                    // Caps RAM footprint (critical for 512MB WP8.1) while providing comfortable cushion for weak networks
                    int count = 0;
                    lock (_queueLock) { count = _sampleQueue.Count; }
                    if (count >= 650)
                    {
                        await Task.Delay(1500, ct).ConfigureAwait(false);
                        continue;
                    }

                    // Respect server backoff policy if specified
                    if (_serverBackoffMs > 0)
                    {
                        int backoff = _serverBackoffMs;
                        _serverBackoffMs = 0;
                        Log("Respecting server backoff policy: waiting " + backoff + "ms...");
                        await Task.Delay(backoff, ct).ConfigureAwait(false);
                    }

                    // Pacing guard: each chunk is ~5.0s of audio.
                    // The live encoder on YouTube generates chunks in real-time (~5.0s intervals).
                    // If buffer is healthy (>= 430 samples, ~10s) and we just received a chunk less than 3.5s ago,
                    // wait for the remaining time so the encoder has time to produce the next segment.
                    // If buffer is low (< 430 samples), do NOT wait: fetch immediately to build up safety cushion!
                    if (_isLiveStream && count >= 430 && _lastSuccessfulChunkTime > DateTime.MinValue)
                    {
                        double elapsedSinceLast = (DateTime.UtcNow - _lastSuccessfulChunkTime).TotalSeconds;
                        if (elapsedSinceLast < 3.5)
                        {
                            int waitMs = (int)((3.5 - elapsedSinceLast) * 1000);
                            if (waitMs > 100)
                            {
                                await Task.Delay(waitMs, ct).ConfigureAwait(false);
                            }
                        }
                    }

                    int currentRn = _requestNumber;
                    _requestNumber++;

                    int newSamples = await FetchChunkAsync(currentRn, ct).ConfigureAwait(false);
                    if (newSamples < 0)
                    {
                        // HTTP/network error: wait 2s and retry next request
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (newSamples == 0)
                    {
                        if (_lastSpsCode == 3)
                        {
                            LastError = "YouTube Stream Protection: Attestation required (SPS code 3)";
                            Log(LastError + ", stopping SABR streaming loop.");
                            break;
                        }

                        _consecutiveEmptyChunks++;
                        if (!_isLiveStream && _consecutiveEmptyChunks >= 5)
                        {
                            LastError = "End of track reached (no audio after " + _consecutiveEmptyChunks + " attempts)";
                            Log(LastError + ", finishing SABR streaming loop.");
                            break;
                        }
                        else if (_isLiveStream && _consecutiveEmptyChunks >= 120)
                        {
                            LastError = "Live stream stalled (no audio after " + _consecutiveEmptyChunks + " attempts)";
                            Log(LastError + ", stopping SABR streaming loop.");
                            break;
                        }

                        // Server returned 0 audio samples (e.g. reached live head or duplicate chunk skipped).
                        int waitMs = _isLiveStream ? Math.Min(1000 + _consecutiveEmptyChunks * 200, 2200) : 1500;
                        Log("No audio in rn=" + currentRn + " (attempt " + _consecutiveEmptyChunks + "), waiting " + (waitMs / 1000.0).ToString("F1") + "s for next live segment...");
                        await Task.Delay(waitMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    _consecutiveEmptyChunks = 0;
                    _formatsInitialized = true;
                    _lastSuccessfulChunkTime = DateTime.UtcNow;

                    // Periodically trigger GC every 8 chunks (~40s) to reclaim native COM wrappers and prevent OOM on 512MB WP8.1
                    if (_requestNumber % 8 == 0)
                    {
                        try { GC.Collect(); } catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Log("StreamingLoop exception: " + ex.Message);
                    try { await Task.Delay(1500, ct).ConfigureAwait(false); } catch { }
                }
            }

            Log("Sabr streaming loop terminated");
            lock (_queueLock)
            {
                while (_pendingRequests.Count > 0)
                {
                    var p = _pendingRequests[0];
                    _pendingRequests.RemoveAt(0);
                    p.Request.Sample = null;
                    try { p.Deferral.Complete(); } catch { }
                }
            }
        }

        private void OnUmpPartReceived(UmpPart part)
        {
            if (_isDisposed) return;

            switch (part.Type)
            {
                case UmpPartId.MEDIA:
                    // In UMP, byte 0 is headerId, byte 1..N is the raw fMP4 chunk
                    if (part.Data != null && part.Data.Length > 1)
                    {
                        int headerId = part.Data[0];
                        lock (_queueLock)
                        {
                            MemoryStream ms;
                            if (!_pendingSegments.TryGetValue(headerId, out ms))
                            {
                                ms = new MemoryStream();
                                _pendingSegments[headerId] = ms;
                            }
                            ms.Write(part.Data, 1, part.Data.Length - 1);
                        }
                    }
                    break;

                case UmpPartId.MEDIA_END:
                    if (part.Data != null && part.Data.Length > 0)
                    {
                        int headerId = part.Data[0];
                        byte[] segBytes = null;
                        ParsedMediaHeader segHeader = null;
                        lock (_queueLock)
                        {
                            MemoryStream ms;
                            if (_pendingSegments.TryGetValue(headerId, out ms))
                            {
                                _pendingSegments.Remove(headerId);
                                segBytes = ms.ToArray();
                                ms.Dispose();
                            }
                            _pendingMediaHeaders.TryGetValue(headerId, out segHeader);
                            _pendingMediaHeaders.Remove(headerId);
                        }

                        if (segBytes != null && segBytes.Length > 0)
                        {
                            bool isInit = (segHeader != null) ? segHeader.IsInitSeg : false;
                            int seq = (segHeader != null) ? segHeader.SequenceNumber : -1;
                            long startMs = (segHeader != null) ? segHeader.StartMs : 0;
                            long durMs = (segHeader != null) ? segHeader.DurationMs : 0;

                            // Skip init segments (moov/ftyp only, no playable audio)
                            if (isInit)
                            {
                                Log("Skipping init segment " + headerId + " (" + segBytes.Length + " bytes)");
                                break;
                            }

                            if (_isLiveStream)
                            {
                                // Discard stale DVR history from cold start (e.g. segment from before SABR_SEEK live head)
                                if (_liveHeadSeekTimeMs > 0 && startMs > 0 && startMs < _liveHeadSeekTimeMs - 30000)
                                {
                                    Log("Discarding stale DVR segment seq=" + seq + " startMs=" + startMs + " (" + segBytes.Length + " bytes, live seek is " + _liveHeadSeekTimeMs + ")");
                                    break;
                                }

                                // Enforce monotonic sequence order in live streams (never jump backwards or replay)
                                if (_lastEnqueuedLiveSeqNum >= 0 && seq >= 0 && seq <= _lastEnqueuedLiveSeqNum)
                                {
                                    Log("Skipping duplicate/out-of-order live seq=" + seq + " (last enqueued was " + _lastEnqueuedLiveSeqNum + ")");
                                    break;
                                }
                            }
                            else
                            {
                                // Deduplicate for VOD: skip if we already parsed this sequence number
                                if (seq >= 0 && _downloadedSequences.Contains(seq))
                                {
                                    Log("Skipping duplicate seq=" + seq + " (" + segBytes.Length + " bytes)");
                                    break;
                                }
                            }

                            int samples = ParseAndEnqueueFmp4(segBytes, 0, segBytes.Length);
                            if (samples > 0)
                            {
                                if (seq >= 0)
                                {
                                    if (_firstMediaHeaderSeqNum < 0) _firstMediaHeaderSeqNum = seq;
                                    _lastMediaHeaderSeqNum = seq;
                                    _lastMediaHeaderStartMs = startMs;
                                    _lastMediaHeaderDurationMs = durMs;
                                    _downloadedSequences.Add(seq);
                                    if (_isLiveStream) _lastEnqueuedLiveSeqNum = seq;

                                    if (_downloadedSequences.Count > 80)
                                    {
                                        int threshold = seq - 40;
                                        var toRemove = new List<int>();
                                        foreach (int s in _downloadedSequences)
                                        {
                                            if (s < threshold) toRemove.Add(s);
                                        }
                                        for (int r = 0; r < toRemove.Count; r++)
                                        {
                                            _downloadedSequences.Remove(toRemove[r]);
                                        }
                                    }
                                }
                                _lastSuccessfulChunkTime = DateTime.UtcNow;
                                Log("Segment " + headerId + " seq=" + seq + " finalized: " + samples + " samples parsed (" + segBytes.Length + " bytes)");
                            }
                            else
                            {
                                Log("Segment " + headerId + " received (" + segBytes.Length + " bytes, no audio samples)");
                            }
                        }
                    }
                    break;

                case UmpPartId.MEDIA_HEADER:
                    // Parse MediaHeader protobuf: field 1 = header_id, field 8 = isInitSeg, field 9 = sequenceNumber, field 11 = start_ms, field 12 = duration_ms
                    var parsedHeader = new ParsedMediaHeader();
                    if (part.Data != null && part.Data.Length > 2)
                    {
                        int idx = 0;
                        while (idx < part.Data.Length)
                        {
                            int b = part.Data[idx++];
                            int fld = b >> 3;
                            int wt = b & 0x07;
                            if (wt == 0) // varint
                            {
                                long val = 0; int shift = 0;
                                while (idx < part.Data.Length)
                                {
                                    byte vb = part.Data[idx++];
                                    val |= (long)(vb & 0x7F) << shift;
                                    if ((vb & 0x80) == 0) break;
                                    shift += 7;
                                }
                                if (fld == 1) parsedHeader.HeaderId = (int)val;
                                else if (fld == 8) parsedHeader.IsInitSeg = (val != 0);
                                else if (fld == 9) parsedHeader.SequenceNumber = (int)val;
                                else if (fld == 11) parsedHeader.StartMs = val;
                                else if (fld == 12) parsedHeader.DurationMs = val;
                            }
                            else if (wt == 2) // length-delimited
                            {
                                int len = 0; int shift = 0;
                                while (idx < part.Data.Length)
                                {
                                    byte lb = part.Data[idx++];
                                    len |= (lb & 0x7F) << shift;
                                    if ((lb & 0x80) == 0) break;
                                    shift += 7;
                                }
                                idx += len;
                            }
                            else if (wt == 5) { idx += 4; } // fixed32
                            else if (wt == 1) { idx += 8; } // fixed64
                            else break;
                        }
                    }
                    lock (_queueLock)
                    {
                        _pendingMediaHeaders[parsedHeader.HeaderId] = parsedHeader;
                    }
                    Log("Received MEDIA_HEADER (headerId=" + parsedHeader.HeaderId + ", seq=" + parsedHeader.SequenceNumber + ", startMs=" + parsedHeader.StartMs + ", dur=" + parsedHeader.DurationMs + "ms, init=" + parsedHeader.IsInitSeg + ")");
                    break;

                case UmpPartId.LIVE_METADATA:
                    var liveMeta = UmpParser.ExtractLiveMetadata(part.Data);
                    if (liveMeta.HeadSequenceNumber > 0)
                    {
                        _liveHeadSeq = (int)liveMeta.HeadSequenceNumber;
                        _liveHeadTimeMs = liveMeta.HeadTimeMs;
                        Log("Received LIVE_METADATA: headSeq=" + _liveHeadSeq + ", headTimeMs=" + _liveHeadTimeMs + " (" + part.Size + " bytes)");
                    }
                    else
                    {
                        Log("Received LIVE_METADATA (" + part.Size + " bytes)");
                    }
                    break;

                case UmpPartId.FORMAT_INITIALIZATION_METADATA:
                    Log("Received FORMAT_INITIALIZATION_METADATA (" + part.Size + " bytes)");
                    break;

                case UmpPartId.SABR_REDIRECT:
                    string newUrl = UmpParser.ExtractSabrRedirectUrl(part.Data);
                    if (!string.IsNullOrEmpty(newUrl))
                    {
                        Log("SABR Redirect: " + newUrl);
                        _serverAbrUrl = newUrl;
                    }
                    break;

                case UmpPartId.SABR_ERROR:
                    string errDetail = UmpParser.ExtractSabrError(part.Data);
                    LastError = "SABR Server Error: " + errDetail;
                    Log(LastError);
                    break;

                case UmpPartId.NEXT_REQUEST_POLICY:
                    var nrp = UmpParser.ExtractNextRequestPolicy(part.Data);
                    if (nrp.PlaybackCookie != null && nrp.PlaybackCookie.Length > 0)
                    {
                        _playbackCookieBytes = nrp.PlaybackCookie;
                        Log("Extracted playback cookie (" + nrp.PlaybackCookie.Length + " bytes)");
                    }
                    if (nrp.BackoffTimeMs > 0)
                    {
                        _serverBackoffMs = nrp.BackoffTimeMs;
                        Log("Server requested backoff: " + _serverBackoffMs + "ms");
                    }
                    break;

                case UmpPartId.SABR_SEEK:
                    var seek = UmpParser.ExtractSabrSeek(part.Data);
                    if (seek.SeekTimeMs > 0)
                    {
                        _liveHeadSeekTimeMs = seek.SeekTimeMs;
                        Log("Received SABR_SEEK: seekTimeMs=" + _liveHeadSeekTimeMs + " (source=" + seek.SeekSource + ", " + part.Size + " bytes)");

                        // If any samples were enqueued from a cold-start DVR segment before this live seek head, purge them
                        if (_isLiveStream && _lastMediaHeaderStartMs > 0 && _lastMediaHeaderStartMs < _liveHeadSeekTimeMs - 30000)
                        {
                            lock (_queueLock)
                            {
                                Log("Purging " + _sampleQueue.Count + " samples from DVR segment before SABR_SEEK head (" + _lastMediaHeaderStartMs + " vs " + _liveHeadSeekTimeMs + ")");
                                _sampleQueue.Clear();
                                _sampleIndex = 0;
                                _currentPositionMs = 0;
                                _lastEnqueuedLiveSeqNum = -1;
                                _lastMediaHeaderStartMs = 0;
                                _lastMediaHeaderSeqNum = -1;
                            }
                        }
                    }
                    else
                    {
                        Log("Received SABR_SEEK (" + part.Size + " bytes)");
                    }
                    break;

                case UmpPartId.STREAM_PROTECTION_STATUS:
                    int spsCode = UmpParser.ExtractStreamProtectionStatus(part.Data);
                    _lastSpsCode = spsCode;
                    string spsDesc = spsCode == 1 ? "OK / Verified" : (spsCode == 2 ? "Attestation Pending" : (spsCode == 3 ? "Attestation Required" : "Code " + spsCode));
                    Log("Received STREAM_PROTECTION_STATUS: code " + spsCode + " (" + spsDesc + ", " + part.Size + " bytes)");
                    break;

                default:
                    Log("Received UMP part " + part.Type + " (" + part.Size + " bytes)");
                    break;
            }
        }

        private void FlushPendingSegments()
        {
            lock (_queueLock)
            {
                var keys = new List<int>(_pendingSegments.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    int hid = keys[i];
                    MemoryStream ms = _pendingSegments[hid];
                    _pendingSegments.Remove(hid);
                    byte[] segBytes = ms.ToArray();
                    ms.Dispose();

                    ParsedMediaHeader segHeader = null;
                    _pendingMediaHeaders.TryGetValue(hid, out segHeader);
                    _pendingMediaHeaders.Remove(hid);

                    if (segBytes.Length > 0)
                    {
                        bool isInit = (segHeader != null) ? segHeader.IsInitSeg : false;
                        int seq = (segHeader != null) ? segHeader.SequenceNumber : -1;
                        long startMs = (segHeader != null) ? segHeader.StartMs : 0;
                        long durMs = (segHeader != null) ? segHeader.DurationMs : 0;

                        if (isInit) continue;

                        if (_isLiveStream)
                        {
                            if (_liveHeadSeekTimeMs > 0 && startMs > 0 && startMs < _liveHeadSeekTimeMs - 30000) continue;
                            if (_lastEnqueuedLiveSeqNum >= 0 && seq >= 0 && seq <= _lastEnqueuedLiveSeqNum) continue;
                        }
                        else
                        {
                            if (seq >= 0 && _downloadedSequences.Contains(seq)) continue;
                        }

                        int samples = ParseAndEnqueueFmp4(segBytes, 0, segBytes.Length);
                        if (samples > 0)
                        {
                            if (seq >= 0)
                            {
                                if (_firstMediaHeaderSeqNum < 0) _firstMediaHeaderSeqNum = seq;
                                _lastMediaHeaderSeqNum = seq;
                                _lastMediaHeaderStartMs = startMs;
                                _lastMediaHeaderDurationMs = durMs;
                                _downloadedSequences.Add(seq);
                                if (_isLiveStream) _lastEnqueuedLiveSeqNum = seq;
                            }
                            Log("Flushed segment " + hid + " seq=" + seq + ": " + samples + " samples (" + segBytes.Length + " bytes)");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Lightweight fMP4 parser: extracts AAC frames from trun box and mdat payload,
        /// prepends standard 7-byte ADTS header, and enqueues MediaStreamSample instances.
        /// </summary>
        private int ParseAndEnqueueFmp4(byte[] chunk, int offset, int length)
        {
            if (chunk == null || length < 64 || offset + length > chunk.Length) return 0;

            int trunPos = FindBox(chunk, BOX_TRUN, offset, offset + length);
            int mdatPos = FindBox(chunk, BOX_MDAT, offset, offset + length);
            if (trunPos < 4 || mdatPos < 4) return 0;

            int trunHeader = trunPos - 4;
            if (trunHeader + 16 > offset + length) return 0;

            int flags = (chunk[trunHeader + 9] << 16) | (chunk[trunHeader + 10] << 8) | chunk[trunHeader + 11];
            int sampleCount = ReadInt32BE(chunk, trunHeader + 12);
            if (sampleCount <= 0 || sampleCount > 2000) return 0;

            int cur = trunHeader + 16;
            if ((flags & 0x000001) != 0) cur += 4; // data_offset
            if ((flags & 0x000004) != 0) cur += 4; // first_sample_flags

            bool hasDuration = (flags & 0x000100) != 0;
            bool hasSize = (flags & 0x000200) != 0;
            bool hasFlags = (flags & 0x000400) != 0;
            bool hasCompTime = (flags & 0x000800) != 0;

            int[] sampleSizes = new int[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                if (cur >= offset + length) break;
                if (hasDuration) cur += 4;
                int sz = hasSize ? ReadInt32BE(chunk, cur) : 0;
                cur += 4;
                if (hasFlags) cur += 4;
                if (hasCompTime) cur += 4;
                sampleSizes[i] = sz;
            }

            int rawOffset = mdatPos + 4;
            if (rawOffset >= offset + length) return 0;

            var sampleDuration = TimeSpan.FromTicks(232199); // 1024 samples at 44100Hz (~23.22ms)
            int parsedCount = 0;

            lock (_queueLock)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    int rawLen = sampleSizes[i];
                    if (rawLen <= 0 || rawOffset + rawLen > offset + length) break;

                    // Build standard 7-byte ADTS header for AAC-LC Stereo 44.1kHz
                    int flen = 7 + rawLen;
                    byte[] adtsFrame = new byte[flen];
                    adtsFrame[0] = 0xFF;
                    adtsFrame[1] = 0xF1;
                    adtsFrame[2] = 0x50;
                    adtsFrame[3] = (byte)(0x80 | ((flen >> 11) & 0x03));
                    adtsFrame[4] = (byte)((flen >> 3) & 0xFF);
                    adtsFrame[5] = (byte)(((flen & 0x07) << 5) | 0x1F);
                    adtsFrame[6] = 0xFC;
                    System.Buffer.BlockCopy(chunk, rawOffset, adtsFrame, 7, rawLen);
                    rawOffset += rawLen;

                    // Monotonic continuous timestamping
                    long ticks = (long)(_sampleIndex * (1024.0 / 44100.0 * 10000000.0));
                    var timestamp = TimeSpan.FromTicks(ticks);
                    _sampleIndex++;

                    var sample = MediaStreamSample.CreateFromBuffer(adtsFrame.AsBuffer(), timestamp);
                    sample.Duration = sampleDuration;
                    sample.KeyFrame = true;

                    _sampleQueue.Enqueue(sample);
                    parsedCount++;
                    _chunkParsedSamples++;
                }

                // Fulfill waiting deferrals immediately
                while (_pendingRequests.Count > 0 && _sampleQueue.Count > 0)
                {
                    var p = _pendingRequests[0];
                    _pendingRequests.RemoveAt(0);
                    var s = _sampleQueue.Dequeue();
                    p.Request.Sample = s;
                    _currentPositionMs = (long)s.Timestamp.TotalMilliseconds;
                    try { p.Deferral.Complete(); } catch { }
                }
            }

            return parsedCount;
        }

        private static int FindBox(byte[] data, byte[] boxType, int start, int end)
        {
            if (data == null) return -1;
            int limit = Math.Min(data.Length, end) - boxType.Length;
            for (int i = start; i <= limit; i++)
            {
                if (data[i] == boxType[0] &&
                    data[i + 1] == boxType[1] &&
                    data[i + 2] == boxType[2] &&
                    data[i + 3] == boxType[3])
                {
                    return i;
                }
            }
            return -1;
        }

        private static int ReadInt32BE(byte[] data, int offset)
        {
            return (int)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static bool IsLiveStreamUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf("live=1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("live/1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("yt_live_broadcast", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("hls_variant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("hls_playlist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_cts != null)
            {
                try { _cts.Cancel(); _cts.Dispose(); } catch { }
                _cts = null;
            }

            lock (_queueLock)
            {
                while (_pendingRequests.Count > 0)
                {
                    var p = _pendingRequests[0];
                    _pendingRequests.RemoveAt(0);
                    try
                    {
                        p.Request.Sample = null;
                        p.Deferral.Complete();
                    }
                    catch { }
                }
                _sampleQueue.Clear();

                foreach (var kvp in _pendingSegments)
                {
                    try { kvp.Value.Dispose(); } catch { }
                }
                _pendingSegments.Clear();
                _pendingMediaHeaders.Clear();
            }

            if (_httpClient != null)
            {
                try { _httpClient.Dispose(); } catch { }
                _httpClient = null;
            }

            if (_mss != null)
            {
                try
                {
                    _mss.SampleRequested -= Mss_SampleRequested;
                    _mss.Starting -= Mss_Starting;
                    _mss.Closed -= Mss_Closed;
                }
                catch { }
                _mss = null;
            }
        }
    }
}
