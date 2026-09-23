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
        private int _lastMediaHeaderSeqNum = -1;
        private bool _lastMediaHeaderIsInit = false;

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
            Action<string> logFunc = null)
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

            // Shared HTTP filter that ignores legacy SSL handshake anomalies
            var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter();
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Untrusted);
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Expired);
            _httpClient = new HttpClient(filter);

            // Audio encoding descriptor: AAC-ADTS 44.1kHz Stereo 128kbps (itag 140 baseline)
            var encodingProps = AudioEncodingProperties.CreateAacAdts(44100, 2, 128000);
            var streamDescriptor = new AudioStreamDescriptor(encodingProps);

            _mss = new MediaStreamSource(streamDescriptor);
            _mss.CanSeek = true;
            _mss.BufferTime = TimeSpan.FromSeconds(3);

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
            if (request.StartPosition.HasValue && request.StartPosition.Value > TimeSpan.FromMilliseconds(500))
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
                if (_isDisposed) return;
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
                    Log("Preload done: parsed " + samples0 + " samples (~" + BufferedSeconds.ToString("F1") + "s buffered)");
                    return true;
                }
                else if (samples0 == 0 && string.IsNullOrEmpty(LastError))
                {
                    Log("rn=0 delivered init segment. Preloading rn=1 for audio samples...");
                    int samples1 = await FetchChunkAsync(1, ct).ConfigureAwait(false);
                    if (samples1 > 0)
                    {
                        _requestNumber = 2;
                        Log("Preload done at rn=1: parsed " + samples1 + " samples (~" + BufferedSeconds.ToString("F1") + "s buffered)");
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
            _lastMediaHeaderSeqNum = -1;
            _lastMediaHeaderIsInit = false;

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

            // Build Protobuf VideoPlaybackAbrRequest
            byte[] requestBody = MiniProtoWriter.BuildAudioAbrRequest(
                _ustreamerConfig,
                fetchedPositionMs,
                _playbackRate,
                140, // 140 = AAC 128kbps itag
                _clientNameInt,
                !string.IsNullOrEmpty(_clientVersion) ? _clientVersion : "19.29.35",
                _poTokenBytes,
                _playbackCookieBytes,
                _formatsInitialized,
                fetchedPositionMs);

            using (var req = new HttpRequestMessage(HttpMethod.Post, new Uri(requestUrl)))
            {
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
                    // Flow control: keep ~10s buffer in RAM (~400 samples)
                    // Caps RAM footprint (critical for 512MB WP8.1) and prevents overrunning live broadcast head
                    int count = 0;
                    lock (_queueLock) { count = _sampleQueue.Count; }
                    if (count >= 400)
                    {
                        await Task.Delay(1500, ct).ConfigureAwait(false);
                        continue;
                    }

                    int newSamples = await FetchChunkAsync(_requestNumber, ct).ConfigureAwait(false);
                    if (newSamples < 0)
                    {
                        // HTTP/network error: wait 2s and retry the same rn
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
                        if (_consecutiveEmptyChunks >= 4)
                        {
                            LastError = "No audio received after " + _consecutiveEmptyChunks + " attempts";
                            Log(LastError + ", stopping SABR streaming loop.");
                            break;
                        }

                        // Server returned 0 audio samples (e.g. reached live head or waiting for next chunk).
                        // DO NOT advance _requestNumber! Wait 3s for live encoder to generate the next chunk.
                        Log("No audio in rn=" + _requestNumber + ", waiting 3s for next live segment...");
                        await Task.Delay(3000, ct).ConfigureAwait(false);
                        continue;
                    }

                    _consecutiveEmptyChunks = 0;
                    _formatsInitialized = true;

                    // Audio samples received successfully, advance request sequence
                    _requestNumber++;
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
                        lock (_queueLock)
                        {
                            MemoryStream ms;
                            if (_pendingSegments.TryGetValue(headerId, out ms))
                            {
                                _pendingSegments.Remove(headerId);
                                segBytes = ms.ToArray();
                                ms.Dispose();
                            }
                        }

                        if (segBytes != null && segBytes.Length > 0)
                        {
                            // Skip init segments (moov/ftyp only, no playable audio)
                            if (_lastMediaHeaderIsInit)
                            {
                                Log("Skipping init segment " + headerId + " (" + segBytes.Length + " bytes)");
                                break;
                            }

                            // Deduplicate: skip if we already parsed this sequence number
                            if (_lastMediaHeaderSeqNum >= 0 && _downloadedSequences.Contains(_lastMediaHeaderSeqNum))
                            {
                                Log("Skipping duplicate seq=" + _lastMediaHeaderSeqNum + " (" + segBytes.Length + " bytes)");
                                break;
                            }

                            int samples = ParseAndEnqueueFmp4(segBytes, 0, segBytes.Length);
                            if (samples > 0)
                            {
                                if (_lastMediaHeaderSeqNum >= 0)
                                    _downloadedSequences.Add(_lastMediaHeaderSeqNum);
                                Log("Segment " + headerId + " seq=" + _lastMediaHeaderSeqNum + " finalized: " + samples + " samples parsed (" + segBytes.Length + " bytes)");
                            }
                            else
                            {
                                Log("Segment " + headerId + " received (" + segBytes.Length + " bytes, no audio samples)");
                            }
                        }
                    }
                    break;

                case UmpPartId.MEDIA_HEADER:
                    // Parse MediaHeader protobuf: field 8 = isInitSeg (bool), field 9 = sequenceNumber (varint)
                    _lastMediaHeaderIsInit = false;
                    _lastMediaHeaderSeqNum = -1;
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
                                if (fld == 8) _lastMediaHeaderIsInit = (val != 0);
                                else if (fld == 9) _lastMediaHeaderSeqNum = (int)val;
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
                    Log("Received MEDIA_HEADER (seq=" + _lastMediaHeaderSeqNum + ", init=" + _lastMediaHeaderIsInit + ", " + part.Size + " bytes)");
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
                    byte[] cookie = UmpParser.ExtractPlaybackCookie(part.Data);
                    if (cookie != null && cookie.Length > 0)
                    {
                        _playbackCookieBytes = cookie;
                        Log("Extracted playback cookie (" + cookie.Length + " bytes)");
                    }
                    else
                    {
                        Log("Received NEXT_REQUEST_POLICY (" + part.Size + " bytes)");
                    }
                    break;

                case UmpPartId.STREAM_PROTECTION_STATUS:
                    int spsCode = (part.Data != null && part.Data.Length > 1) ? (int)part.Data[1] : 0;
                    _lastSpsCode = spsCode;
                    Log("Received STREAM_PROTECTION_STATUS (code " + spsCode + ", " + part.Size + " bytes)");
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
                    if (segBytes.Length > 0)
                    {
                        int samples = ParseAndEnqueueFmp4(segBytes, 0, segBytes.Length);
                        if (samples > 0)
                        {
                            Log("Flushed segment " + hid + ": " + samples + " samples (" + segBytes.Length + " bytes)");
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
                    try { p.Deferral.Complete(); } catch { }
                }
                _sampleQueue.Clear();

                foreach (var kvp in _pendingSegments)
                {
                    try { kvp.Value.Dispose(); } catch { }
                }
                _pendingSegments.Clear();
            }

            if (_httpClient != null)
            {
                try { _httpClient.Dispose(); } catch { }
                _httpClient = null;
            }

            if (_mss != null)
            {
                _mss.SampleRequested -= Mss_SampleRequested;
                _mss.Starting -= Mss_Starting;
                _mss.Closed -= Mss_Closed;
                _mss = null;
            }
        }
    }
}
