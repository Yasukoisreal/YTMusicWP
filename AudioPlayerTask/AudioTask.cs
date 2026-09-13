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
        private bool _isCurrentTrackLive = false;
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
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            _mediaPlayer.CurrentStateChanged += MediaPlayer_CurrentStateChanged;

            BackgroundMediaPlayer.MessageReceivedFromForeground += BackgroundMediaPlayer_MessageReceivedFromForeground;
            taskInstance.Canceled += TaskInstance_Canceled;
        }

        private void TaskInstance_Canceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            try
            {
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
                CleanupLiveTempFiles();
                _httpClient?.Dispose();
            }
            catch { }
            if (_deferral != null) _deferral.Complete();
        }

        private void BackgroundMediaPlayer_MessageReceivedFromForeground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            if (e.Data.ContainsKey("UpdatePlaylist"))
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

                bool hasFastUrl = _innerTubeAttempted; // set true bởi FastUrl ở trên
                ResetRetryState();
                if (hasFastUrl) _innerTubeAttempted = true; // giữ lại → skip double-resolve
                _currentLoadedVidId = "";
                StartPlaybackAsync();
            }
            else if (e.Data.ContainsKey("UpdateQueueOnly"))
            {
                _trackList = new List<string>((string[])e.Data["Urls"]);
                _titleList = new List<string>((string[])e.Data["Titles"]);
                _artistList = new List<string>((string[])e.Data["Artists"]);
                _videoIdList = new List<string>((string[])e.Data["VideoIds"]);
                _thumbnailList = new List<string>((string[])e.Data["Thumbnails"]);
                if (e.Data.ContainsKey("CurrentIndex"))
                {
                    _currentTrackIndex = (int)e.Data["CurrentIndex"];
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
                CleanupLiveTempFiles();
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
                var request = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Get,
                    new Uri("https://www.youtube.com/sw.js_data"));
                request.Headers.TryAppendWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Linux; Andr0id 9; BRAVIA 8K UR2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.125 Safari/537.36 OPR/46.0.2207.0 OMI/4.21.0.273.DIA6.149 Model/Sony-BRAVIA-8K-UR2,gzip(gfe)");
                request.Headers.Add("Accept", "application/json");

                string result;
                using (var response = await _httpClient.SendRequestAsync(request))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    result = await response.Content.ReadAsStringAsync();
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
                    var content = new Windows.Web.Http.HttpStringContent(body, Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
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
                if (usePoToken)
                {
                    var tokenInfo = await FetchRemotePoTokenAsync(videoId, clientName);
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

                var content = new Windows.Web.Http.HttpStringContent(
                    requestBody,
                    Windows.Storage.Streams.UnicodeEncoding.Utf8,
                    "application/json"
                );

                string key = !string.IsNullOrEmpty(apiKey) ? apiKey : (clientName == "IOS" || clientName == "VISIONOS" ? "AIzaSyB-63vPrdThhKuerbB2N_l7Kwwcxj6yUAc" : "AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI");
                // [FIX] Use per-request headers instead of DefaultRequestHeaders to avoid race condition
                var request = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Post,
                    new Uri("https://www.youtube.com/youtubei/v1/player?key=" + key + "&prettyPrint=false&fields=playabilityStatus,streamingData"));
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

                string json;
                using (var response = await _httpClient.SendRequestAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _innerTubeDebug += " [" + clientName + (usePoToken ? "+po" : "") + ":H" + (int)response.StatusCode + "]";
                        return null;
                    }
                    json = await response.Content.ReadAsStringAsync();
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

                        // 3. Fallback cho Live stream: trích xuất direct audio BaseURL từ dashManifestUrl (MP4 AAC itag 140/139) bằng streaming reader siêu nhẹ (~170KB, 0 bytes trên Large Object Heap)
                        if (streamingData.ContainsKey("dashManifestUrl"))
                        {
                            string dashUrl = streamingData.GetNamedString("dashManifestUrl");
                            if (!string.IsNullOrEmpty(dashUrl))
                            {
                                try
                                {
                                    var dashReq = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Get, new Uri(dashUrl));
                                    dashReq.Headers.TryAppendWithoutValidation("User-Agent", userAgent);
                                    using (var dashResp = await _httpClient.SendRequestAsync(dashReq, Windows.Web.Http.HttpCompletionOption.ResponseHeadersRead))
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
                                catch (Exception ex)
                                {
                                    _innerTubeDebug += " [DASH_EX:" + ex.Message.Substring(0, Math.Min(15, ex.Message.Length)) + "]";
                                }
                            }
                        }
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
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _trackList.Count) return;

            int currentSeq = ++_playbackSequence;
            string vidId = _videoIdList[_currentTrackIndex];

            // Offline track: phát trực tiếp (nếu bài cũ vẫn mở thì tua về 0)
            if (vidId.StartsWith("LOCAL:"))
            {
                if (vidId == _currentLoadedVidId && _mediaPlayer.CurrentState != MediaPlayerState.Closed && _retryCount == 0)
                {
                    try { _mediaPlayer.Position = TimeSpan.Zero; _mediaPlayer.Play(); _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing; UpdateSystemMediaControls(); }
                    catch { }
                    return;
                }
                PlayUrl(_trackList[_currentTrackIndex], vidId);
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
                        var segs = await YTMusicWP.Services.SponsorBlockApi.GetSkipSegmentsAsync(vidId);
                        if (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count && _videoIdList[_currentTrackIndex] == vidId) {
                            _skipSegments = segs;
                        }
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
                    _trackList[_currentTrackIndex] = directUrl;
                    PlayUrl(directUrl, vidId);
                    return;
                }
            }

            // FALLBACK: URL từ MainPage nếu có sẵn (chỉ resolve nếu chưa thử)
            string fallbackUrl = _trackList[_currentTrackIndex];
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
                using (var inputStream = await resp.Content.ReadAsInputStreamAsync())
                using (var stream = inputStream.AsStreamForRead())
                {
                    byte[] buf = new byte[8192];
                    var sb = new System.Text.StringBuilder();
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        string chunk = System.Text.Encoding.UTF8.GetString(buf, 0, bytesRead);
                        sb.Append(chunk);
                        string full = sb.ToString();

                        int idx = full.IndexOf("id=\"140\"", StringComparison.OrdinalIgnoreCase);
                        if (idx < 0) idx = full.IndexOf("id=\"139\"", StringComparison.OrdinalIgnoreCase);

                        if (idx >= 0)
                        {
                            int bStart = full.IndexOf("<BaseURL", idx, StringComparison.OrdinalIgnoreCase);
                            if (bStart >= 0)
                            {
                                int bContentStart = full.IndexOf('>', bStart);
                                if (bContentStart >= 0)
                                {
                                    bContentStart++;
                                    int bEnd = full.IndexOf("</BaseURL>", bContentStart, StringComparison.OrdinalIgnoreCase);
                                    if (bEnd > bContentStart)
                                    {
                                        string url = full.Substring(bContentStart, bEnd - bContentStart).Trim();
                                        return url.Replace("&amp;", "&");
                                    }
                                }
                            }
                        }

                        if (sb.Length > 32768)
                        {
                            int keepIdx = full.LastIndexOf("id=\"140\"", StringComparison.OrdinalIgnoreCase);
                            if (keepIdx < 0) keepIdx = full.LastIndexOf("id=\"139\"", StringComparison.OrdinalIgnoreCase);
                            if (keepIdx > 0 && keepIdx < sb.Length)
                            {
                                sb.Remove(0, keepIdx);
                            }
                            else if (keepIdx < 0)
                            {
                                sb.Remove(0, 16384);
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
                    LogLive("[Live Refresh URL Xong] URL mới seq=" + _currentLiveSeq);
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
                var headReq = System.Net.WebRequest.CreateHttp(baseUrl);
                headReq.Method = "HEAD";
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(3000);
                    using (timeoutCts.Token.Register(() => { try { headReq.Abort(); } catch { } }))
                    using (var headResp = (System.Net.HttpWebResponse)await headReq.GetResponseAsync())
                    {
                        string seqHeader = headResp.Headers["X-Head-Seqnum"] ?? headResp.Headers["X-Sequence-Num"];
                        long parsedSeq;
                        if (!string.IsNullOrEmpty(seqHeader) && long.TryParse(seqHeader, out parsedSeq))
                        {
                            return parsedSeq;
                        }
                    }
                }
            }
            catch (System.Net.WebException wex)
            {
                var resp = wex.Response as System.Net.HttpWebResponse;
                if (resp != null && resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    _currentLiveBaseUrl = null; // BaseURL expired (30s TTL)
                }
            }
            catch { }
            return -1;
        }

        private async Task<byte[]> DownloadLiveSegmentAsync(string baseUrl, long seq, CancellationToken ct)
        {
            string segUrl = baseUrl + (baseUrl.EndsWith("/") ? "" : "/") + "sq/" + seq;

            // Try HttpWebRequest first (fastest .NET stream on WP8.1)
            try
            {
                var req = System.Net.WebRequest.CreateHttp(segUrl);
                req.Method = "GET";
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(4000);
                    using (timeoutCts.Token.Register(() => { try { req.Abort(); } catch { } }))
                    using (var resp = (System.Net.HttpWebResponse)await req.GetResponseAsync())
                    {
                        if (resp.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            using (var respStream = resp.GetResponseStream())
                            using (var ms = new MemoryStream())
                            {
                                await respStream.CopyToAsync(ms);
                                return ms.ToArray();
                            }
                        }
                    }
                }
            }
            catch (System.Net.WebException wex)
            {
                var resp = wex.Response as System.Net.HttpWebResponse;
                int code = resp != null ? (int)resp.StatusCode : -1;
                if (!ct.IsCancellationRequested && code != 200)
                {
                    LogLive("[Live Seg " + seq + " WebEx] code=" + code + " " + wex.Message);
                }
                if (code == 403)
                {
                    // 30s TTL expired: immediately invalidate BaseURL and abort this segment so caller can refresh URL
                    _currentLiveBaseUrl = null;
                    return null;
                }
            }
            catch { }

            // Fallback via WinRT HttpClient
            try
            {
                var request = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Get, new Uri(segUrl));
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(4000);
                    using (var response = await _httpClient.SendRequestAsync(request).AsTask(timeoutCts.Token))
                    {
                        if (response.IsSuccessStatusCode)
                        {
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
            catch { }

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
                    string curVid = (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count)
                        ? _videoIdList[_currentTrackIndex] : null;
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
                    string curVidId = (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count)
                        ? _videoIdList[_currentTrackIndex] : null;

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
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _videoIdList.Count) return;
            if (_videoIdList[_currentTrackIndex] != vidId) return;
            if (_isLiveInitializing) return;
            _isLiveInitializing = true;
            StopPlaybackMonitor();

            try
            {
                if (_liveCts != null)
                {
                    try { _liveCts.Cancel(); _liveCts.Dispose(); } catch { }
                    _liveCts = null;
                }
                _liveCts = new CancellationTokenSource();
                var ct = _liveCts.Token;

                _isCurrentTrackLive = true;
                _liveBufferCycle++;
                _currentLiveBufferIndex = _liveBufferCycle % 3;
                _isNextLiveBufferReady = false;
                _nextLiveBufferTask = null;
                _isPreBuffering = false;
                _lastLiveSwapTime = DateTime.UtcNow;

                try
                {
                    ApplicationData.Current.LocalSettings.Values["IsCurrentLive"] = true;
                }
                catch { }

                var ls = ApplicationData.Current.LocalSettings.Values;
                bool normalize = ls.ContainsKey("NormalizeVolume") ? (bool)ls["NormalizeVolume"] : false;
                _mediaPlayer.Volume = normalize ? 0.75 : 1.0;
                UpdateSystemMediaControls();

                string buf0File = "temp_live_buf_" + _currentLiveBufferIndex + ".mp4";
                int initialDownloaded = 0;

                while (!ct.IsCancellationRequested && _liveReconnectCount < 3)
                {
                    // 1. Get or refresh BaseURL
                    if (string.IsNullOrEmpty(_currentLiveBaseUrl) || 
                        !_liveBaseUrlStopwatch.IsRunning || 
                        _liveBaseUrlStopwatch.Elapsed.TotalSeconds >= 15.0)
                    {
                        await RefreshLiveBaseUrlAsync(vidId, ct);
                    }

                    // 2. Discover live edge sequence number
                    long headSeq = -1;
                    if (!string.IsNullOrEmpty(_currentLiveBaseUrl))
                    {
                        headSeq = await GetLatestLiveSeqAsync(_currentLiveBaseUrl, ct);
                    }

                    if (headSeq > 0)
                    {
                        _currentLiveSeq = headSeq;
                        LogLive("[Live HEAD] seq=" + _currentLiveSeq);
                    }

                    // 3. Start safely in DVR window (10 segments = 50s behind live edge)
                    long safetyOffset = 10;
                    long startSeq = _currentLiveSeq > 0 ? Math.Max(1, _currentLiveSeq - safetyOffset) : -1;
                    if (startSeq <= 0 && _nextLiveStartSeq > 0)
                    {
                        startSeq = _nextLiveStartSeq;
                    }

                    // If still no valid sequence or BaseURL is missing, force a fresh BaseURL resolve
                    if (startSeq <= 0 || string.IsNullOrEmpty(_currentLiveBaseUrl))
                    {
                        await RefreshLiveBaseUrlAsync(vidId, ct, true);
                        if (!string.IsNullOrEmpty(_currentLiveBaseUrl))
                        {
                            headSeq = await GetLatestLiveSeqAsync(_currentLiveBaseUrl, ct);
                            if (headSeq > 0) _currentLiveSeq = headSeq;
                        }
                        startSeq = _currentLiveSeq > 0 ? Math.Max(1, _currentLiveSeq - safetyOffset) : -1;
                        if (startSeq <= 0 && _nextLiveStartSeq > 0) startSeq = _nextLiveStartSeq;
                    }

                    LogLive("[Live InitBuf0 Bắt đầu] startSeq=" + startSeq + " (currSeq=" + _currentLiveSeq + ", offset=" + safetyOffset + ")");
                    var dlSw0 = System.Diagnostics.Stopwatch.StartNew();
                    if (startSeq > 0 && !string.IsNullOrEmpty(_currentLiveBaseUrl))
                    {
                        initialDownloaded = await AssembleLiveBufferAsync(_currentLiveBaseUrl, startSeq, LIVE_INITIAL_SEGMENTS, buf0File, ct);
                    }
                    dlSw0.Stop();
                    LogLive("[Live InitBuf0 Xong] " + initialDownloaded + "/" + LIVE_INITIAL_SEGMENTS + " chunks in " + dlSw0.ElapsedMilliseconds + "ms");

                    if (initialDownloaded > 0 && !ct.IsCancellationRequested)
                    {
                        _nextLiveStartSeq = startSeq + initialDownloaded;
                        _currentLoadedVidId = vidId;

                        _liveBufferDurationSec = initialDownloaded * 5.0;
                        _isLiveSwapping = false;
                        _lastLiveSwapTime = DateTime.UtcNow;
                        try { _liveBufferStopwatch.Restart(); } catch { }

                        _mediaPlayer.AutoPlay = true;
                        string localUri = "ms-appdata:///local/" + buf0File;
                        _mediaPlayer.SetUriSource(new Uri(localUri));
                        try { _mediaPlayer.PlaybackRate = _playbackRate; } catch { }
                        _mediaPlayer.Play();
                        _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                        LogLive("[Live Phát Buffer " + _currentLiveBufferIndex + "] " + initialDownloaded + " chunk (" + _liveBufferDurationSec.ToString("F0") + "s)");

                        // Start playback monitor timer ONLY after buffer 0 is successfully playing!
                        StartPlaybackMonitor();
                        return;
                    }

                    _liveReconnectCount++;
                    LogLive("[Live Init Lỗi] Không tải được chunk ban đầu, thử lại lần " + _liveReconnectCount + "/3");
                    if (_liveReconnectCount < 3 && !ct.IsCancellationRequested)
                    {
                        _currentLiveBaseUrl = null;
                        _liveBufferCycle++;
                        _currentLiveBufferIndex = _liveBufferCycle % 3;
                        buf0File = "temp_live_buf_" + _currentLiveBufferIndex + ".mp4";
                        try { await Task.Delay(1500, ct); } catch { break; }
                    }
                }

                if (!ct.IsCancellationRequested && initialDownloaded <= 0)
                {
                    ReportErrorToUI("Live stream unavailable");
                }
            }
            finally
            {
                _isLiveInitializing = false;
            }
        }

        private async void CleanupLiveTempFiles()
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
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _videoIdList.Count) return;
            if (_videoIdList[_currentTrackIndex] != vidId) return;

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
                if (!_isCurrentTrackLive && _liveCts != null)
                {
                    try { _liveCts.Cancel(); _liveCts.Dispose(); } catch { }
                    _liveCts = null;
                }
                if (_currentLoadedVidId != vidId) _liveReconnectCount = 0;

                if (_isCurrentTrackLive && !string.IsNullOrEmpty(_currentLiveBaseUrl))
                {
                    LogLive("[Live PlayUrl] vid=" + vidId + " seq=" + _currentLiveSeq + " url=" + trackUrl.Substring(0, Math.Min(40, trackUrl.Length)) + "...");
                    PlayLiveBufferedTrackAsync(vidId);
                    return;
                }

                // Normalize Volume: set consistent volume level
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                bool normalize = ls.ContainsKey("NormalizeVolume") ? (bool)ls["NormalizeVolume"] : false;
                _mediaPlayer.Volume = normalize ? 0.75 : 1.0;

                UpdateSystemMediaControls();
                _mediaPlayer.SetUriSource(new Uri(trackUrl));
                try { _mediaPlayer.PlaybackRate = _playbackRate; } catch { }
                _currentLoadedVidId = vidId;
                _mediaPlayer.Play();
                _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;

                // Start crossfade monitoring (gapless pre-resolve triggers near end of track)
                StartPlaybackMonitor();
            }
            catch (Exception ex)
            {
                ReportErrorToUI("Stream Error: " + ex.Message.Split('\n')[0]);
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
            }

            // [FIX-SOF] Guard against re-entrancy – prevents StackOverflowException
            if (_isRetrying) return;
            _isRetrying = true;

            _currentLoadedVidId = "";
            _retryCount++;

            string vidId = (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count)
                ? _videoIdList[_currentTrackIndex] : "";

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
                _isRetrying = false; // Allow next failure to re-enter
                _cachedVisitorData = null;
                string freshUrl = await ResolveViaInnerTubeDirectAsync(vidId);
                if (!string.IsNullOrEmpty(freshUrl))
                {
                    freshUrl = PrepareStreamUrl(freshUrl);
                    _resolvedUrl = freshUrl;
                    _innerTubeAttempted = true;
                    StartPlaybackAsync();
                    return;
                }
            }

            // Retry 3-4: Dùng URL từ MainPage hoặc resolve lại nếu chưa có
            await Task.Delay(800);
            _isRetrying = false; // Allow next failure to re-enter
            _resolvedUrl = null;
            if (_trackList != null && _currentTrackIndex >= 0 && _currentTrackIndex < _trackList.Count && !string.IsNullOrEmpty(_trackList[_currentTrackIndex]))
            {
                _innerTubeAttempted = true;
            }
            else
            {
                _innerTubeAttempted = false;
            }
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
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                string prev = ls.ContainsKey("LiveDebugLog") ? (ls["LiveDebugLog"]?.ToString() ?? "") : "";
                string updated = line + "\n" + prev;
                // WinRT LocalSettings has a strict 4096-byte limit per value.
                // Cap to 1500 chars (3000 UTF-16 bytes) to never throw WinRT quota exceptions.
                if (updated.Length > 1500) updated = updated.Substring(0, 1500);
                ls["LiveDebugLog"] = updated;
            }
            catch { }
            // NOTE: Do NOT call SendToast here — sending rapid IPC messages to foreground
            // on every live chunk/event crashes Windows.Media.BackgroundPlayback.exe with 0x800703e9 (STATUS_STACK_OVERFLOW).
        }

        private void ReportErrorToUI(string errorDetail)
        {
            string title = (_currentTrackIndex >= 0 && _currentTrackIndex < _titleList.Count) ? _titleList[_currentTrackIndex] : "Beatora";
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                ls["CurrentTitle"] = title; ls["CurrentArtist"] = errorDetail;
                var msg = new ValueSet { { "TrackChanged", "" }, { "NewTitle", title }, { "NewArtist", errorDetail } };
                if (_currentTrackIndex >= 0 && _currentTrackIndex < _thumbnailList.Count) msg.Add("NewThumbnail", _thumbnailList[_currentTrackIndex]);
                BackgroundMediaPlayer.SendMessageToForeground(msg);
            }
            catch { }
            try { _systemControls.DisplayUpdater.MusicProperties.Title = title; _systemControls.DisplayUpdater.MusicProperties.Artist = errorDetail; _systemControls.DisplayUpdater.Update(); } catch { }
        }

        private void UpdateSystemMediaControls()
        {
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _titleList.Count) return;
            string title = _titleList[_currentTrackIndex], artist = _artistList[_currentTrackIndex];
            string thumb = _thumbnailList[_currentTrackIndex], vidId = _videoIdList[_currentTrackIndex];

            try { _systemControls.DisplayUpdater.Type = MediaPlaybackType.Music; _systemControls.DisplayUpdater.MusicProperties.Title = title; _systemControls.DisplayUpdater.MusicProperties.Artist = artist; _systemControls.DisplayUpdater.Update(); } catch { }
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                // FIX Bug 12: DÃƒÆ’Ã‚Â¹ng ContainsKey trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â ApplicationDataContainer throws KeyNotFoundException nÃƒÂ¡Ã‚ÂºÃ‚Â¿u key chÃƒâ€ Ã‚Â°a tÃƒÂ¡Ã‚Â»Ã¢â‚¬Å“n tÃƒÂ¡Ã‚ÂºÃ‚Â¡i
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
                            "<binding template=\"TileSquare310x310PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text><text id=\"2\">{2}</text></binding>" +
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
                if (_trackList.Count > 1 && remaining <= 15 && remaining > 10 && string.IsNullOrEmpty(_preResolvedNextUrl) && !_isPreResolving)
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
                if (_trackList.Count <= 1) return;

                bool shuffle = ls.ContainsKey("ShuffleMode") ? (bool)ls["ShuffleMode"] : false;
                int repeat = ls.ContainsKey("RepeatMode") ? (int)ls["RepeatMode"] : 0;
                bool autoplay = ls.ContainsKey("Autoplay") ? (bool)ls["Autoplay"] : true;

                int nextIdx;
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
                string nextVidId = _videoIdList[nextIdx];
                if (nextVidId.StartsWith("LOCAL:"))
                {
                    _preResolvedNextUrl = _trackList[nextIdx];
                    _preResolvedNextIndex = nextIdx;
                    _preResolvedNextVideoId = nextVidId;
                    return;
                }

                string url = await ResolveViaInnerTubeDirectAsync(nextVidId);
                // Strict check: verify target index and videoId still match after async call
                if (!string.IsNullOrEmpty(url) && nextIdx < _videoIdList.Count && _videoIdList[nextIdx] == nextVidId)
                {
                    _preResolvedNextUrl = PrepareStreamUrl(url);
                    _preResolvedNextIndex = nextIdx;
                    _preResolvedNextVideoId = nextVidId;
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
            if (_trackList.Count == 0) return;
            var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            bool shuffle = ls.ContainsKey("ShuffleMode") ? (bool)ls["ShuffleMode"] : false;
            int repeat = ls.ContainsKey("RepeatMode") ? (int)ls["RepeatMode"] : 0;
            bool autoplay = ls.ContainsKey("Autoplay") ? (bool)ls["Autoplay"] : true;
            if (repeat == 2) { ResetRetryState(); _currentLoadedVidId = ""; StartPlaybackAsync(); return; }
            ResetRetryState();

            // Calculate expected next index
            int targetIdx;
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

            string preUrl = _preResolvedNextUrl;
            int preIdx = _preResolvedNextIndex;
            string preVid = _preResolvedNextVideoId;
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

            StartPlaybackAsync();
        }

        private void MovePrevious()
        {
            if (_trackList.Count == 0) return;
            if (_mediaPlayer.Position.TotalSeconds > 3) { _mediaPlayer.Position = TimeSpan.Zero; _mediaPlayer.Play(); return; }
            var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            bool shuffle = ls.ContainsKey("ShuffleMode") ? (bool)ls["ShuffleMode"] : false;
            int repeat = ls.ContainsKey("RepeatMode") ? (int)ls["RepeatMode"] : 0;
            if (repeat == 2) { ResetRetryState(); StartPlaybackAsync(); return; }
            ResetRetryState();
            ClearPreResolvedState();
            if (shuffle) _currentTrackIndex = _rand.Next(0, _trackList.Count);
            else { _currentTrackIndex--; if (_currentTrackIndex < 0) { if (repeat == 1) _currentTrackIndex = _trackList.Count - 1; else { _currentTrackIndex = 0; return; } } }
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
                    if (_currentTrackIndex >= 0 && _currentTrackIndex < _videoIdList.Count)
                    {
                        PlayLiveBufferedTrackAsync(_videoIdList[_currentTrackIndex]);
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
                        double elapsed = _liveBufferStopwatch.Elapsed.TotalSeconds;
                        // Immediate swap when player pauses near end of buffer! Eliminates 250ms waiting for MediaEnded!
                        if (_liveBufferDurationSec > 1.0 && elapsed >= (_liveBufferDurationSec - 1.5) &&
                            !_isLiveSwapping && !_isLiveInitializing && (DateTime.UtcNow - _lastLiveSwapTime).TotalSeconds >= 2.5)
                        {
                            LogLive("[Live State Paused Swap] elapsed=" + elapsed.ToString("F1") + "s/" + _liveBufferDurationSec.ToString("F1") + "s");
                            SwapToNextLiveBuffer();
                            return;
                        }
                        try { _liveBufferStopwatch.Stop(); } catch { }
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
                if (sender.AutoPlay || sender.CurrentState != MediaPlayerState.Playing)
                {
                    sender.Play();
                }

                if (_isCurrentTrackLive)
                {
                    try { _liveBufferStopwatch.Restart(); } catch { }
                    try
                    {
                        if (sender.NaturalDuration > TimeSpan.Zero)
                        {
                            _liveBufferDurationSec = sender.NaturalDuration.TotalSeconds;
                        }
                    }
                    catch { }
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    LogLive("[Live MediaOpened] Đang phát Buffer " + _currentLiveBufferIndex + " (" + _liveBufferDurationSec.ToString("F1") + "s, state=" + sender.CurrentState + ")");
                }
            }
            catch { }
        }

        private void SystemControls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play: try { if (_mediaPlayer.CurrentState == MediaPlayerState.Closed) StartPlaybackAsync(); else _mediaPlayer.Play(); } catch { StartPlaybackAsync(); } break;
                case SystemMediaTransportControlsButton.Pause: try { _mediaPlayer.Pause(); } catch { } break;
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
