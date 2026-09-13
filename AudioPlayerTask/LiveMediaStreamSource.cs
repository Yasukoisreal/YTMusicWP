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

namespace AudioPlayerTask
{
    /// <summary>
    /// Manages continuous, gapless YouTube Live audio playback using WinRT MediaStreamSource (MSS).
    /// Demuxes fragmented MP4 (fMP4) chunks in-memory, wraps AAC frames with ADTS headers,
    /// and streams them directly into BackgroundMediaPlayer without any pipeline teardowns or file swapping.
    /// </summary>
    internal sealed class LiveMediaStreamSource : IDisposable
    {
        public delegate Task<byte[]> DownloadSegmentDelegate(string baseUrl, long seq, CancellationToken ct);
        public delegate Task<string> RefreshBaseUrlDelegate(string vidId, CancellationToken ct);
        public delegate Task<long> QueryHeadSeqDelegate(string baseUrl, CancellationToken ct);
        public delegate long GetCachedHeadSeqDelegate();

        private static readonly byte[] BOX_TRUN = new byte[] { (byte)'t', (byte)'r', (byte)'u', (byte)'n' };
        private static readonly byte[] BOX_MDAT = new byte[] { (byte)'m', (byte)'d', (byte)'a', (byte)'t' };

        private MediaStreamSource _mss;
        private readonly object _queueLock = new object();
        private readonly Queue<MediaStreamSample> _sampleQueue = new Queue<MediaStreamSample>();
        private MediaStreamSourceSampleRequestDeferral _pendingDeferral;
        private MediaStreamSourceSampleRequest _pendingRequest;

        private long _sampleIndex = 0;
        private long _nextSequence = 0;
        private long _lastKnownHeadSeq = 0;
        private string _videoId;
        private string _currentBaseUrl;
        private Stopwatch _baseUrlStopwatch = new Stopwatch();

        private CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _downloadLoopTask;
        private bool _isDisposed = false;

        private readonly DownloadSegmentDelegate _downloadFunc;
        private readonly RefreshBaseUrlDelegate _refreshBaseUrlFunc;
        private readonly GetCachedHeadSeqDelegate _getCachedHeadSeqFunc;
        private readonly QueryHeadSeqDelegate _queryHeadSeqFunc;
        private readonly Action<string> _logFunc;

        public MediaStreamSource StreamSource { get { return _mss; } }
        public bool IsDisposed { get { return _isDisposed; } }

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

        public LiveMediaStreamSource(
            string videoId,
            string initialBaseUrl,
            long startSeq,
            DownloadSegmentDelegate downloadFunc,
            RefreshBaseUrlDelegate refreshBaseUrlFunc,
            GetCachedHeadSeqDelegate getCachedHeadSeqFunc,
            QueryHeadSeqDelegate queryHeadSeqFunc,
            Action<string> logFunc = null)
        {
            _videoId = videoId;
            _currentBaseUrl = initialBaseUrl;
            _nextSequence = startSeq;
            _downloadFunc = downloadFunc;
            _refreshBaseUrlFunc = refreshBaseUrlFunc;
            _getCachedHeadSeqFunc = getCachedHeadSeqFunc;
            _queryHeadSeqFunc = queryHeadSeqFunc;
            _logFunc = logFunc;
            if (_getCachedHeadSeqFunc != null)
            {
                try { _lastKnownHeadSeq = _getCachedHeadSeqFunc(); } catch { }
            }
            _baseUrlStopwatch.Restart();

            // Native WinRT AAC-ADTS stream descriptor at standard 44.1kHz Stereo 128kbps
            var encodingProps = AudioEncodingProperties.CreateAacAdts(44100, 2, 128000);
            var streamDescriptor = new AudioStreamDescriptor(encodingProps);

            _mss = new MediaStreamSource(streamDescriptor);
            _mss.CanSeek = false;
            _mss.BufferTime = TimeSpan.FromSeconds(3);

            _mss.Starting += Mss_Starting;
            _mss.SampleRequested += Mss_SampleRequested;
            _mss.Closed += Mss_Closed;
        }

