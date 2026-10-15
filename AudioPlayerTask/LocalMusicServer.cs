using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AudioPlayerTask
{
    internal sealed class CustomActionDto
    {
        public string Action { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public bool IsActive { get; set; }

        public CustomActionDto()
        {
            Action = "";
            Name = "";
            Icon = "";
            IsActive = false;
        }
    }

    internal sealed class QueueItemDto
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Description { get; set; }
        public string MediaId { get; set; }
        public string IconUri { get; set; }
        public string MediaUri { get; set; }
        public long BtFolderType { get; set; }

        public QueueItemDto()
        {
            Id = 0;
            Title = "";
            Subtitle = "";
            Description = "";
            MediaId = "";
            IconUri = "";
            MediaUri = "";
            BtFolderType = 0;
        }
    }

    internal sealed class TrackStatusDto
    {
        public bool HasTrack { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Author { get; set; }
        public string Writer { get; set; }
        public string Composer { get; set; }
        public string AlbumArtist { get; set; }
        public string MediaUri { get; set; }
        public string Thumbnail { get; set; }
        public string VideoId { get; set; }
        public long DurationMs { get; set; }
        public long PositionMs { get; set; }
        public long BufferedPositionMs { get; set; }
        public bool IsPlaying { get; set; }
        public int State { get; set; } // 0: None, 1: Stopped, 2: Paused, 3: Playing, 6: Buffering, 7: Error
        public float Speed { get; set; }
        public long Actions { get; set; }
        public string AppName { get; set; }
        public string AppIcon { get; set; }
        public string AudioOutput { get; set; }
        public int RepeatMode { get; set; }
        public int ShuffleMode { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsShuffle { get; set; }
        public int RatingStyle { get; set; }
        public float RatingValue { get; set; }
        public string Subtitle { get; set; }
        public string Description { get; set; }
        public string Genre { get; set; }
        public string Compilation { get; set; }
        public string Date { get; set; }
        public int TrackNumber { get; set; }
        public int NumTracks { get; set; }
        public int Year { get; set; }
        public int DiscNumber { get; set; }
        public string QueueTitle { get; set; }
        public long ActiveQueueItemId { get; set; }
        public int PlaybackType { get; set; }
        public int VolumeControl { get; set; }
        public string VolumeControlId { get; set; }
        public int Volume { get; set; }
        public int MaxVolume { get; set; }
        public int SessionFlags { get; set; }
        public string ErrorMessage { get; set; }
        public string PackageName { get; set; }
        public string LaunchUri { get; set; }
        public System.Collections.Generic.List<QueueItemDto> Queue { get; set; }
        public System.Collections.Generic.List<CustomActionDto> CustomActions { get; set; }

        public TrackStatusDto()
        {
            HasTrack = false;
            Title = "";
            Artist = "";
            Album = "";
            Author = "";
            Writer = "";
            Composer = "";
            AlbumArtist = "";
            MediaUri = "";
            Thumbnail = "";
            VideoId = "";
            DurationMs = 0;
            PositionMs = 0;
            BufferedPositionMs = 0;
            IsPlaying = false;
            State = 0;
            Speed = 1.0f;
            Actions = 4215807L; // AOSP Actions bitmask (includes SKIP_TO_PREVIOUS 16L and SKIP_TO_NEXT 32L)
            AppName = "YTMusicWP";
            AppIcon = "http://olmp.local/api/icon";
            AudioOutput = "Điện thoại này";
            RepeatMode = 0;
            ShuffleMode = 0;
            IsFavorite = false;
            IsShuffle = false;
            RatingStyle = 1;
            RatingValue = 0.0f;
            Subtitle = "";
            Description = "";
            Genre = "";
            Compilation = "";
            Date = "";
            TrackNumber = 0;
            NumTracks = 0;
            Year = 0;
            DiscNumber = 1;
            QueueTitle = "";
            ActiveQueueItemId = -1;
            PlaybackType = 1;
            VolumeControl = 2;
            VolumeControlId = "device_speaker_0";
            Volume = 100;
            MaxVolume = 100;
            SessionFlags = 3;
            ErrorMessage = "";
            PackageName = "com.google.android.apps.youtube.music";
            LaunchUri = "";
            Queue = new System.Collections.Generic.List<QueueItemDto>();
            CustomActions = new System.Collections.Generic.List<CustomActionDto>();
        }
    }

    internal sealed class LocalMusicServer : IDisposable
    {
        private StreamSocketListener _listener;
        private readonly int _port;
        private readonly Func<TrackStatusDto> _getStatusFunc;
        private readonly Action _playAction;
        private readonly Action _pauseAction;
        private readonly Action _nextAction;
        private readonly Action _prevAction;
        private readonly Action<long> _seekAction;
        private readonly Action _stopAction;
        private readonly Action _fastForwardAction;
        private readonly Action _rewindAction;
        private readonly Action<float> _speedAction;
        private readonly Action<int> _repeatAction;
        private readonly Action<int> _shuffleAction;
        private readonly Action<int, float> _ratingAction;
        private readonly Action<long> _queueSkipAction;
        private readonly Action<int> _volumeAction;
        private readonly Action<int> _adjustVolumeAction;
        private readonly Action _prepareAction;
        private readonly Action<string, string> _customAction;
        private bool _isDisposed = false;

        internal LocalMusicServer(
            int port,
            Func<TrackStatusDto> getStatusFunc,
            Action playAction,
            Action pauseAction,
            Action nextAction,
            Action prevAction,
            Action<long> seekAction,
            Action stopAction = null,
            Action fastForwardAction = null,
            Action rewindAction = null,
            Action<float> speedAction = null,
            Action<int> repeatAction = null,
            Action<int> shuffleAction = null,
            Action<int, float> ratingAction = null,
            Action<long> queueSkipAction = null,
            Action<int> volumeAction = null,
            Action<int> adjustVolumeAction = null,
            Action prepareAction = null,
            Action<string, string> customAction = null)
        {
            _port = port;
            _getStatusFunc = getStatusFunc;
            _playAction = playAction;
            _pauseAction = pauseAction;
            _nextAction = nextAction;
            _prevAction = prevAction;
            _seekAction = seekAction;
            _stopAction = stopAction;
            _fastForwardAction = fastForwardAction;
            _rewindAction = rewindAction;
            _speedAction = speedAction;
            _repeatAction = repeatAction;
            _shuffleAction = shuffleAction;
            _ratingAction = ratingAction;
            _queueSkipAction = queueSkipAction;
            _volumeAction = volumeAction;
            _adjustVolumeAction = adjustVolumeAction;
            _prepareAction = prepareAction;
            _customAction = customAction;
        }

        internal async Task StartAsync()
        {
            if (_isDisposed) return;
            try
            {
                _listener = new StreamSocketListener();
                _listener.ConnectionReceived += OnConnectionReceived;
                await _listener.BindServiceNameAsync(_port.ToString());
                System.Diagnostics.Debug.WriteLine("[LocalMusicServer] Started on port " + _port);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[LocalMusicServer] Start failed: " + ex.Message);
            }
        }

        private async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            if (_isDisposed) return;
            var socket = args.Socket;

            DataReader reader = null;
            DataWriter writer = null;

            try
            {
                reader = new DataReader(socket.InputStream);
                writer = new DataWriter(socket.OutputStream);
                reader.InputStreamOptions = InputStreamOptions.Partial;
                await reader.LoadAsync(2048);
                if (reader.UnconsumedBufferLength == 0) return;

                string request = reader.ReadString(reader.UnconsumedBufferLength);
                string firstLine = "";
                int lineEnd = request.IndexOfAny(new char[] { '\r', '\n' });
                if (lineEnd > 0)
                {
                    firstLine = request.Substring(0, lineEnd).Trim();
                }
                else
                {
                    firstLine = request.Trim();
                }

                // Format: METHOD URL HTTP/1.1
                string[] parts = firstLine.Split(' ');
                string method = parts.Length > 0 ? parts[0].ToUpperInvariant() : "GET";
                string rawUrl = parts.Length > 1 ? parts[1] : "/";

                // Hỗ trợ OPTIONS pre-flight cho trình duyệt / web app
                if (method == "OPTIONS")
                {
                    string corsHeaders = "HTTP/1.1 200 OK\r\n" +
                                         "Access-Control-Allow-Origin: *\r\n" +
                                         "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                         "Access-Control-Allow-Headers: Content-Type, Authorization\r\n" +
                                         "Content-Length: 0\r\n" +
                                         "Connection: close\r\n\r\n";
                    writer.WriteString(corsHeaders);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    return;
                }

                string path = rawUrl;
                string query = "";
                int qIdx = rawUrl.IndexOf('?');
                if (qIdx >= 0)
                {
                    path = rawUrl.Substring(0, qIdx);
                    query = rawUrl.Substring(qIdx + 1);
                }

                // Xử lý nếu client truyền full URL: http://olmp.local/api/... -> bóc tách lấy /api/...
                if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    int slashSlash = path.IndexOf("://", StringComparison.Ordinal);
                    if (slashSlash >= 0)
                    {
                        int nextSlash = path.IndexOf('/', slashSlash + 3);
                        if (nextSlash >= 0)
                        {
                            path = path.Substring(nextSlash);
                        }
                        else
                        {
                            path = "/";
                        }
                    }
                }

                path = path.ToLowerInvariant();

                string responseJson = "{\"status\":\"ok\"}";

                if (path == "/api/track" || path == "/api/status")
                {
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/play"))
                {
                    if (_playAction != null) _playAction();
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    status.IsPlaying = true;
                    status.State = 3;
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/pause"))
                {
                    if (_pauseAction != null) _pauseAction();
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    status.IsPlaying = false;
                    status.State = 2;
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/next"))
                {
                    if (_nextAction != null) _nextAction();
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    if (status.HasTrack)
                    {
                        status.IsPlaying = true;
                        status.State = 6; // AndroidPlaybackState.STATE_BUFFERING
                    }
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/prev"))
                {
                    if (_prevAction != null) _prevAction();
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    if (status.HasTrack)
                    {
                        status.IsPlaying = true;
                        status.State = 6; // AndroidPlaybackState.STATE_BUFFERING
                    }
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/seek"))
                {
                    long seekMs = 0;
                    string posStr = ExtractQueryParam(query, "pos");
                    if (string.IsNullOrEmpty(posStr)) posStr = ExtractQueryParam(query, "ms");
                    if (!string.IsNullOrEmpty(posStr) && long.TryParse(posStr, out seekMs))
                    {
                        if (_seekAction != null) _seekAction(seekMs);
                    }
                    responseJson = "{\"status\":\"seeked\",\"pos\":" + seekMs + "}";
                }
                else if (path.StartsWith("/api/stop"))
                {
                    if (_stopAction != null) _stopAction();
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    status.IsPlaying = false;
                    status.State = 1; // STATE_STOPPED
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/fastforward"))
                {
                    if (_fastForwardAction != null) _fastForwardAction();
                    responseJson = "{\"status\":\"ok\"}";
                }
                else if (path.StartsWith("/api/rewind"))
                {
                    if (_rewindAction != null) _rewindAction();
                    responseJson = "{\"status\":\"ok\"}";
                }
                else if (path.StartsWith("/api/speed"))
                {
                    string speedStr = ExtractQueryParam(query, "value");
                    if (string.IsNullOrEmpty(speedStr)) speedStr = ExtractQueryParam(query, "speed");
                    float spd = 1.0f;
                    if (!string.IsNullOrEmpty(speedStr) && float.TryParse(speedStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out spd))
                    {
                        if (_speedAction != null) _speedAction(spd);
                    }
                    responseJson = "{\"status\":\"ok\",\"speed\":" + spd.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "}";
                }
                else if (path.StartsWith("/api/repeat"))
                {
                    int repMode = 0;
                    string modeStr = ExtractQueryParam(query, "mode");
                    if (!string.IsNullOrEmpty(modeStr) && int.TryParse(modeStr, out repMode))
                    {
                        // Parsed
                    }
                    else
                    {
                        string val = (ExtractQueryParam(query, "value") ?? "").ToLowerInvariant();
                        if (val == "one" || val == "track" || val == "single") repMode = 1;
                        else if (val == "all" || val == "true" || val == "playlist") repMode = 2;
                        else repMode = 0;
                    }
                    if (_repeatAction != null) _repeatAction(repMode);
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    status.RepeatMode = repMode;
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/shuffle"))
                {
                    int shuffMode = 0;
                    string modeStr = ExtractQueryParam(query, "mode");
                    if (!string.IsNullOrEmpty(modeStr) && int.TryParse(modeStr, out shuffMode))
                    {
                        // Parsed
                    }
                    else
                    {
                        string val = (ExtractQueryParam(query, "value") ?? "").ToLowerInvariant();
                        shuffMode = (val == "true" || val == "1") ? 1 : 0;
                    }
                    if (_shuffleAction != null) _shuffleAction(shuffMode);
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    status.ShuffleMode = shuffMode;
                    status.IsShuffle = (shuffMode != 0);
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/rating"))
                {
                    int style = 1;
                    float val = 0.0f;
                    string styleStr = ExtractQueryParam(query, "style");
                    string valStr = ExtractQueryParam(query, "value");
                    if (!string.IsNullOrEmpty(styleStr)) int.TryParse(styleStr, out style);
                    if (!string.IsNullOrEmpty(valStr)) float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val);
                    if (_ratingAction != null) _ratingAction(style, val);
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    status.RatingStyle = style;
                    status.RatingValue = val;
                    if (style == 1) status.IsFavorite = (val > 0.0f);
                    responseJson = SerializeStatus(status);
                }
                else if (path.StartsWith("/api/queue/skip"))
                {
                    long skipId = 0;
                    string idStr = ExtractQueryParam(query, "id");
                    if (!string.IsNullOrEmpty(idStr) && long.TryParse(idStr, out skipId))
                    {
                        if (_queueSkipAction != null) _queueSkipAction(skipId);
                    }
                    responseJson = "{\"status\":\"skipped\",\"id\":" + skipId + "}";
                }
                else if (path.StartsWith("/api/volume/adjust"))
                {
                    int dir = 0;
                    string dirStr = ExtractQueryParam(query, "direction");
                    if (!string.IsNullOrEmpty(dirStr)) int.TryParse(dirStr, out dir);
                    if (_adjustVolumeAction != null) _adjustVolumeAction(dir);
                    responseJson = "{\"status\":\"ok\",\"direction\":" + dir + "}";
                }
                else if (path.StartsWith("/api/volume"))
                {
                    int vol = 100;
                    string volStr = ExtractQueryParam(query, "value");
                    if (!string.IsNullOrEmpty(volStr)) int.TryParse(volStr, out vol);
                    if (_volumeAction != null) _volumeAction(vol);
                    responseJson = "{\"status\":\"ok\",\"volume\":" + vol + "}";
                }
                else if (path.StartsWith("/api/prepare"))
                {
                    if (_prepareAction != null) _prepareAction();
                    responseJson = "{\"status\":\"ok\"}";
                }
                else if (path.StartsWith("/api/icon"))
                {
                    byte[] iconBytes = await GetAppIconBytesAsync();
                    if (iconBytes != null && iconBytes.Length > 0)
                    {
                        string imgHeaders = "HTTP/1.1 200 OK\r\n" +
                                            "Content-Type: image/png\r\n" +
                                            "Access-Control-Allow-Origin: *\r\n" +
                                            "Cache-Control: public, max-age=86400\r\n" +
                                            "Connection: close\r\n" +
                                            "Content-Length: " + iconBytes.Length + "\r\n\r\n";
                        writer.WriteString(imgHeaders);
                        writer.WriteBytes(iconBytes);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                    }
                    else
                    {
                        string notFound = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                        writer.WriteString(notFound);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                    }
                    return;
                }
                else if (path.StartsWith("/api/action") || path.StartsWith("/api/favorite"))
                {
                    string actionName = ExtractQueryParam(query, "name");
                    if (string.IsNullOrEmpty(actionName))
                    {
                        if (path.StartsWith("/api/favorite")) actionName = "android.media.action.FAVORITE";
                        else actionName = "custom";
                    }
                    string actionVal = ExtractQueryParam(query, "value");
                    if (string.IsNullOrEmpty(actionVal)) actionVal = ExtractQueryParam(query, "val");
                    if (string.IsNullOrEmpty(actionVal)) actionVal = "";

                    if (_customAction != null) _customAction(actionName, actionVal);
                    var status = _getStatusFunc != null ? _getStatusFunc() : new TrackStatusDto();
                    responseJson = SerializeStatus(status);
                }

                byte[] bodyBytes = Encoding.UTF8.GetBytes(responseJson);
                string headers = "HTTP/1.1 200 OK\r\n" +
                                 "Content-Type: application/json; charset=utf-8\r\n" +
                                 "Access-Control-Allow-Origin: *\r\n" +
                                 "Cache-Control: no-cache, no-store, must-revalidate, max-age=0\r\n" +
                                 "Pragma: no-cache\r\n" +
                                 "Expires: 0\r\n" +
                                 "Connection: close\r\n" +
                                 "Content-Length: " + bodyBytes.Length + "\r\n\r\n";

                writer.WriteString(headers);
                writer.WriteBytes(bodyBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[LocalMusicServer] Client handling error: " + ex.Message);
            }
            finally
            {
                if (reader != null)
                {
                    try { reader.DetachStream(); reader.Dispose(); } catch { }
                }
                if (writer != null)
                {
                    try { writer.DetachStream(); writer.Dispose(); } catch { }
                }
                try { socket.Dispose(); } catch { }
            }
        }

        private static string ExtractQueryParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return null;
            string[] pairs = query.Split('&');
            string prefix = key.ToLowerInvariant() + "=";
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i];
                if (p.ToLowerInvariant().StartsWith(prefix))
                {
                    return p.Substring(prefix.Length);
                }
            }
            return null;
        }

        private static string SerializeStatus(TrackStatusDto s)
        {
            if (s == null) return "{\"hasTrack\":false,\"state\":0}";

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"hasTrack\":").Append(s.HasTrack ? "true" : "false").Append(",");
            sb.Append("\"appName\":\"").Append(EscapeJson(string.IsNullOrEmpty(s.AppName) ? "YTMusicWP" : s.AppName)).Append("\",");
            sb.Append("\"packageName\":\"").Append(EscapeJson(string.IsNullOrEmpty(s.PackageName) ? "com.google.android.apps.youtube.music" : s.PackageName)).Append("\",");
            sb.Append("\"launchUri\":\"").Append(EscapeJson(s.LaunchUri ?? "")).Append("\",");
            sb.Append("\"appIcon\":\"").Append(EscapeJson(string.IsNullOrEmpty(s.AppIcon) ? "http://olmp.local/api/icon" : s.AppIcon)).Append("\",");
            sb.Append("\"title\":\"").Append(EscapeJson(s.Title ?? "")).Append("\",");
            sb.Append("\"artist\":\"").Append(EscapeJson(s.Artist ?? "")).Append("\",");
            sb.Append("\"album\":\"").Append(EscapeJson(s.Album ?? "")).Append("\",");
            sb.Append("\"author\":\"").Append(EscapeJson(s.Author ?? "")).Append("\",");
            sb.Append("\"writer\":\"").Append(EscapeJson(s.Writer ?? "")).Append("\",");
            sb.Append("\"composer\":\"").Append(EscapeJson(s.Composer ?? "")).Append("\",");
            sb.Append("\"albumArtist\":\"").Append(EscapeJson(s.AlbumArtist ?? "")).Append("\",");
            sb.Append("\"mediaUri\":\"").Append(EscapeJson(s.MediaUri ?? "")).Append("\",");
            sb.Append("\"thumbnail\":\"").Append(EscapeJson(s.Thumbnail ?? "")).Append("\",");
            sb.Append("\"thumbnailUrl\":\"").Append(EscapeJson(s.Thumbnail ?? "")).Append("\",");
            sb.Append("\"videoId\":\"").Append(EscapeJson(s.VideoId ?? "")).Append("\",");
            sb.Append("\"trackId\":\"").Append(EscapeJson(s.VideoId ?? "")).Append("\",");
            sb.Append("\"durationMs\":").Append(s.DurationMs).Append(",");
            sb.Append("\"positionMs\":").Append(s.PositionMs).Append(",");
            sb.Append("\"bufferedPositionMs\":").Append(s.BufferedPositionMs).Append(",");
            sb.Append("\"sessionFlags\":").Append(s.SessionFlags).Append(",");
            sb.Append("\"state\":").Append(s.State).Append(",");
            sb.Append("\"speed\":").Append(s.Speed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(",");
            sb.Append("\"actions\":").Append(s.Actions).Append(",");
            sb.Append("\"isPlaying\":").Append(s.IsPlaying ? "true" : "false").Append(",");
            sb.Append("\"canSeek\":").Append((s.Actions & 256L) != 0 ? "true" : "false").Append(",");
            sb.Append("\"canSkipNext\":").Append((s.Actions & 32L) != 0 ? "true" : "false").Append(",");
            sb.Append("\"canSkipPrev\":").Append((s.Actions & 16L) != 0 ? "true" : "false").Append(",");
            sb.Append("\"audioOutput\":\"").Append(EscapeJson(string.IsNullOrEmpty(s.AudioOutput) ? "Điện thoại này" : s.AudioOutput)).Append("\",");
            sb.Append("\"repeatMode\":").Append(s.RepeatMode).Append(",");
            sb.Append("\"shuffleMode\":").Append(s.ShuffleMode).Append(",");
            sb.Append("\"ratingStyle\":").Append(s.RatingStyle).Append(",");
            sb.Append("\"ratingValue\":").Append(s.RatingValue.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(",");
            sb.Append("\"subtitle\":\"").Append(EscapeJson(s.Subtitle ?? "")).Append("\",");
            sb.Append("\"description\":\"").Append(EscapeJson(s.Description ?? "")).Append("\",");
            sb.Append("\"genre\":\"").Append(EscapeJson(s.Genre ?? "")).Append("\",");
            sb.Append("\"compilation\":\"").Append(EscapeJson(s.Compilation ?? "")).Append("\",");
            sb.Append("\"date\":\"").Append(EscapeJson(s.Date ?? "")).Append("\",");
            sb.Append("\"trackNumber\":").Append(s.TrackNumber).Append(",");
            sb.Append("\"numTracks\":").Append(s.NumTracks).Append(",");
            sb.Append("\"year\":").Append(s.Year).Append(",");
            sb.Append("\"discNumber\":").Append(s.DiscNumber).Append(",");
            sb.Append("\"queueTitle\":\"").Append(EscapeJson(s.QueueTitle ?? "")).Append("\",");
            sb.Append("\"activeQueueItemId\":").Append(s.ActiveQueueItemId).Append(",");
            sb.Append("\"playbackType\":").Append(s.PlaybackType).Append(",");
            sb.Append("\"volumeControl\":").Append(s.VolumeControl).Append(",");
            sb.Append("\"volumeControlId\":\"").Append(EscapeJson(string.IsNullOrEmpty(s.VolumeControlId) ? "device_speaker_0" : s.VolumeControlId)).Append("\",");
            sb.Append("\"volume\":").Append(s.Volume).Append(",");
            sb.Append("\"maxVolume\":").Append(s.MaxVolume).Append(",");
            sb.Append("\"errorMessage\":\"").Append(EscapeJson(s.ErrorMessage ?? "")).Append("\",");
            sb.Append("\"isFavorite\":").Append(s.IsFavorite ? "true" : "false").Append(",");
            sb.Append("\"isShuffle\":").Append(s.IsShuffle ? "true" : "false");

            if (s.Queue != null && s.Queue.Count > 0)
            {
                sb.Append(",\"queue\":[");
                for (int i = 0; i < s.Queue.Count; i++)
                {
                    var q = s.Queue[i];
                    if (q == null) continue;
                    if (i > 0) sb.Append(",");
                    sb.Append("{");
                    sb.Append("\"id\":").Append(q.Id).Append(",");
                    sb.Append("\"title\":\"").Append(EscapeJson(q.Title ?? "")).Append("\",");
                    sb.Append("\"subtitle\":\"").Append(EscapeJson(q.Subtitle ?? "")).Append("\",");
                    sb.Append("\"description\":\"").Append(EscapeJson(q.Description ?? "")).Append("\",");
                    sb.Append("\"mediaId\":\"").Append(EscapeJson(q.MediaId ?? "")).Append("\",");
                    sb.Append("\"iconUri\":\"").Append(EscapeJson(q.IconUri ?? "")).Append("\",");
                    sb.Append("\"mediaUri\":\"").Append(EscapeJson(q.MediaUri ?? "")).Append("\",");
                    sb.Append("\"btFolderType\":").Append(q.BtFolderType);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            if (s.CustomActions != null && s.CustomActions.Count > 0)
            {
                sb.Append(",\"customActions\":[");
                for (int i = 0; i < s.CustomActions.Count; i++)
                {
                    var ca = s.CustomActions[i];
                    if (ca == null) continue;
                    if (i > 0) sb.Append(",");
                    sb.Append("{");
                    sb.Append("\"action\":\"").Append(EscapeJson(ca.Action ?? "")).Append("\",");
                    sb.Append("\"name\":\"").Append(EscapeJson(ca.Name ?? "")).Append("\",");
                    sb.Append("\"icon\":\"").Append(EscapeJson(ca.Icon ?? "")).Append("\",");
                    sb.Append("\"isActive\":").Append(ca.IsActive ? "true" : "false");
                    sb.Append("}");
                }
                sb.Append("]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.AppendFormat("\\u{0:X04}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        private static byte[] _cachedAppIconBytes;

        private static async Task<byte[]> GetAppIconBytesAsync()
        {
            if (_cachedAppIconBytes != null && _cachedAppIconBytes.Length > 0)
            {
                return _cachedAppIconBytes;
            }

            string[] candidateFiles = new string[]
            {
                @"Assets\Square71x71Logo.scale-240.png",
                @"Assets\Square71x71Logo.scale-140.png",
                @"Assets\Square71x71Logo.scale-100.png",
                @"Assets\SmallLogo.scale-240.png",
                @"Assets\SmallLogo.scale-140.png",
                @"Assets\SmallLogo.scale-100.png",
                @"Assets\Logo.scale-240.png",
                @"Assets\Logo.scale-140.png",
                @"Assets\Logo.scale-100.png",
                @"Assets\StoreLogo.scale-240.png",
                @"Assets\Square71x71Logo.png",
                @"Assets\SmallLogo.png",
                @"Assets\Logo.png"
            };

            for (int i = 0; i < candidateFiles.Length; i++)
            {
                try
                {
                    string uriStr = "ms-appx:///" + candidateFiles[i].Replace('\\', '/');
                    var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(uriStr));
                    if (file != null)
                    {
                        using (var stream = await file.OpenReadAsync())
                        {
                            if (stream.Size > 0)
                            {
                                var buffer = new byte[stream.Size];
                                using (var reader = new DataReader(stream))
                                {
                                    await reader.LoadAsync((uint)stream.Size);
                                    reader.ReadBytes(buffer);
                                    _cachedAppIconBytes = buffer;
                                    return _cachedAppIconBytes;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Thử tiếp ứng viên kế tiếp trong thư mục Assets
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            if (_listener != null)
            {
                try
                {
                    _listener.ConnectionReceived -= OnConnectionReceived;
                    _listener.Dispose();
                }
                catch { }
                _listener = null;
            }
        }
    }
}
