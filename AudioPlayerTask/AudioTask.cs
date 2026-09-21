using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Playback;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using Windows.Storage;

namespace AudioPlayerTask
{
    public sealed class AudioTask : IBackgroundTask
    {
        private BackgroundTaskDeferral _deferral;
        private SystemMediaTransportControls _systemControls;
        private MediaPlayer _mediaPlayer;

        private static Windows.Web.Http.Filters.HttpBaseProtocolFilter CreateHttpFilter()
        {
            var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter();
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Untrusted);
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
            filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Expired);
            return filter;
        }

        // [OPT] Shared HttpClient with SSL filter — avoids socket leaks & certificate errors on WP8.1
        private Windows.Web.Http.HttpClient _httpClient = new Windows.Web.Http.HttpClient(CreateHttpFilter());

        private readonly object _playlistLock = new object();
        private List<string> _trackList = new List<string>();
        private List<string> _titleList = new List<string>();
        private List<string> _artistList = new List<string>();
        private List<string> _videoIdList = new List<string>();
        private List<string> _thumbnailList = new List<string>();

        private int _currentTrackIndex = -1;
        private Random _rand = new Random();
        private int _retryCount = 0;
        private bool _isRetrying = false;
        private string _currentLoadedVidId = "";

        // Server stream state
        private string _resolvedUrl = null;
        private bool _innerTubeAttempted = false;
        private double _playbackRate = 1.0;
        private DateTime _sleepTimerExpiry = DateTime.MaxValue;
        private volatile bool _isUserPaused = false;
        private volatile bool _startPaused = false;
        private bool _isCurrentTrackLive = false;
        private LiveMediaStreamSource _liveMss = null;
        private SabrMediaStreamSource _sabrMss = null;
        private CancellationTokenSource _sabrCts = null;
        private string _currentLiveBaseUrl = null;
        private long _currentLiveSeq = -1;
        private long _nextLiveStartSeq = -1;
        private int _currentLiveBufferIndex = 0;
        private int _liveBufferCycle = 0;
        private DateTime _lastLiveSwapTime = DateTime.MinValue;
        private bool _isNextLiveBufferReady = false;
        private Task<bool> _nextLiveBufferTask = null;
        private CancellationTokenSource _liveCts = null;
        private int _liveReconnectCount = 0;
        private double _liveBufferDurationSec = 0;
        private double _nextLiveDurationSec = 0;
        private bool _isLiveSwapping = false;
        private bool _isLiveInitializing = false;
        private bool _isPreBuffering = false;
        private DateTime _lastPreBufferFailureTime = DateTime.MinValue;
        private System.Diagnostics.Stopwatch _liveBufferStopwatch = new System.Diagnostics.Stopwatch();
        private System.Diagnostics.Stopwatch _liveBaseUrlStopwatch = new System.Diagnostics.Stopwatch();
        private static readonly System.Threading.SemaphoreSlim _liveDownloadSemaphore = new System.Threading.SemaphoreSlim(3, 3);
        private static readonly System.Threading.SemaphoreSlim _liveRefreshSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        private const int LIVE_INITIAL_SEGMENTS = 4;  // 20s initial buffer (~300KB, fast 1.5s startup)
        private const int LIVE_DEEP_SEGMENTS = 8;     // 40s rolling buffer (~600KB, downloads in ~2s, 14s safe runway)
        private readonly object _logLock = new object();
        private readonly List<string> _pendingLiveLogs = new List<string>();
        private DateTime _lastLogFlushTime = DateTime.MinValue;

        // Tối đa 4 lần retry: Stream URL (2 lần) → Render /api/play (2 lần)
        private const int MAX_RETRIES = 4;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();

            _systemControls = SystemMediaTransportControls.GetForCurrentView();
            _systemControls.IsEnabled = true;
            _systemControls.ButtonPressed += SystemControls_ButtonPressed;
            _systemControls.IsPlayEnabled = true;
            _systemControls.IsPauseEnabled = true;
            _systemControls.IsNextEnabled = true;
            _systemControls.IsPreviousEnabled = true;

            _mediaPlayer = BackgroundMediaPlayer.Current;
            _mediaPlayer.AutoPlay = true;
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (settings.ContainsKey("PlaybackRate"))
                {
                    _playbackRate = Convert.ToDouble(settings["PlaybackRate"]);
                    _mediaPlayer.PlaybackRate = _playbackRate;
                }
            }
            catch { }
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            _mediaPlayer.CurrentStateChanged += MediaPlayer_CurrentStateChanged;

            BackgroundMediaPlayer.MessageReceivedFromForeground += BackgroundMediaPlayer_MessageReceivedFromForeground;
            taskInstance.Canceled += TaskInstance_Canceled;
        }

        private async void TaskInstance_Canceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            try
            {
                LogLive("[Live TaskInstance_Canceled] reason=" + reason);
                FlushLiveLogs();
                _systemControls.ButtonPressed -= SystemControls_ButtonPressed;
                _systemControls.IsEnabled = false;
                _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
                _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
                _mediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;
                _mediaPlayer.CurrentStateChanged -= MediaPlayer_CurrentStateChanged;
                BackgroundMediaPlayer.MessageReceivedFromForeground -= BackgroundMediaPlayer_MessageReceivedFromForeground;
                BackgroundMediaPlayer.Shutdown();
                StopPlaybackMonitor();
                if (_liveCts != null)
                {
                    try { _liveCts.Cancel(); _liveCts.Dispose(); } catch { }
                    _liveCts = null;
                }
                if (_liveMss != null)
                {
                    try { _liveMss.Dispose(); } catch { }
                    _liveMss = null;
                }
                if (_sabrCts != null)
                {
                    try { _sabrCts.Cancel(); _sabrCts.Dispose(); } catch { }
                    _sabrCts = null;
                }
                if (_sabrMss != null)
                {
                    try { _sabrMss.Dispose(); } catch { }
                    _sabrMss = null;
                }
                await CleanupLiveTempFilesAsync();
                _httpClient?.Dispose();
            }
            catch { }
            finally
            {
                if (_deferral != null) _deferral.Complete();
            }
        }

        private void BackgroundMediaPlayer_MessageReceivedFromForeground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            try
            {
                if (e.Data.ContainsKey("UpdatePlaylist"))
                {
                    lock (_playlistLock)
                    {
                        _trackList = new List<string>((string[])e.Data["Urls"]);
                        _titleList = new List<string>((string[])e.Data["Titles"]);
                        _artistList = new List<string>((string[])e.Data["Artists"]);
                        _videoIdList = new List<string>((string[])e.Data["VideoIds"]);
                        _thumbnailList = new List<string>((string[])e.Data["Thumbnails"]);
                        _currentTrackIndex = (int)e.Data["StartIndex"];

                        if (e.Data.ContainsKey("FastUrl"))
                        {
                            string fastUrl = e.Data["FastUrl"].ToString();
                            if (!string.IsNullOrEmpty(fastUrl) && _currentTrackIndex < _trackList.Count)
                            {
                                _trackList[_currentTrackIndex] = fastUrl;
                                // Foreground đã resolve → skip InnerTube trong AudioTask
                                _innerTubeAttempted = true;
                            }
                        }
                    }

                    bool hasFastUrl = _innerTubeAttempted; // set true bởi FastUrl ở trên
                    ResetRetryState();
                    if (hasFastUrl) _innerTubeAttempted = true; // giữ lại → skip double-resolve
                    _currentLoadedVidId = "";
                    _startPaused = e.Data.ContainsKey("StartPaused") && Convert.ToBoolean(e.Data["StartPaused"]);
                    StartPlaybackAsync();
                }
                else if (e.Data.ContainsKey("UpdateQueueOnly"))
                {
                    lock (_playlistLock)
                    {
                        var newUrls = (string[])e.Data["Urls"];
                        var newTitles = (string[])e.Data["Titles"];
                        var newArtists = (string[])e.Data["Artists"];
                        var newVideoIds = (string[])e.Data["VideoIds"];
                        var newThumbnails = (string[])e.Data["Thumbnails"];

                        // Preserve previously resolved URLs across queue updates
                        var oldUrlLookup = new Dictionary<string, string>();
                        if (_videoIdList != null && _trackList != null)
                        {
                            for (int i = 0; i < _videoIdList.Count && i < _trackList.Count; i++)
                            {
                                string v = _videoIdList[i];
                                string u = _trackList[i];
                                if (!string.IsNullOrEmpty(v) && !string.IsNullOrEmpty(u) && !oldUrlLookup.ContainsKey(v))
                                {
                                    oldUrlLookup[v] = u;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(_currentLoadedVidId) && !string.IsNullOrEmpty(_resolvedUrl) && !oldUrlLookup.ContainsKey(_currentLoadedVidId))
                        {
                            oldUrlLookup[_currentLoadedVidId] = _resolvedUrl;
                        }

                        var mergedTrackList = new List<string>(newUrls.Length);
                        for (int i = 0; i < newUrls.Length; i++)
                        {
                            string url = newUrls[i];
                            string vid = (newVideoIds != null && i < newVideoIds.Length) ? newVideoIds[i] : null;
                            if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(vid) && oldUrlLookup.ContainsKey(vid))
                            {
                                url = oldUrlLookup[vid];
                            }
                            mergedTrackList.Add(url ?? "");
                        }

                        _trackList = mergedTrackList;
                        _titleList = new List<string>(newTitles);
                        _artistList = new List<string>(newArtists);
                        _videoIdList = new List<string>(newVideoIds);
                        _thumbnailList = new List<string>(newThumbnails);

                        // Preserve track index of actively playing track
                        string activeVid = !string.IsNullOrEmpty(_currentLoadedVidId)
                            ? _currentLoadedVidId
                            : (_videoIdList != null && _currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count ? _videoIdList[_currentTrackIndex] : null);

                        int foundIndex = -1;
                        if (!string.IsNullOrEmpty(activeVid) && _videoIdList != null)
                        {
                            foundIndex = _videoIdList.IndexOf(activeVid);
                        }

                        if (foundIndex >= 0)
                        {
                            _currentTrackIndex = foundIndex;
                        }
                        else if (e.Data.ContainsKey("CurrentIndex"))
                        {
                            int idx = (int)e.Data["CurrentIndex"];
                            if (idx >= 0 && idx < _trackList.Count)
                            {
                                _currentTrackIndex = idx;
                            }
                        }
                    }
                    ClearPreResolvedState();
                    PreResolveNextTrack();
                }
                else if (e.Data.ContainsKey("NextTrackMessage")) MoveNext();
                else if (e.Data.ContainsKey("PrevTrackMessage")) MovePrevious();
                else if (e.Data.ContainsKey("SetPlaybackRate"))
                {
                    try
                    {
                        _playbackRate = (double)e.Data["SetPlaybackRate"];
                        _mediaPlayer.PlaybackRate = _playbackRate;
                        Windows.Storage.ApplicationData.Current.LocalSettings.Values["PlaybackRate"] = _playbackRate;
                    }
                    catch { }
                }
                else if (e.Data.ContainsKey("SetSleepTimer"))
                {
                    try
                    {
                        int minutes = Convert.ToInt32(e.Data["SetSleepTimer"]);
                        if (minutes <= 0)
                        {
                            _sleepTimerExpiry = DateTime.MaxValue;
                        }
                        else
                        {
                            _sleepTimerExpiry = DateTime.UtcNow.AddMinutes(minutes);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AudioTask] MessageReceived error: " + ex.Message);
            }
        }

        private void ResetRetryState()
        {
            _retryCount = 0;
            _isRetrying = false;
            _resolvedUrl = null;
            _innerTubeAttempted = false;
            if (_isCurrentTrackLive)
            {
                if (_liveCts != null)
                {
                    try { _liveCts.Cancel(); _liveCts.Dispose(); } catch { }
                    _liveCts = null;
                }
                var _ = CleanupLiveTempFilesAsync();
            }
            if (_sabrCts != null)
            {
                try { _sabrCts.Cancel(); _sabrCts.Dispose(); } catch { }
                _sabrCts = null;
            }
            if (_sabrMss != null)
            {
                try { _sabrMss.Dispose(); } catch { }
                _sabrMss = null;
            }
            _isCurrentTrackLive = false;
            _currentLiveBaseUrl = null;
            _currentLiveSeq = -1;
            _nextLiveStartSeq = -1;
            _currentLiveBufferIndex = 0;
            _isNextLiveBufferReady = false;
            _nextLiveBufferTask = null;
            _liveReconnectCount = 0;
            _liveBufferDurationSec = 0;
            _nextLiveDurationSec = 0;
            _isLiveSwapping = false;
            try { _liveBufferStopwatch.Reset(); } catch { }
            try { _liveBaseUrlStopwatch.Reset(); } catch { }
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["IsCurrentLive"] = false;
            }
            catch { }
            ClearPreResolvedState();
        }

        // ==========================================
        // RESOLVE AUDIO URL – InnerTube direct (ANDROID_VR)
        // ==========================================
        private string _innerTubeDebug = "";

        /// <summary>
        /// LÃƒÂ¡Ã‚ÂºÃ‚Â¥y visitorData ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â cache + LocalSettings + sw.js_data endpoint (~2KB, an toÃƒÆ’Ã‚Â n RAM cho background task)
        /// </summary>
        private static string _cachedVisitorData = null;

        private async Task<string> GetVisitorDataAsync(string videoId = null)
        {
            if (!string.IsNullOrEmpty(_cachedVisitorData))
                return _cachedVisitorData;

            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (settings.ContainsKey("CachedVisitorData"))
                {
                    string savedVd = settings["CachedVisitorData"] as string;
                    if (!string.IsNullOrEmpty(savedVd) && savedVd.StartsWith("Cg"))
                    {
                        _cachedVisitorData = savedVd;
                        return savedVd;
                    }
                }
            }
            catch { }

            string vd = await FetchVisitorDataFromSwJs();
            if (!string.IsNullOrEmpty(vd))
            {
                _cachedVisitorData = vd;
                try
                {
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values["CachedVisitorData"] = vd;
                }
                catch { }
                return vd;
            }

            // Guaranteed fallback: valid base visitorData to prevent null and Google Workspace blocks
            const string DEFAULT_VISITOR_DATA = "CgtaRHNGQkVnZlZXcyie1_3UBjIKCgJVUxIEGgAgaA%3D%3D";
            _cachedVisitorData = DEFAULT_VISITOR_DATA;
            return DEFAULT_VISITOR_DATA;
        }

        private async Task<string> FetchVisitorDataFromSwJs()
        {
            try
            {
                string result = null;
                using (var request = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Get,
                    new Uri("https://www.youtube.com/sw.js_data")))
                {
                    request.Headers.TryAppendWithoutValidation("User-Agent",
                        "Mozilla/5.0 (Linux; Andr0id 9; BRAVIA 8K UR2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.125 Safari/537.36 OPR/46.0.2207.0 OMI/4.21.0.273.DIA6.149 Model/Sony-BRAVIA-8K-UR2,gzip(gfe)");
                    request.Headers.Add("Accept", "application/json");

                    using (var response = await _httpClient.SendRequestAsync(request))
                    {
                        if (!response.IsSuccessStatusCode) return null;
                        result = await response.Content.ReadAsStringAsync();
                    }
                }

                if (result.StartsWith(")]}'"))
                    result = result.Substring(4);

                return ExtractVisitorData(result);
            }
            catch { return null; }
        }

        private string ExtractVisitorData(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // 1. TÃƒÆ’Ã‚Â¬m visitorData":"CgXXX"
            string[] markers = { "visitorData\":\"", "\"visitorData\":\"" };
            foreach (string marker in markers)
            {
                int pos = text.IndexOf(marker);
                if (pos >= 0)
                {
                    int start = pos + marker.Length;
                    int end = text.IndexOf("\"", start);
                    if (end > start && end - start >= 20 && end - start < 600)
                    {
                        string vd = System.Net.WebUtility.UrlDecode(text.Substring(start, end - start));
                        if (vd.StartsWith("Cg")) return vd;
                    }
                }
            }

            // 2. TÃƒÆ’Ã‚Â¬m "CgXXX" trong array format cÃƒÂ¡Ã‚Â»Ã‚Â§a sw.js_data
            int searchPos = 0;
            while (searchPos < text.Length)
            {
                int quotePos = text.IndexOf("\"Cg", searchPos);
                if (quotePos < 0) break;

                int start2 = quotePos + 1; // skip opening quote
                int end2 = text.IndexOf("\"", start2);
                if (end2 > start2)
                {
                    int len = end2 - start2;
                    if (len >= 20 && len < 600)
                    {
                        string candidate = System.Net.WebUtility.UrlDecode(text.Substring(start2, len));
                        if (candidate.StartsWith("Cg"))
                        {
                            bool valid = true;
                            for (int i = 0; i < candidate.Length && valid; i++)
                            {
                                char c = candidate[i];
                                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                                      (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '=' || c == '+' || c == '/'))
                                    valid = false;
                            }
                            if (valid) return candidate;
                        }
                    }
                }
                searchPos = quotePos + 3;
            }

            return null;
        }

        private class RemotePoTokenResult
        {
            public string PoToken;
            public string VisitorData;
        }

        private static string ExtractJsonField(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains("\"" + key + "\"")) return null;
            int idx = json.IndexOf("\"" + key + "\"");
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + key.Length + 2);
            if (colon < 0) return null;
            int startQuote = json.IndexOf('"', colon);
            if (startQuote < 0) return null;
            int endQuote = json.IndexOf('"', startQuote + 1);
            if (endQuote > startQuote)
            {
                return json.Substring(startQuote + 1, endQuote - startQuote - 1);
            }
            return null;
        }

        private static RemotePoTokenResult _cachedPoTokenResult = null;
        private static DateTime _poTokenExpiry = DateTime.MinValue;

        private async Task<RemotePoTokenResult> FetchRemotePoTokenAsync(string videoId, string clientName)
        {
            if (_cachedPoTokenResult != null && DateTime.UtcNow < _poTokenExpiry)
            {
                return _cachedPoTokenResult;
            }

            // Check LocalSettings in case MainPage already fetched it
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (ls.ContainsKey("CachedPoToken") && ls.ContainsKey("CachedPoTokenExpiry"))
                {
                    long expiryTicks;
                    if (long.TryParse(ls["CachedPoTokenExpiry"]?.ToString(), out expiryTicks))
                    {
                        var expiry = new DateTime(expiryTicks, DateTimeKind.Utc);
                        if (DateTime.UtcNow < expiry)
                        {
                            string token = ls["CachedPoToken"]?.ToString();
                            string tokenVd = ls.ContainsKey("CachedPoTokenVd") ? ls["CachedPoTokenVd"]?.ToString() : null;
                            if (!string.IsNullOrEmpty(token))
                            {
                                _cachedPoTokenResult = new RemotePoTokenResult { PoToken = token, VisitorData = tokenVd };
                                _poTokenExpiry = expiry;
                                return _cachedPoTokenResult;
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                // Let Cloudflare Worker generate its matching poToken
                string serverUrl = "https://potoken-api.nguyentruongan06052007.workers.dev/";
                string body = "{}";
                
                var filter = CreateHttpFilter();
                
                using (var httpClient = new Windows.Web.Http.HttpClient(filter))
                {
                    using (var content = new Windows.Web.Http.HttpStringContent(body, Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json"))
                    using (var resp = await httpClient.PostAsync(new Uri(serverUrl), content))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        string json = await resp.Content.ReadAsStringAsync();

                        var result = new RemotePoTokenResult();
                        Windows.Data.Json.JsonObject obj;
                        if (Windows.Data.Json.JsonObject.TryParse(json, out obj))
                        {
                            if (obj.ContainsKey("po_token")) result.PoToken = obj.GetNamedString("po_token");
                            else if (obj.ContainsKey("poToken")) result.PoToken = obj.GetNamedString("poToken");

                            if (obj.ContainsKey("visitor_data")) result.VisitorData = obj.GetNamedString("visitor_data");
                            else if (obj.ContainsKey("visitorData")) result.VisitorData = obj.GetNamedString("visitorData");
                            else if (obj.ContainsKey("visit_identifier")) result.VisitorData = obj.GetNamedString("visit_identifier");
                            else if (obj.ContainsKey("contentBinding")) result.VisitorData = obj.GetNamedString("contentBinding");
                            else if (obj.ContainsKey("content_binding")) result.VisitorData = obj.GetNamedString("content_binding");
                        }
                        else
                        {
                            result.PoToken = ExtractJsonField(json, "po_token") ?? ExtractJsonField(json, "poToken");
                            result.VisitorData = ExtractJsonField(json, "visitor_data") ?? ExtractJsonField(json, "visitorData") ?? ExtractJsonField(json, "visit_identifier") ?? ExtractJsonField(json, "contentBinding") ?? ExtractJsonField(json, "content_binding");
                        }

                        if (!string.IsNullOrEmpty(result.PoToken))
                        {
                            _cachedPoTokenResult = result;
                            _poTokenExpiry = DateTime.UtcNow.AddMinutes(50);
                            try
                            {
                                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                                ls["CachedPoToken"] = result.PoToken;
                                ls["CachedPoTokenExpiry"] = _poTokenExpiry.Ticks.ToString();
                                if (!string.IsNullOrEmpty(result.VisitorData))
                                    ls["CachedPoTokenVd"] = result.VisitorData;
                            }
                            catch { }
                            return result;
                        }
                        return null;
                    }
                }
            }
            catch { return null; }
        }

        private string GenerateSAPISIDHash(string sapisid)
        {
            long timestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string input = timestamp + " " + sapisid + " https://music.youtube.com";
            var provider = Windows.Security.Cryptography.Core.HashAlgorithmProvider.OpenAlgorithm(
                Windows.Security.Cryptography.Core.HashAlgorithmNames.Sha1);
            var buffer = Windows.Security.Cryptography.CryptographicBuffer.ConvertStringToBinary(
                input, Windows.Security.Cryptography.BinaryStringEncoding.Utf8);
            var hashBuffer = provider.HashData(buffer);
            string hash = Windows.Security.Cryptography.CryptographicBuffer.EncodeToHexString(hashBuffer);
            return "SAPISIDHASH " + timestamp + "_" + hash;
        }

        private async Task<string> ResolveViaInnerTubeDirectAsync(string videoId)
        {
            string initVd = await GetVisitorDataAsync(videoId);
            _innerTubeDebug = "vd:" + (!string.IsNullOrEmpty(initVd) ? "OK" : "NULL");
            
            // 0. InnerTube ANDROID v20.49.37 (Ưu tiên số 1 - không bị bóp băng thông/throttling, lấy itag 18)
            string url = await TryInnerTubeClient(videoId, "ANDROID", "20.49.37", "3", "Nokia", "LumiaWP", "Android", "11",
                "com.google.android.youtube/20.49.37 (Linux; U; Android 11) gzip", false);
            if (!string.IsNullOrEmpty(url)) return url;

            // 0.5. InnerTube ANDROID v21.02.35 (Dự phòng phiên bản Android mới nhất Pixel 7)
            url = await TryInnerTubeClient(videoId, "ANDROID", "21.02.35", "3", "Google", "Pixel 7", "Android", "11",
                "com.google.android.youtube/21.02.35 (Linux; U; Android 11) gzip", false);
            if (!string.IsNullOrEmpty(url)) return url;

            // 1. Cookie Auth (WEB_REMIX) - 100% bypasses BotGuard if logged in
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (settings.ContainsKey("GoogleCookieString") && settings.ContainsKey("GoogleSAPISID"))
            {
                string cookie = settings["GoogleCookieString"]?.ToString();
                string sapisid = settings["GoogleSAPISID"]?.ToString();
                if (!string.IsNullOrEmpty(cookie) && !string.IsNullOrEmpty(sapisid))
                {
                    string auth = GenerateSAPISIDHash(sapisid);
                    string urlCookie = await TryInnerTubeClient(videoId, "WEB_REMIX", "1.20231214.00.00", "86", "Windows", "PC", "Windows", "10",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", false, cookie, auth);
                    if (!string.IsNullOrEmpty(urlCookie)) return urlCookie;
                }
            }

            // 2. VISIONOS (lấy itag 140 trực tiếp không mã hóa)
            url = await TryInnerTubeClient(videoId, "VISIONOS", "1.02", "101", "Apple", "RealityDevice14,1", "visionOS", "1.0.2.21O209",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15", false);
            if (!string.IsNullOrEmpty(url)) return url;

            // 3. ANDROID_VR (Meta Oculus Quest - lấy itag 18 và itag 140 trực tiếp)
            url = await TryInnerTubeClient(videoId, "ANDROID_VR", "1.65.10", "28", "Oculus", "Quest 3", "Android", "12",
                "com.google.android.apps.youtube.vr.oculus/1.65.10 (Linux; U; Android 12) gzip", false);
            if (!string.IsNullOrEmpty(url)) return url;

            // 4. Fallback cuối cùng: Thử VISIONOS với poToken từ Render server (vượt qua BotGuard)
            url = await TryInnerTubeClient(videoId, "VISIONOS", "1.02", "101", "Apple", "RealityDevice14,1", "visionOS", "1.0.2.21O209",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15", true, null, null, "AIzaSyB-63vPrdThhKuerbB2N_l7Kwwcxj6yUAc");
            if (!string.IsNullOrEmpty(url)) return url;

            return null;
        }

        private async Task<string> TryInnerTubeClient(string videoId, string clientName, string clientVersion, 
            string clientId, string deviceMake, string deviceModel, string osName, string osVersion, string userAgent, bool usePoToken, string cookie = null, string auth = null, string apiKey = null)
        {
            try
            {
                string visitorData = await GetVisitorDataAsync(videoId);

                string poTokenField = "";
                RemotePoTokenResult tokenInfo = null;
                if (usePoToken)
                {
                    tokenInfo = await FetchRemotePoTokenAsync(videoId, clientName);
                    if (tokenInfo != null && !string.IsNullOrEmpty(tokenInfo.PoToken))
                    {
                        poTokenField = ",\"serviceIntegrityDimensions\":{\"poToken\":\"" + tokenInfo.PoToken + "\"}";
                    }
                }

                string vdField = "";
                if (!string.IsNullOrEmpty(visitorData))
                    vdField = ",\"visitorData\":\"" + visitorData + "\"";

                string platformField = "";
                if (clientName == "ANDROID" || clientName == "ANDROID_VR")
                {
                    platformField = "\"platform\":\"MOBILE\",\"clientFormFactor\":0,";
                }
                string sdkVersionField = clientName == "ANDROID" ? "\"androidSdkVersion\":30," : "";

                string requestBody = "{" +
                    "\"contentCheckOk\":true," +
                    "\"racyCheckOk\":true," +
                    "\"context\":{\"client\":{" +
                        "\"clientName\":\"" + clientName + "\"," +
                        "\"clientVersion\":\"" + clientVersion + "\"," +
                        "\"deviceMake\":\"" + deviceMake + "\"," +
                        "\"deviceModel\":\"" + deviceModel + "\"," +
                        "\"userAgent\":\"" + userAgent + "\"," +
                        "\"osName\":\"" + osName + "\"," +
                        "\"osVersion\":\"" + osVersion + "\"," +
                        platformField +
                        sdkVersionField +
                        "\"hl\":\"en\"," +
                        "\"gl\":\"US\"" +
                        vdField +
                    "}}" + poTokenField + "," +
                    "\"videoId\":\"" + videoId + "\"" +
                "}";

                string key = !string.IsNullOrEmpty(apiKey) ? apiKey : (clientName == "IOS" || clientName == "VISIONOS" ? "AIzaSyB-63vPrdThhKuerbB2N_l7Kwwcxj6yUAc" : "AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI");
                string json;
                using (var content = new Windows.Web.Http.HttpStringContent(
                    requestBody,
                    Windows.Storage.Streams.UnicodeEncoding.Utf8,
                    "application/json"
                ))
                using (var request = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Post,
                    new Uri("https://www.youtube.com/youtubei/v1/player?key=" + key + "&prettyPrint=false&fields=playabilityStatus,streamingData,playerConfig")))
                {
                    request.Content = content;
                    request.Headers.TryAppendWithoutValidation("User-Agent", userAgent);
                    request.Headers.Add("X-YouTube-Client-Name", clientId);
                    request.Headers.Add("X-YouTube-Client-Version", clientVersion);
                    
                    if (!string.IsNullOrEmpty(cookie) && !string.IsNullOrEmpty(auth))
                    {
                        request.Headers.Add("Cookie", cookie);
                        request.Headers.Add("Authorization", auth);
                        request.Headers.Add("Origin", "https://music.youtube.com");
                    }

                    using (var reqCts = new CancellationTokenSource(5000))
                    using (var response = await _httpClient.SendRequestAsync(request).AsTask(reqCts.Token))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _innerTubeDebug += " [" + clientName + (usePoToken ? "+po" : "") + ":H" + (int)response.StatusCode + "]";
                            return null;
                        }
                        json = await response.Content.ReadAsStringAsync().AsTask(reqCts.Token);
                    }
                }

                Windows.Data.Json.JsonObject data;
                if (Windows.Data.Json.JsonObject.TryParse(json, out data))
                {
                    string status = "";
                    if (data.ContainsKey("playabilityStatus"))
                    {
                        var pStatus = data.GetNamedObject("playabilityStatus");
                        if (pStatus.ContainsKey("status"))
                            status = pStatus.GetNamedString("status");
                    }

                    if (status != "OK")
                    {
                        _innerTubeDebug += " [" + clientName + (usePoToken ? "+po" : "") + ":" + status + "]";
                        return null;
                    }

                    int[] preferredItags = new[] { 18, 140, 141, 139 };

                    if (data.ContainsKey("streamingData"))
                    {
                        var streamingData = data.GetNamedObject("streamingData");

                        // Kiểm tra setting ForceSabr: ép dùng SABR cho toàn bộ bài hát để kiểm thử
                        bool forceSabr = false;
                        try
                        {
                            var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                            if (ls.ContainsKey("ForceSabr") && (bool)ls["ForceSabr"]) forceSabr = true;
                        }
                        catch { }

                        if (forceSabr && streamingData.ContainsKey("serverAbrStreamingUrl"))
                        {
                            string sabrUrl = streamingData.GetNamedString("serverAbrStreamingUrl");
                            if (!string.IsNullOrEmpty(sabrUrl))
                            {
                                string ustreamerConfig = "";
                                if (streamingData.ContainsKey("ustreamerConfig"))
                                {
                                    ustreamerConfig = streamingData.GetNamedString("ustreamerConfig");
                                }
                                else if (data.ContainsKey("playerConfig"))
                                {
                                    try
                                    {
                                        var pcfg = data.GetNamedObject("playerConfig");
                                        var mcfg = pcfg.GetNamedObject("mediaCommonConfig");
                                        var ucfg = mcfg.GetNamedObject("mediaUstreamerRequestConfig");
                                        ustreamerConfig = ucfg.GetNamedString("videoPlaybackUstreamerConfig");
                                    }
                                    catch { }
                                }

                                _innerTubeDebug += " [" + clientName + ":SABR_FORCED:OK]";
                                return "SABR:" + sabrUrl + "|" + (ustreamerConfig ?? "") + "|" + (userAgent ?? "") + "|" + (clientId ?? "") + "|" + (clientVersion ?? "") + "|" + (tokenInfo != null ? tokenInfo.PoToken ?? "" : "");
                            }
                        }

                        // 1. Ưu tiên itag 18 từ formats (không bị bóp băng thông)
                        if (streamingData.ContainsKey("formats"))
                        {
                            var formats = streamingData.GetNamedArray("formats");
                            foreach (var fmtVal in formats)
                            {
                                if (fmtVal.ValueType == Windows.Data.Json.JsonValueType.Object)
                                {
                                    var fmt = fmtVal.GetObject();
                                    if (fmt.ContainsKey("itag"))
                                    {
                                        int itag = (int)fmt.GetNamedNumber("itag");
                                        if (itag == 18 && fmt.ContainsKey("url"))
                                        {
                                            _innerTubeDebug += " [" + clientName + ":i18:OK]";
                                            return fmt.GetNamedString("url");
                                        }
                                    }
                                }
                            }
                        }

                        // 2. Fallback xuống adaptiveFormats (âm thanh chuyên dụng)
                        if (streamingData.ContainsKey("adaptiveFormats"))
                        {
                            var formats = streamingData.GetNamedArray("adaptiveFormats");
                            foreach (int targetItag in preferredItags)
                            {
                                foreach (var fmtVal in formats)
                                {
                                    if (fmtVal.ValueType == Windows.Data.Json.JsonValueType.Object)
                                    {
                                        var fmt = fmtVal.GetObject();
                                        if (fmt.ContainsKey("itag"))
                                        {
                                            int itag = (int)fmt.GetNamedNumber("itag");
                                            if (itag == targetItag && fmt.ContainsKey("url"))
                                            {
                                                _innerTubeDebug += " [" + clientName + (usePoToken ? "+po" : "") + ":i" + targetItag + ":OK]";
                                                return fmt.GetNamedString("url");
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // 3. Fallback cho SABR stream: serverAbrStreamingUrl (Ưu tiên số 1 cho livestream thay cho LiveMediaStreamSource)
                        if (streamingData.ContainsKey("serverAbrStreamingUrl"))
                        {
                            string sabrUrl = streamingData.GetNamedString("serverAbrStreamingUrl");
                            if (!string.IsNullOrEmpty(sabrUrl))
                            {
                                string ustreamerConfig = "";
                                if (streamingData.ContainsKey("ustreamerConfig"))
                                {
                                    ustreamerConfig = streamingData.GetNamedString("ustreamerConfig");
                                }
                                else if (data.ContainsKey("playerConfig"))
                                {
                                    try
                                    {
                                        var pcfg = data.GetNamedObject("playerConfig");
                                        var mcfg = pcfg.GetNamedObject("mediaCommonConfig");
                                        var ucfg = mcfg.GetNamedObject("mediaUstreamerRequestConfig");
                                        ustreamerConfig = ucfg.GetNamedString("videoPlaybackUstreamerConfig");
                                    }
                                    catch { }
                                }

                                _innerTubeDebug += " [" + clientName + ":SABR:OK]";
                                return "SABR:" + sabrUrl + "|" + (ustreamerConfig ?? "") + "|" + (userAgent ?? "") + "|" + (clientId ?? "") + "|" + (clientVersion ?? "") + "|" + (tokenInfo != null ? tokenInfo.PoToken ?? "" : "");
                            }
                        }

                        // 4. Fallback cho Live stream qua DASH: [TẠM THỜI VÔ HIỆU HÓA để test SABR theo yêu cầu người dùng]
                        /*
                        if (streamingData.ContainsKey("dashManifestUrl"))
                        {
                            string dashUrl = streamingData.GetNamedString("dashManifestUrl");
                            if (!string.IsNullOrEmpty(dashUrl))
                            {
                                try
                                {
                                    using (var dashReq = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Get, new Uri(dashUrl)))
                                    {
                                        dashReq.Headers.TryAppendWithoutValidation("User-Agent", userAgent);
                                        using (var dashCts = new CancellationTokenSource(5000))
                                        using (var dashResp = await _httpClient.SendRequestAsync(dashReq).AsTask(dashCts.Token))
                                        {
                                            if (dashResp.IsSuccessStatusCode)
                                            {
                                                string dashBaseUrl = await ExtractDashAudioBaseUrlFromStreamAsync(dashResp);
                                                if (!string.IsNullOrEmpty(dashBaseUrl))
                                                {
                                                    _currentLiveBaseUrl = dashBaseUrl;
                                                    _isCurrentTrackLive = true;
                                                    try { _liveBaseUrlStopwatch.Restart(); } catch { }
                                                    _innerTubeDebug += " [" + clientName + ":DASH:OK]";
                                                    return dashBaseUrl;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _innerTubeDebug += " [DASH_EX:" + ex.Message.Substring(0, Math.Min(15, ex.Message.Length)) + "]";
                                }
                            }
                        }
                        */
                    }
                }

                _innerTubeDebug += " [" + clientName + (usePoToken ? "+po" : "") + ":NO_URL]";
                return null;
            }
            catch (Exception ex)
            {
                _innerTubeDebug += " [" + clientName + (usePoToken ? "+po" : "") + ":EX:" + ex.Message.Substring(0, Math.Min(20, ex.Message.Length)) + "]";
                return null;
            }
        }


        // ==========================================
        // MAIN PLAYBACK ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â InnerTube direct only
        // MAIN PLAYBACK – InnerTube direct only
        // ==========================================
        private async void StartPlaybackAsync()
        {
            try
            {
                string vidId;
                string initialTrackUrl;
                lock (_playlistLock)
                {
                    if (_currentTrackIndex < 0 || _currentTrackIndex >= _trackList.Count || _currentTrackIndex >= _videoIdList.Count) return;
                    vidId = _videoIdList[_currentTrackIndex];
                    initialTrackUrl = _trackList[_currentTrackIndex];
                }

                int currentSeq = ++_playbackSequence;

                // Offline track: phát trực tiếp (nếu bài cũ vẫn mở thì tua về 0)
                if (vidId.StartsWith("LOCAL:"))
                {
                    if (vidId == _currentLoadedVidId && _mediaPlayer.CurrentState != MediaPlayerState.Closed && _retryCount == 0)
                    {
                        try { _mediaPlayer.Position = TimeSpan.Zero; _mediaPlayer.Play(); _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing; UpdateSystemMediaControls(); }
                        catch { }
                        return;
                    }
                    PlayUrl(initialTrackUrl, vidId);
                    return;
                }

                // Nếu đã có URL resolved (từ retry)
                // Fetch SponsorBlock segments
                _skipSegments = new List<YTMusicWP.Models.SponsorBlockSegment>();
                if (!vidId.StartsWith("LOCAL:"))
                {
                    var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                    bool sponsorBlock = ls.ContainsKey("SponsorBlock") ? (bool)ls["SponsorBlock"] : true;
                    if (sponsorBlock)
                    {
                        var ignoreTask = System.Threading.Tasks.Task.Run(async () => {
                            try
                            {
                                var segs = await YTMusicWP.Services.SponsorBlockApi.GetSkipSegmentsAsync(vidId);
                                if (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count && _videoIdList[_currentTrackIndex] == vidId) {
                                    _skipSegments = segs;
                                }
                            }
                            catch { }
                        });
                    }
                }
                if (!string.IsNullOrEmpty(_resolvedUrl))
                {
                    string url = _resolvedUrl;
                    _resolvedUrl = null;
                    PlayUrl(url, vidId);
                    return;
                }

                if (!_innerTubeAttempted)
                {
                    _innerTubeAttempted = true;
                    UpdateSystemMediaControls();

                    // Stop previous audio immediately so old audio doesn't play while loading next track
                    try { if (_mediaPlayer.CurrentState == MediaPlayerState.Playing) _mediaPlayer.Pause(); } catch { }

                    string directUrl = await ResolveViaInnerTubeDirectAsync(vidId);
                    if (currentSeq != _playbackSequence) return;
                    if (_currentTrackIndex < 0 || _currentTrackIndex >= _videoIdList.Count || _videoIdList[_currentTrackIndex] != vidId) return;

                    if (!string.IsNullOrEmpty(directUrl))
                    {
                        directUrl = PrepareStreamUrl(directUrl);
                        lock (_playlistLock)
                        {
                            if (_currentTrackIndex >= 0 && _currentTrackIndex < _trackList.Count)
                            {
                                _trackList[_currentTrackIndex] = directUrl;
                            }
                        }
                        PlayUrl(directUrl, vidId);
                        return;
                    }
                }

                // FALLBACK: URL từ MainPage nếu có sẵn (chỉ resolve nếu chưa thử)
                string fallbackUrl;
                lock (_playlistLock)
                {
                    fallbackUrl = (_currentTrackIndex >= 0 && _currentTrackIndex < _trackList.Count) ? _trackList[_currentTrackIndex] : null;
                }
                if (string.IsNullOrEmpty(fallbackUrl) && !_innerTubeAttempted)
                {
                    fallbackUrl = await ResolveViaInnerTubeDirectAsync(vidId);
                    if (currentSeq != _playbackSequence) return;
                    if (_currentTrackIndex < 0 || _currentTrackIndex >= _videoIdList.Count || _videoIdList[_currentTrackIndex] != vidId) return;
                }
                if (!string.IsNullOrEmpty(fallbackUrl))
                {
                    PlayUrl(PrepareStreamUrl(fallbackUrl), vidId);
                }
                else
                {
                    if (currentSeq == _playbackSequence)
                        ReportErrorToUI("No stream available: " + _innerTubeDebug);
                }
            }
            catch (Exception ex)
            {
                ReportErrorToUI("Playback error: " + ex.Message);
            }
        }

        /// <summary>
        /// Chuẩn bị URL stream: giữ nguyên URL signed từ YouTube để tránh 403 Forbidden
        /// </summary>
        private string PrepareStreamUrl(string url)
        {
            return url;
        }

        private static async Task<string> ExtractDashAudioBaseUrlFromStreamAsync(Windows.Web.Http.HttpResponseMessage resp)
        {
            try
            {
                string xml = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(xml)) return null;

                // Priority 1: itag 140 (AAC 44.1kHz Stereo 128kbps) - REQUIRED for LiveMediaStreamSource
                int idx = xml.IndexOf("id=\"140\"", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    idx = xml.IndexOf("id=\"139\"", StringComparison.OrdinalIgnoreCase);
                }

                if (idx >= 0)
                {
                    int bStart = xml.IndexOf("<BaseURL", idx, StringComparison.OrdinalIgnoreCase);
                    if (bStart >= 0)
                    {
                        int bContentStart = xml.IndexOf('>', bStart);
                        if (bContentStart >= 0)
                        {
                            bContentStart++;
                            int bEnd = xml.IndexOf("</BaseURL>", bContentStart, StringComparison.OrdinalIgnoreCase);
                            if (bEnd > bContentStart)
                            {
                                string url = xml.Substring(bContentStart, bEnd - bContentStart).Trim();
                                return url.Replace("&amp;", "&");
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static int FindMoofOffset(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8) return -1;
            int limit = Math.Min(bytes.Length - 4, 2048);
            for (int i = 4; i < limit; i++)
            {
                if (bytes[i] == (byte)'m' && bytes[i + 1] == (byte)'o' && bytes[i + 2] == (byte)'o' && bytes[i + 3] == (byte)'f')
                {
                    return i - 4;
                }
            }
            return -1;
        }

        private async Task<string> RefreshLiveBaseUrlAsync(string vidId, CancellationToken ct, bool force = false)
        {
            if (string.IsNullOrEmpty(vidId) || ct.IsCancellationRequested) return null;

            // Reuse existing BaseURL if fetched recently (< 15s ago) unless forced
            if (!force && !string.IsNullOrEmpty(_currentLiveBaseUrl) &&
                _liveBaseUrlStopwatch.IsRunning && _liveBaseUrlStopwatch.Elapsed.TotalSeconds < 15.0)
            {
                return _currentLiveBaseUrl;
            }

            try
            {
                await _liveRefreshSemaphore.WaitAsync(ct);
            }
            catch
            {
                return _currentLiveBaseUrl;
            }

            try
            {
                // Re-check after acquiring semaphore
                if (!force && !string.IsNullOrEmpty(_currentLiveBaseUrl) &&
                    _liveBaseUrlStopwatch.IsRunning && _liveBaseUrlStopwatch.Elapsed.TotalSeconds < 15.0)
                {
                    return _currentLiveBaseUrl;
                }

                LogLive("[Live Refresh URL] Đang lấy BaseURL mới cho " + vidId + "...");
                _cachedVisitorData = null;
                string freshUrl = await ResolveViaInnerTubeDirectAsync(vidId);
                if (!string.IsNullOrEmpty(freshUrl) && IsLiveStreamUrl(freshUrl))
                {
                    int sqIdx = freshUrl.IndexOf("#sq=");
                    if (sqIdx >= 0) freshUrl = freshUrl.Substring(0, sqIdx);
                    _currentLiveBaseUrl = freshUrl;
                    try { _liveBaseUrlStopwatch.Restart(); } catch { }
                    long freshHead = await GetLatestLiveSeqAsync(freshUrl, ct);
                    if (freshHead > 0 && freshHead > _currentLiveSeq) _currentLiveSeq = freshHead;
                    LogLive("[Live Refresh URL Xong] URL mới seq=" + _currentLiveSeq);
                    try { GC.Collect(); } catch { }
                    return freshUrl;
                }
            }
            catch (Exception ex)
            {
                LogLive("[Live Refresh URL Lỗi] " + ex.Message);
            }
            finally
            {
                _liveRefreshSemaphore.Release();
            }
            return _currentLiveBaseUrl;
        }

        private async Task<long> GetLatestLiveSeqAsync(string baseUrl, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(baseUrl)) return -1;
            try
            {
                using (var request = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Head, new Uri(baseUrl)))
                {
                    request.Headers.TryAppendWithoutValidation("User-Agent", "com.google.android.youtube/20.49.37 (Linux; U; Android 11) gzip");
                    using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeoutCts.CancelAfter(3500);
                        using (var response = await _httpClient.SendRequestAsync(request).AsTask(timeoutCts.Token))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                string seqHeader;
                                if ((response.Headers.TryGetValue("X-Head-Seqnum", out seqHeader) || response.Headers.TryGetValue("X-Sequence-Num", out seqHeader)) && !string.IsNullOrEmpty(seqHeader))
                                {
                                    long parsedSeq;
                                    if (long.TryParse(seqHeader, out parsedSeq))
                                    {
                                        if (parsedSeq > _currentLiveSeq) _currentLiveSeq = parsedSeq;
                                        return parsedSeq;
                                    }
                                }
                            }
                            else if (response.StatusCode == Windows.Web.Http.HttpStatusCode.Forbidden)
                            {
                                _currentLiveBaseUrl = null;
                            }
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        private async Task<byte[]> DownloadLiveSegmentAsync(string baseUrl, long seq, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(baseUrl)) return null;
            string segUrl = baseUrl + (baseUrl.EndsWith("/") ? "" : "/") + "sq/" + seq;

            try
            {
                using (var request = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Get, new Uri(segUrl)))
                {
                    request.Headers.TryAppendWithoutValidation("User-Agent", "com.google.android.youtube/20.49.37 (Linux; U; Android 11) gzip");
                    using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeoutCts.CancelAfter(4500);
                        using (var response = await _httpClient.SendRequestAsync(request).AsTask(timeoutCts.Token))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                string headSeqStr;
                                if ((response.Headers.TryGetValue("X-Head-Seqnum", out headSeqStr) || response.Headers.TryGetValue("X-Sequence-Num", out headSeqStr)) && !string.IsNullOrEmpty(headSeqStr))
                                {
                                    long hSeq;
                                    if (long.TryParse(headSeqStr, out hSeq) && hSeq > _currentLiveSeq)
                                    {
                                        _currentLiveSeq = hSeq;
                                    }
                                }

                            var buffer = await response.Content.ReadAsBufferAsync().AsTask(timeoutCts.Token);
                            byte[] bytes = new byte[buffer.Length];
                            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
                            {
                                reader.ReadBytes(bytes);
                            }
                            return bytes;
                        }
                        else
                        {
                            if (response.StatusCode == Windows.Web.Http.HttpStatusCode.Forbidden)
                            {
                                _currentLiveBaseUrl = null;
                                return null;
                            }
                            if (!ct.IsCancellationRequested)
                            {
                                LogLive("[Live Seg " + seq + " HttpErr] code=" + (int)response.StatusCode);
                            }
                        }
                    }
                }
            }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    LogLive("[Live Seg " + seq + " Ex] " + ex.Message);
                }
            }

            return null;
        }

        private async Task<byte[]> DownloadLiveSegmentWithRetryAsync(string baseUrl, long seq, CancellationToken ct)
        {
            try
            {
                await _liveDownloadSemaphore.WaitAsync(ct);
            }
            catch
            {
                return null;
            }

            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (ct.IsCancellationRequested) return null;
                    var bytes = await DownloadLiveSegmentAsync(baseUrl, seq, ct);
                    if (bytes != null && bytes.Length > 0) return bytes;
                    if (_currentLiveBaseUrl == null) break; // 403 Forbidden: BaseURL expired, do not retry with the same expired URL
                    try { await Task.Delay(150, ct); } catch { return null; }
                }
                return null;
            }
            finally
            {
                _liveDownloadSemaphore.Release();
            }
        }

        private async Task<int> AssembleLiveBufferAsync(string baseUrl, long startSeq, int count, string fileName, CancellationToken ct)
        {
            try
            {
                // Check current live edge to prevent overrun into unencoded segments
                long headSeq = await GetLatestLiveSeqAsync(baseUrl, ct);
                if (headSeq > 0)
                {
                    _currentLiveSeq = headSeq;
                    long maxSafeSeq = headSeq - 1;
                    if (startSeq + count - 1 > maxSafeSeq)
                    {
                        int clamped = (int)(maxSafeSeq - startSeq + 1);
                        if (clamped <= 0)
                        {
                            // If caught up to live edge, wait 4s for YouTube to publish the next chunk and re-check
                            LogLive("[Live Pacing] Chạm mép live (start=" + startSeq + ", head=" + headSeq + "), chờ 4s chunk mới...");
                            try { await Task.Delay(4000, ct); } catch { return 0; }
                            headSeq = await GetLatestLiveSeqAsync(baseUrl, ct);
                            if (headSeq > 0)
                            {
                                _currentLiveSeq = headSeq;
                                maxSafeSeq = headSeq - 1;
                                clamped = (int)(maxSafeSeq - startSeq + 1);
                            }
                        }

                        if (clamped > 0 && clamped < count)
                        {
                            LogLive("[Live Pacing] Clamp count " + count + " -> " + clamped + " (head=" + headSeq + ")");
                            count = clamped;
                        }
                        else if (clamped <= 0)
                        {
                            LogLive("[Live Pacing] Vẫn chưa có chunk mới, hoãn tải");
                            return 0;
                        }
                    }
                }

                // Download all segments in parallel using Task.WhenAll with semaphore throttling (max 3 concurrent)
                var downloadTasks = new Task<byte[]>[count];
                for (int i = 0; i < count; i++)
                {
                    long targetSeq = startSeq + i;
                    downloadTasks[i] = DownloadLiveSegmentWithRetryAsync(baseUrl, targetSeq, ct);
                }

                byte[][] segments = await Task.WhenAll(downloadTasks);
                if (ct.IsCancellationRequested) return 0;

                // If segments failed (likely 403 Forbidden on expired BaseURL), refresh and retry once
                if (segments == null || segments.Length == 0 || segments[0] == null || segments[0].Length == 0)
                {
                    string curVid;
                    lock (_playlistLock)
                    {
                        curVid = (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count)
                            ? _videoIdList[_currentTrackIndex] : null;
                    }
                    if (!string.IsNullOrEmpty(curVid) && !ct.IsCancellationRequested)
                    {
                        LogLive("[Live Assemble] Chunk thất bại (có thể 403), đang refresh BaseURL và thử lại...");
                        string freshBase = await RefreshLiveBaseUrlAsync(curVid, ct, true);
                        if (!string.IsNullOrEmpty(freshBase))
                        {
                            baseUrl = freshBase;
                            for (int i = 0; i < count; i++)
                            {
                                long targetSeq = startSeq + i;
                                downloadTasks[i] = DownloadLiveSegmentWithRetryAsync(baseUrl, targetSeq, ct);
                            }
                            segments = await Task.WhenAll(downloadTasks);
                            if (ct.IsCancellationRequested) return 0;
                        }
                    }
                }

                if (segments == null || segments.Length == 0 || segments[0] == null || segments[0].Length == 0) return 0;

                var localFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        file = await localFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                        if (file != null) break;
                    }
                    catch
                    {
                        await Task.Delay(150, ct);
                    }
                }
                if (file == null) return 0;

                using (var stream = await file.OpenStreamForWriteAsync())
                {
                    int downloaded = 0;
                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segBytes = segments[i];
                        if (segBytes == null || segBytes.Length == 0)
                        {
                            if (downloaded == 0) return 0;
                            break;
                        }

                        if (downloaded == 0)
                        {
                            await stream.WriteAsync(segBytes, 0, segBytes.Length, ct);
                        }
                        else
                        {
                            int moofOffset = FindMoofOffset(segBytes);
                            if (moofOffset >= 0 && moofOffset < segBytes.Length)
                            {
                                await stream.WriteAsync(segBytes, moofOffset, segBytes.Length - moofOffset, ct);
                            }
                            else
                            {
                                await stream.WriteAsync(segBytes, 0, segBytes.Length, ct);
                            }
                        }
                        downloaded++;
                        segments[i] = null; // Release segment buffer immediately to prevent memory buildup
                    }
                    await stream.FlushAsync();
                    segments = null;
                    return downloaded;
                }
            }
            catch (OperationCanceledException)
            {
                LogLive("[Live Assemble Hủy] " + fileName);
                return 0;
            }
            catch (Exception ex)
            {
                LogLive("[Live Assemble Lỗi] " + fileName + ": " + ex.Message);
                return 0;
            }
        }

        private void PreBufferNextLiveChunkAsync(int count = LIVE_DEEP_SEGMENTS)
        {
            if (!_isCurrentTrackLive || _nextLiveStartSeq <= 0) return;
            if (_liveCts == null || _liveCts.IsCancellationRequested) return;
            if (_isPreBuffering) return;
            _isPreBuffering = true;

            int nextCycle = _liveBufferCycle + 1;
            int nextIndex = nextCycle % 3;
            string nextFile = "temp_live_buf_" + nextIndex + ".mp4";
            long startSeq = _nextLiveStartSeq;
            var ct = _liveCts.Token;

            LogLive("[Live PreBuf Bắt đầu] buf=" + nextIndex + " seq=" + startSeq + " cnt=" + count);
            _isNextLiveBufferReady = false;
            _nextLiveBufferTask = Task.Run(async () =>
            {
                try
                {
                    string curVidId;
                    lock (_playlistLock)
                    {
                        curVidId = (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count)
                            ? _videoIdList[_currentTrackIndex] : null;
                    }

                    // YouTube enforces a strict 30-second TTL on unauthenticated live BaseURLs.
                    // Refresh BaseURL before prebuffering if current URL is approaching expiry (>= 15s old) or missing.
                    if (string.IsNullOrEmpty(_currentLiveBaseUrl) || 
                        !_liveBaseUrlStopwatch.IsRunning || 
                        _liveBaseUrlStopwatch.Elapsed.TotalSeconds >= 15.0)
                    {
                        if (!string.IsNullOrEmpty(curVidId) && !ct.IsCancellationRequested)
                        {
                            await RefreshLiveBaseUrlAsync(curVidId, ct);
                        }
                    }

                    if (string.IsNullOrEmpty(_currentLiveBaseUrl))
                    {
                        LogLive("[Live PreBuf Lỗi] BaseURL rỗng");
                        _lastPreBufferFailureTime = DateTime.UtcNow;
                        return false;
                    }

                    var dlSw = System.Diagnostics.Stopwatch.StartNew();
                    int downloaded = await AssembleLiveBufferAsync(_currentLiveBaseUrl, startSeq, count, nextFile, ct);
                    dlSw.Stop();
                    if (downloaded > 0 && !ct.IsCancellationRequested)
                    {
                        _nextLiveDurationSec = downloaded * 5.0;
                        _isNextLiveBufferReady = true;
                        _nextLiveStartSeq = startSeq + downloaded;
                        LogLive("[Live PreBuf Xong] buf=" + nextIndex + ": " + downloaded + "/" + count + " in " + dlSw.ElapsedMilliseconds + "ms (" + _nextLiveDurationSec.ToString("F0") + "s) next=" + _nextLiveStartSeq);
                        return true;
                    }
                    _lastPreBufferFailureTime = DateTime.UtcNow;
                    LogLive("[Live PreBuf Lỗi] buf=" + nextIndex + ": " + downloaded + "/" + count + " in " + dlSw.ElapsedMilliseconds + "ms");
                    return false;
                }
                finally
                {
                    _isPreBuffering = false;
                }
            });
        }

        private async void PlayLiveBufferedTrackAsync(string vidId)
        {
            // [TEMP DISABLED FOR SABR TESTING] LiveMediaStreamSource tạm thời vô hiệu hóa để test SABR
            LogLive("[Live MSS] LiveMediaStreamSource tạm thời vô hiệu hóa để test SABR.");
            ReportErrorToUI("LiveMediaStreamSource tạm tắt để test SABR.");
            await Task.Yield();
        }

        private async Task CleanupLiveTempFilesAsync()
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        var f = await localFolder.GetFileAsync("temp_live_buf_" + i + ".mp4");
                        if (f != null) await f.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private bool IsLiveStreamUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf("hls_variant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("hls_playlist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("yt_live_broadcast", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("live/1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("live=1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void PlayUrl(string trackUrl, string vidId)
        {
            lock (_playlistLock)
            {
                if (_currentTrackIndex < 0 || _currentTrackIndex >= _videoIdList.Count) return;
                if (_videoIdList[_currentTrackIndex] != vidId) return;
            }

            _mediaPlayer.AutoPlay = true;
            try
            {
                StopPlaybackMonitor();

                int sqIdx = trackUrl.IndexOf("#sq=");
                if (sqIdx >= 0)
                {
                    string sqStr = trackUrl.Substring(sqIdx + 4);
                    long parsedSq;
                    if (long.TryParse(sqStr, out parsedSq) && parsedSq > 0)
                    {
                        _currentLiveSeq = parsedSq;
                    }
                    trackUrl = trackUrl.Substring(0, sqIdx);
                }

                _isCurrentTrackLive = IsLiveStreamUrl(trackUrl);
                _currentLiveBaseUrl = _isCurrentTrackLive ? trackUrl : null;
                if (_isCurrentTrackLive) try { _liveBaseUrlStopwatch.Restart(); } catch { }
                if (!_isCurrentTrackLive)
                {
                    if (_liveMss != null)
                    {
                        try { _liveMss.Dispose(); } catch { }
                        _liveMss = null;
                    }
                    if (_liveCts != null)
                    {
                        try { _liveCts.Cancel(); _liveCts.Dispose(); } catch { }
                        _liveCts = null;
                    }
                }
                if (_currentLoadedVidId != vidId) _liveReconnectCount = 0;

                if (trackUrl.StartsWith("SABR:", StringComparison.OrdinalIgnoreCase))
                {
                    PlaySabrTrack(trackUrl, vidId);
                    return;
                }

                // [TEMP DISABLED FOR SABR TESTING] LiveMediaStreamSource tạm thời vô hiệu hóa
                /*
                if (_isCurrentTrackLive && !string.IsNullOrEmpty(_currentLiveBaseUrl))
                {
                    LogLive("[Live PlayUrl] vid=" + vidId + " seq=" + _currentLiveSeq + " url=" + trackUrl.Substring(0, Math.Min(40, trackUrl.Length)) + "...");
                    PlayLiveBufferedTrackAsync(vidId);
                    return;
                }
                */

                if (_sabrMss != null)
                {
                    try { _sabrMss.Dispose(); } catch { }
                    _sabrMss = null;
                }

                // Volume preservation / Normalize Volume
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (ls.ContainsKey("UserVolume"))
                {
                    try
                    {
                        double uVol = Convert.ToDouble(ls["UserVolume"]);
                        if (uVol >= 0.0 && uVol <= 1.0) _mediaPlayer.Volume = uVol;
                    }
                    catch { }
                }
                else
                {
                    bool normalize = ls.ContainsKey("NormalizeVolume") ? (bool)ls["NormalizeVolume"] : false;
                    _mediaPlayer.Volume = normalize ? 0.75 : 1.0;
                }

                UpdateSystemMediaControls();
                _mediaPlayer.AutoPlay = !_startPaused;
                _mediaPlayer.SetUriSource(new Uri(trackUrl));
                try { _mediaPlayer.PlaybackRate = _playbackRate; } catch { }
                _currentLoadedVidId = vidId;

                if (_startPaused)
                {
                    _startPaused = false;
                    _mediaPlayer.Pause();
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                }
                else
                {
                    _mediaPlayer.Play();
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                }

                // Start crossfade monitoring (gapless pre-resolve triggers near end of track)
                StartPlaybackMonitor();
            }
            catch (Exception ex)
            {
                ReportErrorToUI("Stream Error: " + ex.Message.Split('\n')[0]);
            }
        }

        private async void PlaySabrTrack(string sabrDescriptor, string vidId)
        {
            int seq = _playbackSequence;
            try
            {
                StopPlaybackMonitor();

                if (_liveMss != null)
                {
                    try { _liveMss.Dispose(); } catch { }
                    _liveMss = null;
                }
                if (_liveCts != null)
                {
                    try { _liveCts.Cancel(); _liveCts.Dispose(); } catch { }
                    _liveCts = null;
                }
                if (_sabrCts != null)
                {
                    try { _sabrCts.Cancel(); _sabrCts.Dispose(); } catch { }
                    _sabrCts = null;
                }
                if (_sabrMss != null)
                {
                    try { _sabrMss.Dispose(); } catch { }
                    _sabrMss = null;
                }

                _sabrCts = new CancellationTokenSource();
                var ct = _sabrCts.Token;

                string payload = sabrDescriptor.Substring(5);
                string[] tokens = payload.Split('|');
                string serverAbrUrl = tokens.Length > 0 ? tokens[0] : null;
                string ustreamerConfigStr = tokens.Length > 1 ? tokens[1] : null;
                string userAgent = tokens.Length > 2 ? tokens[2] : null;
                string clientName = tokens.Length > 3 ? tokens[3] : null;
                string clientVersion = tokens.Length > 4 ? tokens[4] : null;
                string poToken = tokens.Length > 5 ? tokens[5] : null;

                byte[] ustreamerBytes = null;
                if (!string.IsNullOrEmpty(ustreamerConfigStr))
                {
                    try { ustreamerBytes = Convert.FromBase64String(ustreamerConfigStr); }
                    catch { ustreamerBytes = System.Text.Encoding.UTF8.GetBytes(ustreamerConfigStr); }
                }

                _sabrMss = new SabrMediaStreamSource(
                    serverAbrUrl,
                    ustreamerBytes,
                    userAgent,
                    clientName,
                    clientVersion,
                    poToken,
                    LogLive);

                // Preload initial chunk before setting media source to ensure fast start & error detection
                bool preloaded = await _sabrMss.PreloadInitialChunkAsync(ct);
                if (ct.IsCancellationRequested || seq != _playbackSequence)
                {
                    if (_sabrMss != null) { try { _sabrMss.Dispose(); } catch { } _sabrMss = null; }
                    return;
                }

                if (!preloaded)
                {
                    string err = !string.IsNullOrEmpty(_sabrMss.LastError) ? _sabrMss.LastError : "Unknown error";
                    LogLive("[SABR Init Error] " + err);
                    ReportErrorToUI("SABR Error: " + err);
                    if (_sabrMss != null) { try { _sabrMss.Dispose(); } catch { } _sabrMss = null; }
                    return;
                }

                // Volume preservation / Normalize Volume
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (ls.ContainsKey("UserVolume"))
                {
                    try
                    {
                        double uVol = Convert.ToDouble(ls["UserVolume"]);
                        if (uVol >= 0.0 && uVol <= 1.0) _mediaPlayer.Volume = uVol;
                    }
                    catch { }
                }
                else
                {
                    bool normalize = ls.ContainsKey("NormalizeVolume") ? (bool)ls["NormalizeVolume"] : false;
                    _mediaPlayer.Volume = normalize ? 0.75 : 1.0;
                }

                UpdateSystemMediaControls();
                _mediaPlayer.AutoPlay = !_startPaused;
                _mediaPlayer.SetMediaSource(_sabrMss.StreamSource);
                try { _mediaPlayer.PlaybackRate = _playbackRate; } catch { }
                _sabrMss.StartStreaming((float)_playbackRate);
                _currentLoadedVidId = vidId;

                if (_startPaused)
                {
                    _startPaused = false;
                    _mediaPlayer.Pause();
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                }
                else
                {
                    _mediaPlayer.Play();
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                }

                StartPlaybackMonitor();
                LogLive("[SABR Playback] Stream started successfully for " + vidId);
            }
            catch (Exception ex)
            {
                LogLive("[SABR Error] " + ex.Message);
                ReportErrorToUI("SABR playback failed: " + ex.Message);
            }
        }

        // ==========================================
        // RETRY FLOW – InnerTube only
        // Retry 1-2: Lấy URL InnerTube mới (URL cũ hết hạn)
        // Retry 3-4: Dùng URL từ MainPage hoặc resolve lại nếu chưa có
        // ==========================================
        private async void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            if (_isCurrentTrackLive)
            {
                string hr = args.ExtendedErrorCode != null ? args.ExtendedErrorCode.HResult.ToString("X") : "unknown";
                LogLive("[Live Lỗi MediaFailed] 0x" + hr);
                if (_liveMss != null)
                {
                    try { _liveMss.Dispose(); } catch { }
                    _liveMss = null;
                }
            }

            // [FIX-SOF] Guard against re-entrancy – prevents StackOverflowException
            if (_isRetrying) return;
            _isRetrying = true;

            _currentLoadedVidId = "";
            _retryCount++;

            string vidId;
            lock (_playlistLock)
            {
                vidId = (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count)
                    ? _videoIdList[_currentTrackIndex] : "";
            }

            if (string.IsNullOrEmpty(vidId) || vidId.StartsWith("LOCAL:"))
            {
                ResetRetryState();
                ReportErrorToUI("Playback failed");
                return;
            }

            if (_retryCount > MAX_RETRIES)
            {
                ResetRetryState();
                string err = "All sources failed";
                if (args.ExtendedErrorCode != null) err += " (" + args.ExtendedErrorCode.HResult + ")";
                ReportErrorToUI(err);
                return;
            }

            // Retry 1-2: Lấy URL InnerTube MỚI
            if (_retryCount <= 2)
            {
                await Task.Delay(800);
                _cachedVisitorData = null;
                string freshUrl = await ResolveViaInnerTubeDirectAsync(vidId);
                if (!string.IsNullOrEmpty(freshUrl))
                {
                    freshUrl = PrepareStreamUrl(freshUrl);
                    _resolvedUrl = freshUrl;
                    _innerTubeAttempted = true;
                    _isRetrying = false;
                    StartPlaybackAsync();
                    return;
                }
            }

            // Retry 3-4: Dùng URL từ MainPage hoặc resolve lại nếu chưa có
            await Task.Delay(800);
            _resolvedUrl = null;
            bool hasTrackUrl = false;
            lock (_playlistLock)
            {
                if (_trackList != null && _currentTrackIndex >= 0 && _currentTrackIndex < _trackList.Count && !string.IsNullOrEmpty(_trackList[_currentTrackIndex]))
                {
                    hasTrackUrl = true;
                }
            }
            if (hasTrackUrl)
            {
                _innerTubeAttempted = true;
            }
            else
            {
                _innerTubeAttempted = false;
            }
            _isRetrying = false;
            StartPlaybackAsync();
        }

        private void SendToast(string message)
        {
            try { BackgroundMediaPlayer.SendMessageToForeground(new ValueSet { { "ToastMessage", message } }); } catch { }
        }

        private void LogLive(string msg)
        {
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg;
                System.Diagnostics.Debug.WriteLine(line);

                bool shouldFlush = false;
                lock (_logLock)
                {
                    _pendingLiveLogs.Add(line);
                    if (_pendingLiveLogs.Count > 50)
                    {
                        _pendingLiveLogs.RemoveAt(0);
                    }
                    if ((DateTime.UtcNow - _lastLogFlushTime).TotalSeconds >= 4.0 || _pendingLiveLogs.Count >= 10)
                    {
                        shouldFlush = true;
                    }
                }

                if (shouldFlush)
                {
                    FlushLiveLogs();
                }
            }
            catch { }
            // NOTE: Do NOT call SendToast here — sending rapid IPC messages to foreground
            // on every live chunk/event crashes Windows.Media.BackgroundPlayback.exe with 0x800703e9 (STATUS_STACK_OVERFLOW).
        }

        private void FlushLiveLogs()
        {
            try
            {
                string joined;
                lock (_logLock)
                {
                    if (_pendingLiveLogs.Count == 0) return;
                    _lastLogFlushTime = DateTime.UtcNow;
                    var sb = new System.Text.StringBuilder();
                    for (int i = _pendingLiveLogs.Count - 1; i >= 0; i--)
                    {
                        sb.AppendLine(_pendingLiveLogs[i]);
                    }
                    joined = sb.ToString();
                    _pendingLiveLogs.Clear();
                }

                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                string prev = ls.ContainsKey("LiveDebugLog") ? (ls["LiveDebugLog"]?.ToString() ?? "") : "";
                string updated = joined + prev;
                // WinRT LocalSettings has a strict 4096-byte limit per value.
                // Cap to 1500 chars (3000 UTF-16 bytes) to never throw WinRT quota exceptions.
                if (updated.Length > 1500) updated = updated.Substring(0, 1500);
                ls["LiveDebugLog"] = updated;
            }
            catch { }
        }

        private void ReportErrorToUI(string errorDetail)
        {
            FlushLiveLogs();
            string title = "Beatora";
            string thumb = null;
            lock (_playlistLock)
            {
                if (_currentTrackIndex >= 0 && _currentTrackIndex < _titleList.Count) title = _titleList[_currentTrackIndex];
                if (_currentTrackIndex >= 0 && _currentTrackIndex < _thumbnailList.Count) thumb = _thumbnailList[_currentTrackIndex];
            }
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                ls["CurrentTitle"] = title; ls["CurrentArtist"] = errorDetail;
                var msg = new ValueSet { { "TrackChanged", "" }, { "NewTitle", title }, { "NewArtist", errorDetail } };
                if (!string.IsNullOrEmpty(thumb)) msg.Add("NewThumbnail", thumb);
                BackgroundMediaPlayer.SendMessageToForeground(msg);
            }
            catch { }
            try { _systemControls.DisplayUpdater.MusicProperties.Title = title; _systemControls.DisplayUpdater.MusicProperties.Artist = errorDetail; _systemControls.DisplayUpdater.Update(); } catch { }
        }

        private void UpdateSystemMediaControls()
        {
            string title, artist, thumb, vidId;
            lock (_playlistLock)
            {
                if (_currentTrackIndex < 0 || 
                    _currentTrackIndex >= _titleList.Count ||
                    _currentTrackIndex >= _artistList.Count ||
                    _currentTrackIndex >= _thumbnailList.Count ||
                    _currentTrackIndex >= _videoIdList.Count) return;
                title = _titleList[_currentTrackIndex];
                artist = _artistList[_currentTrackIndex];
                thumb = _thumbnailList[_currentTrackIndex];
                vidId = _videoIdList[_currentTrackIndex];
            }

            try { _systemControls.DisplayUpdater.Type = MediaPlaybackType.Music; _systemControls.DisplayUpdater.MusicProperties.Title = title; _systemControls.DisplayUpdater.MusicProperties.Artist = artist; _systemControls.DisplayUpdater.Update(); } catch { }
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                string storedTitle = ls.ContainsKey("CurrentTitle") ? ls["CurrentTitle"]?.ToString() : null;
                string storedArtist = ls.ContainsKey("CurrentArtist") ? ls["CurrentArtist"]?.ToString() : null;
                string storedVid = ls.ContainsKey("CurrentVideoId") ? ls["CurrentVideoId"]?.ToString() : null;
                string storedThumb = ls.ContainsKey("CurrentThumbnail") ? ls["CurrentThumbnail"]?.ToString() : null;

                if (storedTitle != title) ls["CurrentTitle"] = title;
                if (storedArtist != artist) ls["CurrentArtist"] = artist;
                if (storedVid != vidId) ls["CurrentVideoId"] = vidId;
                if (storedThumb != thumb) ls["CurrentThumbnail"] = thumb;

                // Add to PendingHistory for SQLite insertion by foreground
                try
                {
                    string historyTrackStr = $"{vidId}|{title.Replace("|", "").Replace("^", "")}|{artist.Replace("|", "").Replace("^", "")}|{thumb}";
                    string pending = ls.ContainsKey("PendingHistory") ? ls["PendingHistory"]?.ToString() : "";
                    if (pending.Length > 2000) pending = ""; // Protect settings quota
                    if (string.IsNullOrEmpty(pending))
                        ls["PendingHistory"] = historyTrackStr;
                    else
                        ls["PendingHistory"] = pending + "^^^" + historyTrackStr;
                }
                catch { }

                // Live Tile & Badge update from Background (matching TileService.cs)
                try
                {
                    bool isLiveTileEnabled = ls.ContainsKey("EnableLiveTile") ? (bool)ls["EnableLiveTile"] : (!ls.ContainsKey("LiveTileEnabled") || (bool)ls["LiveTileEnabled"]);
                    int liveTileMode = ls.ContainsKey("LiveTileMode") ? Convert.ToInt32(ls["LiveTileMode"]) : 0;

                    if (isLiveTileEnabled && liveTileMode != 2 && !string.IsNullOrEmpty(thumb))
                    {
                        var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                        bool isDynamic = (liveTileMode == 0);
                        updater.EnableNotificationQueue(isDynamic);

                        string squareThumb = thumb;
                        if (thumb.Contains("=w") || thumb.Contains("=s"))
                        {
                            int wIdx = thumb.LastIndexOf("=w");
                            if (wIdx > 0) squareThumb = thumb.Substring(0, wIdx) + "=w500-h500-s-no";
                            else
                            {
                                int sIdx = thumb.LastIndexOf("=s");
                                if (sIdx > 0) squareThumb = thumb.Substring(0, sIdx) + "=w500-h500-s-no";
                            }
                        }

                        string safeThumb = System.Net.WebUtility.HtmlEncode(squareThumb);
                        string safeTitle = System.Net.WebUtility.HtmlEncode(title ?? "");
                        string safeArtist = System.Net.WebUtility.HtmlEncode(artist ?? "");

                        string xml = string.Format(
                            "<tile><visual version=\"2\">" +
                            "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                            "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text></binding>" +
                            "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text><text id=\"2\">{2}</text></binding>" +
                            "<binding template=\"TileSquare310x310ImageAndText01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text><text id=\"2\">{2}</text></binding>" +
                            "</visual></tile>", safeThumb, safeTitle, safeArtist);

                        var doc = new XmlDocument();
                        doc.LoadXml(xml);
                        var notif = new TileNotification(doc)
                        {
                            Tag = "nowplaying",
                            ExpirationTime = DateTimeOffset.UtcNow.AddHours(12)
                        };
                        updater.Update(notif);

                        try
                        {
                            var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
                            var badgeEl = badgeXml.SelectSingleNode("/badge") as XmlElement;
                            badgeEl?.SetAttribute("value", "playing");
                            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(new BadgeNotification(badgeXml));
                        }
                        catch { }
                    }
                }
                catch { }
            }
            catch { }
            try { BackgroundMediaPlayer.SendMessageToForeground(new ValueSet { { "TrackChanged", "" }, { "NewTitle", title }, { "NewArtist", artist }, { "NewVideoId", vidId }, { "NewThumbnail", thumb } }); } catch { }
        }

        // ─── Playback Monitor (Gapless & SponsorBlock) ───
        private Windows.System.Threading.ThreadPoolTimer _playbackMonitorTimer;
        private string _preResolvedNextUrl = null;
        private List<YTMusicWP.Models.SponsorBlockSegment> _skipSegments = new List<YTMusicWP.Models.SponsorBlockSegment>();
        
        private int _preResolvedNextIndex = -1;
        private string _preResolvedNextVideoId = null;
        private int _playbackSequence = 0;

        private void ClearPreResolvedState()
        {
            _preResolvedNextUrl = null;
            _preResolvedNextIndex = -1;
            _preResolvedNextVideoId = null;
            _isPreResolving = false;
        }

        private void StartPlaybackMonitor()
        {
            StopPlaybackMonitor();
            // High-precision 100ms polling for live stream handover; 500ms for regular tracks
            int intervalMs = _isCurrentTrackLive ? 100 : 500;
            _playbackMonitorTimer = Windows.System.Threading.ThreadPoolTimer.CreatePeriodicTimer(
                PlaybackMonitorTimer_Tick, TimeSpan.FromMilliseconds(intervalMs));
        }

        private void StopPlaybackMonitor()
        {
            if (_playbackMonitorTimer != null)
            {
                _playbackMonitorTimer.Cancel();
                _playbackMonitorTimer = null;
            }
        }

        private void PlaybackMonitorTimer_Tick(Windows.System.Threading.ThreadPoolTimer timer)
        {
            try
            {
                if (_mediaPlayer == null || _trackList.Count == 0) return;

                // Sleep Timer Check (functions even when phone is locked or screen is off)
                if (DateTime.UtcNow >= _sleepTimerExpiry)
                {
                    _sleepTimerExpiry = DateTime.MaxValue;
                    _mediaPlayer.Pause();
                    SendToast("Sleep Timer: Music paused.");
                    return;
                }

                if (_isCurrentTrackLive)
                {
                    if (_liveMss != null || _sabrMss != null)
                    {
                        // LiveMediaStreamSource / SabrMediaStreamSource streams continuously in RAM without any buffer swaps!
                        return;
                    }

                    double elapsed = _liveBufferStopwatch.Elapsed.TotalSeconds;
                    double duration = _liveBufferDurationSec;
                    try
                    {
                        if (_mediaPlayer.NaturalDuration > TimeSpan.Zero)
                        {
                            duration = _mediaPlayer.NaturalDuration.TotalSeconds;
                            _liveBufferDurationSec = duration;
                        }
                    }
                    catch { }

                    // Hardware AAC stream terminates ~0.55s before nominal MP4 duration due to
                    // 215-frame chunk quantization (4.992s per chunk) and decoder priming sample trimming.
                    // The actual audio ends at ~(_liveBufferDurationSec - 0.55s).
                    // Triggering swap when elapsed reaches (_liveBufferDurationSec - 0.75s) ensures we begin
                    // loading the next buffer ~200-250ms before audio runs out, perfectly matching the ~270ms
                    // MediaFoundation pipeline switch latency and completely eliminating the pause/stutter!
                    bool nearEnd = (_isNextLiveBufferReady && elapsed >= 3.0 && duration > 2.0 && elapsed >= (duration - 0.75));
                    bool finishedBuffer = (elapsed >= 3.0 && (_mediaPlayer.CurrentState == MediaPlayerState.Paused || _mediaPlayer.CurrentState == MediaPlayerState.Stopped));

                    // Auto-kickstart if player got stuck in Paused right after buffer swap (< 3s)
                    if (_mediaPlayer.CurrentState == MediaPlayerState.Paused && elapsed < 3.0 && !_isLiveSwapping && !_isLiveInitializing)
                    {
                        try { _mediaPlayer.Play(); } catch { }
                    }

                    if ((nearEnd || finishedBuffer) && !_isLiveSwapping && !_isLiveInitializing && (DateTime.UtcNow - _lastLiveSwapTime).TotalSeconds >= 2.5)
                    {
                        LogLive("[Live Đổi Buffer] -> buf=" + ((_liveBufferCycle + 1) % 3) + 
                            " (nearEnd=" + nearEnd + " fin=" + finishedBuffer + " ela=" + elapsed.ToString("F1") + "s/" + _liveBufferDurationSec.ToString("F1") + "s state=" + _mediaPlayer.CurrentState + " ready=" + _isNextLiveBufferReady + ")");
                        SwapToNextLiveBuffer();
                    }
                    else if (!_isNextLiveBufferReady && 
                             !_isPreBuffering && 
                             !_isLiveSwapping && 
                             !_isLiveInitializing &&
                             (_nextLiveBufferTask == null || _nextLiveBufferTask.IsCompleted) && 
                              (DateTime.UtcNow - _lastPreBufferFailureTime).TotalSeconds >= 4.0 &&
                              elapsed >= Math.Max(4.0, _liveBufferDurationSec - 14.0))
                    {
                        // Paced pre-buffering: trigger ~14s before current buffer ends.
                        // Allows YouTube to generate upcoming chunks so we can stay close to live edge (~50s delay)
                        // while maintaining a generous ~14s download runway.
                        PreBufferNextLiveChunkAsync(LIVE_DEEP_SEGMENTS);
                    }
                    return;
                }

                if (_mediaPlayer.CurrentState != MediaPlayerState.Playing) return;

                var pos = _mediaPlayer.Position;
                var naturalDuration = _mediaPlayer.NaturalDuration;
                if (naturalDuration == TimeSpan.Zero || naturalDuration.TotalSeconds < 10) return;

                double remaining = (naturalDuration - pos).TotalSeconds;

                // SponsorBlock Auto-Skip
                if (_skipSegments != null && _skipSegments.Count > 0)
                {
                    foreach (var seg in _skipSegments)
                    {
                        if (pos.TotalSeconds >= seg.Start && pos.TotalSeconds < seg.End - 1) // -1s buffer
                        {
                            _mediaPlayer.Position = TimeSpan.FromSeconds(seg.End);
                            SendToast("Skipped " + (seg.Category ?? "segment"));
                            break;
                        }
                    }
                }

                // Gapless: Pre-resolve next track URL 15s before end (only if playlist has > 1 track)
                bool hasMultipleTracks;
                lock (_playlistLock)
                {
                    hasMultipleTracks = _trackList.Count > 1;
                }
                if (hasMultipleTracks && remaining <= 15 && remaining > 10 && string.IsNullOrEmpty(_preResolvedNextUrl) && !_isPreResolving)
                {
                    PreResolveNextTrack();
                }
            }
            catch { }
        }

        private bool _isPreResolving = false;

        private async void PreResolveNextTrack()
        {
            if (_isPreResolving) return;
            if (!string.IsNullOrEmpty(_preResolvedNextUrl)) return;
            _isPreResolving = true;
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                bool gapless = ls.ContainsKey("GaplessPlayback") ? (bool)ls["GaplessPlayback"] : true;
                if (!gapless) return;

                int nextIdx;
                string nextVidId;
                lock (_playlistLock)
                {
                    if (_trackList.Count <= 1) return;

                    bool shuffle = ls.ContainsKey("ShuffleMode") ? (bool)ls["ShuffleMode"] : false;
                    int repeat = ls.ContainsKey("RepeatMode") ? (int)ls["RepeatMode"] : 0;
                    bool autoplay = ls.ContainsKey("Autoplay") ? (bool)ls["Autoplay"] : true;

                    if (repeat == 2) nextIdx = _currentTrackIndex;
                    else if (shuffle) nextIdx = _rand.Next(0, _trackList.Count);
                    else
                    {
                        nextIdx = _currentTrackIndex + 1;
                        if (nextIdx >= _trackList.Count)
                        {
                            if (repeat == 1 || autoplay) nextIdx = 0;
                            else return;
                        }
                    }

                    if (nextIdx < 0 || nextIdx >= _videoIdList.Count) return;
                    nextVidId = _videoIdList[nextIdx];
                    if (nextVidId.StartsWith("LOCAL:"))
                    {
                        _preResolvedNextUrl = _trackList[nextIdx];
                        _preResolvedNextIndex = nextIdx;
                        _preResolvedNextVideoId = nextVidId;
                        return;
                    }
                }

                string url = await ResolveViaInnerTubeDirectAsync(nextVidId);
                // Strict check: verify target index and videoId still match after async call
                lock (_playlistLock)
                {
                    if (!string.IsNullOrEmpty(url) && nextIdx < _videoIdList.Count && _videoIdList[nextIdx] == nextVidId)
                    {
                        _preResolvedNextUrl = PrepareStreamUrl(url);
                        _preResolvedNextIndex = nextIdx;
                        _preResolvedNextVideoId = nextVidId;
                    }
                }
            }
            catch { }
            finally
            {
                _isPreResolving = false;
            }
        }

        private void MoveNext()
        {
            int targetIdx;
            string preUrl;
            int preIdx;
            string preVid;

            lock (_playlistLock)
            {
                if (_trackList.Count == 0) return;
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                bool shuffle = ls.ContainsKey("ShuffleMode") ? (bool)ls["ShuffleMode"] : false;
                int repeat = ls.ContainsKey("RepeatMode") ? (int)ls["RepeatMode"] : 0;
                bool autoplay = ls.ContainsKey("Autoplay") ? (bool)ls["Autoplay"] : true;
                if (repeat == 2) { ResetRetryState(); _currentLoadedVidId = ""; StartPlaybackAsync(); return; }
                ResetRetryState();

                // Calculate expected next index
                if (shuffle)
                {
                    targetIdx = _rand.Next(0, _trackList.Count);
                }
                else
                {
                    targetIdx = _currentTrackIndex + 1;
                    if (targetIdx >= _trackList.Count)
                    {
                        if (repeat == 1 || autoplay)
                        {
                            targetIdx = 0;
                        }
                        else
                        {
                            _currentTrackIndex = _trackList.Count - 1;
                            return; // Stop playback when queue ends and autoplay is off
                        }
                    }
                }

                preUrl = _preResolvedNextUrl;
                preIdx = _preResolvedNextIndex;
                preVid = _preResolvedNextVideoId;
                ClearPreResolvedState();

                _currentTrackIndex = targetIdx;

                // Only use pre-resolved URL if it matches targetIdx and targetVidId EXACTLY
                if (preIdx == targetIdx && !string.IsNullOrEmpty(preUrl) && targetIdx < _videoIdList.Count && preVid == _videoIdList[targetIdx])
                {
                    _trackList[_currentTrackIndex] = preUrl;
                    _innerTubeAttempted = true;
                }
                else
                {
                    _innerTubeAttempted = false;
                }
            }

            StartPlaybackAsync();
        }

        private void MovePrevious()
        {
            lock (_playlistLock)
            {
                if (_trackList.Count == 0) return;
                try
                {
                    if (_mediaPlayer != null && _mediaPlayer.CurrentState != MediaPlayerState.Closed && _mediaPlayer.Position.TotalSeconds > 3)
                    {
                        _mediaPlayer.Position = TimeSpan.Zero;
                        _mediaPlayer.Play();
                        return;
                    }
                }
                catch { }
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                bool shuffle = ls.ContainsKey("ShuffleMode") ? (bool)ls["ShuffleMode"] : false;
                int repeat = ls.ContainsKey("RepeatMode") ? (int)ls["RepeatMode"] : 0;
                if (repeat == 2) { ResetRetryState(); StartPlaybackAsync(); return; }
                ResetRetryState();
                ClearPreResolvedState();
                if (shuffle) _currentTrackIndex = _rand.Next(0, _trackList.Count);
                else { _currentTrackIndex--; if (_currentTrackIndex < 0) { if (repeat == 1) _currentTrackIndex = _trackList.Count - 1; else { _currentTrackIndex = 0; return; } } }
            }
            StartPlaybackAsync();
        }

        private async void SwapToNextLiveBuffer()
        {
            if (!_isCurrentTrackLive || _isLiveSwapping) return;
            // Prevent double-swap within 2.5 seconds (prevents race between timer and MediaEnded/StateChanged)
            if ((DateTime.UtcNow - _lastLiveSwapTime).TotalSeconds < 2.5) return;
            _isLiveSwapping = true;
            _lastLiveSwapTime = DateTime.UtcNow;

            try
            {
                int nextCycle = _liveBufferCycle + 1;
                int nextIndex = nextCycle % 3;
                string nextFile = "temp_live_buf_" + nextIndex + ".mp4";
                LogLive("[Live Swap Bắt đầu] -> buf=" + nextIndex + ", ready=" + _isNextLiveBufferReady + ", elapsed=" + _liveBufferStopwatch.Elapsed.TotalSeconds.ToString("F1") + "s");

                // If next buffer is not yet ready, wait briefly for current download task
                if (!_isNextLiveBufferReady)
                {
                    if (_nextLiveBufferTask == null || _nextLiveBufferTask.IsCompleted)
                    {
                        LogLive("[Live Swap] Task rỗng hoặc đã xong, gọi PreBuffer...");
                        PreBufferNextLiveChunkAsync(LIVE_DEEP_SEGMENTS);
                    }
                    if (_nextLiveBufferTask != null)
                    {
                        LogLive("[Live Swap] Đang chờ task tải xong (tối đa 8s)...");
                        try
                        {
                            await Task.WhenAny(_nextLiveBufferTask, Task.Delay(8000));
                        }
                        catch { }
                        LogLive("[Live Swap] Chờ xong, ready=" + _isNextLiveBufferReady);
                    }
                }

                if (_isNextLiveBufferReady)
                {
                    _liveBufferCycle = nextCycle;
                    _currentLiveBufferIndex = nextIndex;
                    _isNextLiveBufferReady = false;
                    _liveBufferDurationSec = _nextLiveDurationSec > 0 ? _nextLiveDurationSec : (LIVE_DEEP_SEGMENTS * 5.0);

                    for (int setAttempt = 0; setAttempt < 3; setAttempt++)
                    {
                        try
                        {
                            _mediaPlayer.AutoPlay = true;
                            string nextUri = "ms-appdata:///local/" + nextFile;
                            LogLive("[Live Swap Đặt nguồn] " + nextUri + " (lần " + setAttempt + ")");
                            _mediaPlayer.SetUriSource(new Uri(nextUri));
                            try { _mediaPlayer.PlaybackRate = _playbackRate; } catch { }
                            _mediaPlayer.Play();
                            LogLive("[Live Swap Thành công] buf=" + nextIndex + " (" + _liveBufferDurationSec.ToString("F0") + "s)");
                            _liveReconnectCount = 0;
                            return;
                        }
                        catch (Exception ex)
                        {
                            LogLive("[Live Lỗi Swap] lần " + setAttempt + ": " + ex.Message);
                            await Task.Delay(150);
                        }
                    }
                }

                // If buffer swap failed, reconnect cleanly via fresh buffer
                LogLive("[Live Swap Thất bại] Reconnect lần " + _liveReconnectCount + "/5");
                StopPlaybackMonitor();
                if (_liveReconnectCount < 5)
                {
                    _liveReconnectCount++;
                    string liveVid = null;
                    lock (_playlistLock)
                    {
                        if (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count)
                        {
                            liveVid = _videoIdList[_currentTrackIndex];
                        }
                    }
                    if (!string.IsNullOrEmpty(liveVid))
                    {
                        PlayLiveBufferedTrackAsync(liveVid);
                    }
                    else
                    {
                        StartPlaybackAsync();
                    }
                }
                else
                {
                    _liveReconnectCount = 0;
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                }
            }
            finally
            {
                _isLiveSwapping = false;
            }
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            if (_isCurrentTrackLive && _liveMss != null)
            {
                LogLive("[Live MSS MediaEnded] Livestream completed");
                try { _liveMss.Dispose(); } catch { }
                _liveMss = null;
                StopPlaybackMonitor();
                MoveNext();
                return;
            }

            if (_sabrMss != null)
            {
                LogLive("[SABR MediaEnded] Stream completed");
                try { _sabrMss.Dispose(); } catch { }
                _sabrMss = null;
                StopPlaybackMonitor();
                MoveNext();
                return;
            }

            if (_isCurrentTrackLive || (sender != null && sender.NaturalDuration == TimeSpan.Zero))
            {
                if (_isCurrentTrackLive)
                {
                    LogLive("[Live MediaEnded] elapsed=" + _liveBufferStopwatch.Elapsed.TotalSeconds.ToString("F1") + "s/" + _liveBufferDurationSec.ToString("F0") + "s");
                    if ((DateTime.UtcNow - _lastLiveSwapTime).TotalSeconds >= 2.5)
                    {
                        SwapToNextLiveBuffer();
                    }
                }
                else
                {
                    SwapToNextLiveBuffer();
                }
                return;
            }

            StopPlaybackMonitor();
            MoveNext();
        }

        private void MediaPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            try
            {
                if (_isCurrentTrackLive)
                {
                    LogLive("[Live State] " + sender.CurrentState + " (elapsed=" + _liveBufferStopwatch.Elapsed.TotalSeconds.ToString("F1") + "s/" + _liveBufferDurationSec.ToString("F0") + "s)");
                }

                if (sender.CurrentState == MediaPlayerState.Playing)
                {
                    _isUserPaused = false;
                    if (_isCurrentTrackLive)
                    {
                        try { _liveBufferStopwatch.Start(); } catch { }
                    }

                    // [FIX-SOF] Only reset retryCount if NOT in a retry cycle
                    // Without this guard, player briefly entering Playing before failing
                    // would reset _retryCount → infinite retry → StackOverflow
                    if (!_isRetrying) _retryCount = 0;
                    _isRetrying = false;
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    try
                    {
                        var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
                        ((XmlElement)badgeXml.SelectSingleNode("/badge")).SetAttribute("value", "playing");
                        BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(new BadgeNotification(badgeXml));
                    }
                    catch { }
                }
                else if (sender.CurrentState == MediaPlayerState.Paused)
                {
                    if (_isCurrentTrackLive)
                    {
                        try { _liveBufferStopwatch.Stop(); } catch { }

                        // Immediate swap ONLY for legacy file-swap fallback (when _liveMss == null) and if NOT user-paused
                        if (_liveMss == null && !_isUserPaused)
                        {
                            double elapsed = _liveBufferStopwatch.Elapsed.TotalSeconds;
                            // Immediate swap when player pauses near end of buffer! Eliminates 250ms waiting for MediaEnded!
                            if (_liveBufferDurationSec > 1.0 && elapsed >= (_liveBufferDurationSec - 1.5) &&
                                !_isLiveSwapping && !_isLiveInitializing && (DateTime.UtcNow - _lastLiveSwapTime).TotalSeconds >= 2.5)
                            {
                                LogLive("[Live State Paused Swap] elapsed=" + elapsed.ToString("F1") + "s/" + _liveBufferDurationSec.ToString("F1") + "s");
                                SwapToNextLiveBuffer();
                                return;
                            }
                        }
                    }

                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                    try
                    {
                        var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
                        ((XmlElement)badgeXml.SelectSingleNode("/badge")).SetAttribute("value", "paused");
                        BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(new BadgeNotification(badgeXml));
                    }
                    catch { }
                }
                else if (sender.CurrentState == MediaPlayerState.Closed || sender.CurrentState == MediaPlayerState.Stopped)
                {
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Closed;
                    try
                    {
                        BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
        {
            try
            {
                if (sender.AutoPlay && sender.CurrentState != MediaPlayerState.Playing)
                {
                    sender.Play();
                }

                if (_isCurrentTrackLive)
                {
                    _liveBufferStopwatch.Restart();
                    LogLive("[Live MediaOpened] Đang phát Buffer " + _currentLiveBufferIndex + " (" + _liveBufferDurationSec.ToString("F1") + "s, state=" + sender.CurrentState + ")");
                }
            }
            catch { }
        }

        private void SystemControls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play: _isUserPaused = false; try { if (_mediaPlayer.CurrentState == MediaPlayerState.Closed) StartPlaybackAsync(); else _mediaPlayer.Play(); } catch { StartPlaybackAsync(); } break;
                case SystemMediaTransportControlsButton.Pause: _isUserPaused = true; try { _mediaPlayer.Pause(); } catch { } break;
                case SystemMediaTransportControlsButton.Next: MoveNext(); break;
                case SystemMediaTransportControlsButton.Previous: MovePrevious(); break;
            }
        }
        private static string FormatSquareThumbnail(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (url.Contains("googleusercontent.com") || url.Contains("ggpht.com"))
            {
                int eqIdx = url.LastIndexOf("=");
                if (eqIdx > 0)
                    return url.Substring(0, eqIdx) + "=w480-h480-l90-rj";
                return url + "=w480-h480-l90-rj";
            }
            if (url.Contains("hqdefault.jpg"))
                return url.Replace("hqdefault.jpg", "mqdefault.jpg");
            if (url.Contains("sddefault.jpg"))
                return url.Replace("sddefault.jpg", "mqdefault.jpg");
            return url;
        }
    }
}