        private void Log(string msg)
        {
            if (_logFunc != null)
            {
                try { _logFunc("[LiveMSS] " + msg); } catch { }
            }
        }

        private void Mss_Starting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            Log("Mss_Starting -> SetActualStartPosition(Zero)");
            lock (_queueLock)
            {
                if (_pendingDeferral != null)
                {
                    try { _pendingDeferral.Complete(); } catch { }
                    _pendingDeferral = null;
                    _pendingRequest = null;
                }
            }
            args.Request.SetActualStartPosition(TimeSpan.Zero);
        }

        private void Mss_SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            if (_isDisposed) return;
            var request = args.Request;

            lock (_queueLock)
            {
                if (_sampleQueue.Count > 0)
                {
                    request.Sample = _sampleQueue.Dequeue();
                    return;
                }

                // Buffer is temporarily empty: safely complete previous deferral if any exists
                if (_pendingDeferral != null)
                {
                    try { _pendingDeferral.Complete(); } catch { }
                    _pendingDeferral = null;
                    _pendingRequest = null;
                }
                _pendingDeferral = request.GetDeferral();
                _pendingRequest = request;
            }
        }

        private void Mss_Closed(MediaStreamSource sender, MediaStreamSourceClosedEventArgs args)
        {
            Log("Mss_Closed -> reason: " + args.Request.Reason);
            Dispose();
        }

        /// <summary>
        /// Preloads the initial 2 chunks (~10s of audio) to ensure instantaneous start without stalling.
        /// </summary>
        public async Task<bool> PreloadInitialChunksAsync(CancellationToken ct)
        {
            try
            {
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct))
                {
                    var token = linkedCts.Token;

                    // Download initial 2 chunks in parallel
                    long seq0 = _nextSequence;
                    long seq1 = _nextSequence + 1;

                    Log("Preloading initial chunks seq=" + seq0 + ", " + seq1);
                    var t0 = _downloadFunc(_currentBaseUrl, seq0, token);
                    var t1 = _downloadFunc(_currentBaseUrl, seq1, token);

                    var bytesArr = await Task.WhenAll(t0, t1);
                    if (token.IsCancellationRequested) return false;

                    int parsed0 = ParseAndEnqueueChunk(bytesArr[0]);
                    int parsed1 = ParseAndEnqueueChunk(bytesArr[1]);

                    if (parsed0 > 0) _nextSequence++;
                    if (parsed1 > 0) _nextSequence++;

                    if (parsed0 > 0 || parsed1 > 0)
                    {
                        Log("Preload done: parsed " + (parsed0 + parsed1) + " samples (~" + BufferedSeconds.ToString("F1") + "s buffered)");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Preload failed: " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// Starts background download loop that keeps ~15-22s buffer ahead of playback.
        /// </summary>
        public void StartStreaming()
        {
            if (_downloadLoopTask != null) return;
            _downloadLoopTask = Task.Run((Func<Task>)DownloadLoopAsync);
        }

        private async Task DownloadLoopAsync()
        {
            Log("Background download loop started at seq=" + _nextSequence);
            var token = _cts.Token;

            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    // 1. Flow control: Maintain 600 - 950 samples (~14s - 22s) in RAM buffer.
                    // When buffer is full (>= 900 samples), wait 1.5s to let playback drain samples naturally.
                    int count = 0;
                    lock (_queueLock) { count = _sampleQueue.Count; }

                    if (count >= 900)
                    {
                        await Task.Delay(1500, token);
                        continue;
                    }

                    // 2. BaseURL maintenance:
                    // Unauthenticated YouTube live BaseURLs expire in ~30s on Google Video CDN.
                    // Proactively refresh when BaseURL is >= 20s old and buffer is healthy (>= 400 samples / ~9s),
                    // or immediately if _currentBaseUrl is missing/invalidated.
                    bool needRefresh = string.IsNullOrEmpty(_currentBaseUrl) ||
                                       (_baseUrlStopwatch.Elapsed.TotalSeconds >= 20.0 && count >= 400);

                    if (needRefresh && _refreshBaseUrlFunc != null)
                    {
                        try
                        {
                            string freshUrl = await _refreshBaseUrlFunc(_videoId, token);
                            if (!string.IsNullOrEmpty(freshUrl))
                            {
                                _currentBaseUrl = freshUrl;
                                _baseUrlStopwatch.Restart();
                                Log("BaseURL refreshed successfully (buffered=" + BufferedSeconds.ToString("F1") + "s)");
                            }
                        }
                        catch { }
                    }

                    if (string.IsNullOrEmpty(_currentBaseUrl))
                    {
                        await Task.Delay(1500, token);
                        continue;
                    }

                    // 3. Live Edge Pacing Guard:
                    // Segments cannot be downloaded before YouTube encodes/publishes them.
                    long cachedHead = _getCachedHeadSeqFunc != null ? _getCachedHeadSeqFunc() : -1;
                    if (cachedHead > _lastKnownHeadSeq) _lastKnownHeadSeq = cachedHead;

                    // If _nextSequence is at or past known head, query YouTube for latest published head
                    if (_nextSequence > _lastKnownHeadSeq)
                    {
                        if (_queryHeadSeqFunc != null)
                        {
                            long freshHead = await _queryHeadSeqFunc(_currentBaseUrl, token);
                            if (freshHead > 0)
                            {
                                _lastKnownHeadSeq = freshHead;
                            }
                        }
                    }

                    // If still past head, wait 2.5s for YouTube to publish the chunk.
                    // Playback continues smoothly from the 15-20s buffer in RAM.
                    if (_lastKnownHeadSeq > 0 && _nextSequence > _lastKnownHeadSeq)
                    {
                        Log("At live edge (next=" + _nextSequence + ", head=" + _lastKnownHeadSeq + ", buffered=" + BufferedSeconds.ToString("F1") + "s). Waiting 2.5s...");
                        await Task.Delay(2500, token);
                        continue;
                    }

                    // 4. Download segment
                    long targetSeq = _nextSequence;
                    byte[] chunkBytes = await _downloadFunc(_currentBaseUrl, targetSeq, token);

                    if (chunkBytes != null && chunkBytes.Length > 0)
                    {
                        int parsed = ParseAndEnqueueChunk(chunkBytes);
                        if (parsed > 0)
                        {
                            _nextSequence++;
                            lock (_queueLock) { count = _sampleQueue.Count; }
                            if (count >= 450)
                            {
                                await Task.Delay(200, token);
                            }
                        }
                        else
                        {
                            Log("Parse returned 0 samples for seq=" + targetSeq);
                            await Task.Delay(1000, token);
                        }
                    }
                    else
                    {
                        // Download returned null (BaseURL expired with 403 Forbidden, or transient error)
                        Log("Download returned null for seq=" + targetSeq + ", refreshing BaseURL (buffered=" + BufferedSeconds.ToString("F1") + "s)...");
                        _currentBaseUrl = null;
                        if (_refreshBaseUrlFunc != null)
                        {
                            try
                            {
                                string freshUrl = await _refreshBaseUrlFunc(_videoId, token);
                                if (!string.IsNullOrEmpty(freshUrl))
                                {
                                    _currentBaseUrl = freshUrl;
                                    _baseUrlStopwatch.Restart();
                                    Log("BaseURL refreshed successfully after null download");
                                }
                            }
                            catch { }
                        }

                        if (string.IsNullOrEmpty(_currentBaseUrl))
                        {
                            await Task.Delay(1500, token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log("DownloadLoop exception: " + ex.Message);
                    try { await Task.Delay(1500, token); } catch { }
                }
            }
            Log("Background download loop ended");
        }

        /// <summary>
        /// Lightweight fMP4 parser: extracts AAC frames from trun box and mdat payload,
        /// prepends standard 7-byte ADTS header, and enqueues MediaStreamSample instances.
        /// </summary>
        private int ParseAndEnqueueChunk(byte[] chunk)
        {
            if (chunk == null || chunk.Length < 64) return 0;

            int trunPos = FindBox(chunk, BOX_TRUN, 0, chunk.Length);
            int mdatPos = FindBox(chunk, BOX_MDAT, 0, chunk.Length);
            if (trunPos < 4 || mdatPos < 4) return 0;

            int trunHeader = trunPos - 4;
            if (trunHeader + 16 > chunk.Length) return 0;

            int trunLen = ReadInt32BE(chunk, trunHeader);
            int flags = (chunk[trunHeader + 9] << 16) | (chunk[trunHeader + 10] << 8) | chunk[trunHeader + 11];
            int sampleCount = ReadInt32BE(chunk, trunHeader + 12);
            if (sampleCount <= 0 || sampleCount > 600) return 0;

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
                if (cur >= chunk.Length) break;
                if (hasDuration) cur += 4;
                int sz = hasSize ? ReadInt32BE(chunk, cur) : 0;
                cur += 4;
                if (hasFlags) cur += 4;
                if (hasCompTime) cur += 4;
                sampleSizes[i] = sz;
            }

            // In fMP4, mdat payload starts 4 bytes after 'mdat' fourcc
            int rawOffset = mdatPos + 4;
            if (rawOffset >= chunk.Length) return 0;

            var sampleDuration = TimeSpan.FromTicks(232199); // 1024 samples at 44100Hz = 23.21995ms
            int parsedCount = 0;

            lock (_queueLock)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    int rawLen = sampleSizes[i];
                    if (rawLen <= 0 || rawOffset + rawLen > chunk.Length) break;

                    // Build standard 7-byte ADTS header for AAC-LC Stereo 44.1kHz
                    int flen = 7 + rawLen;
                    byte[] adtsFrame = new byte[flen];
                    adtsFrame[0] = 0xFF;
                    adtsFrame[1] = 0xF1; // MPEG-4, layer 0, no CRC
                    adtsFrame[2] = 0x50; // AAC-LC (1), 44100Hz (4), private 0, chan high 0
                    adtsFrame[3] = (byte)(0x80 | ((flen >> 11) & 0x03)); // stereo (2), frame len high 2 bits
                    adtsFrame[4] = (byte)((flen >> 3) & 0xFF);
                    adtsFrame[5] = (byte)(((flen & 0x07) << 5) | 0x1F);
                    adtsFrame[6] = 0xFC;
                    System.Buffer.BlockCopy(chunk, rawOffset, adtsFrame, 7, rawLen);
                    rawOffset += rawLen;

                    // Monotonic, continuous timestamping based on sample index
                    long ticks = (long)(_sampleIndex * (1024.0 / 44100.0 * 10000000.0));
                    var timestamp = TimeSpan.FromTicks(ticks);
                    _sampleIndex++;

                    var sample = MediaStreamSample.CreateFromBuffer(adtsFrame.AsBuffer(), timestamp);
                    sample.Duration = sampleDuration;
                    sample.KeyFrame = true;

                    _sampleQueue.Enqueue(sample);
                    parsedCount++;
                }

                // Fulfill waiting request immediately if deferral was captured
                if (_pendingDeferral != null && _pendingRequest != null && _sampleQueue.Count > 0)
                {
                    _pendingRequest.Sample = _sampleQueue.Dequeue();
                    var def = _pendingDeferral;
                    _pendingDeferral = null;
                    _pendingRequest = null;
                    try { def.Complete(); } catch { }
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

            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }
            }
            catch { }

            lock (_queueLock)
            {
                if (_pendingDeferral != null)
                {
                    try { _pendingDeferral.Complete(); } catch { }
                    _pendingDeferral = null;
                    _pendingRequest = null;
                }
                _sampleQueue.Clear();
            }

            if (_mss != null)
            {
                try
                {
                    _mss.Starting -= Mss_Starting;
                    _mss.SampleRequested -= Mss_SampleRequested;
                    _mss.Closed -= Mss_Closed;
                }
                catch { }
                _mss = null;
            }

            Log("Disposed");
        }
    }
}
