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

namespace AudioPlayerTask
{
    public sealed class AudioTask : IBackgroundTask
    {
        private BackgroundTaskDeferral _deferral;
        private SystemMediaTransportControls _systemControls;
        private MediaPlayer _mediaPlayer;

        // [OPT] Shared HttpClient — avoids socket leaks from creating new instances per resolve call
        private Windows.Web.Http.HttpClient _httpClient = new Windows.Web.Http.HttpClient();

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
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
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
                _mediaPlayer.CurrentStateChanged -= MediaPlayer_CurrentStateChanged;
                BackgroundMediaPlayer.MessageReceivedFromForeground -= BackgroundMediaPlayer_MessageReceivedFromForeground;
                BackgroundMediaPlayer.Shutdown();
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
                StartPlaybackAsync();
            }
            else if (e.Data.ContainsKey("NextTrackMessage")) MoveNext();
            else if (e.Data.ContainsKey("PrevTrackMessage")) MovePrevious();
        }

        private void ResetRetryState()
        {
            _retryCount = 0;
            _isRetrying = false;
            _resolvedUrl = null;
            _innerTubeAttempted = false;
        }

        // ==========================================
        // RESOLVE AUDIO URL — InnerTube direct
        // Layer 1: InnerTube ANDROID_VR trực tiếp từ phone
        // Layer 2: Invidious API fallback
        // Layer 3: Invidious Embed proxy
        // ==========================================
        private string _innerTubeDebug = "";

        /// <summary>
        /// Lấy visitorData — cache + 2 nguồn (sw.js_data + youtube.com homepage)
        /// </summary>
        private static string _cachedVisitorData = null;

        private async Task<string> GetVisitorDataAsync(string videoId = null)
        {
            // Dùng cache nếu có
            if (!string.IsNullOrEmpty(_cachedVisitorData))
                return _cachedVisitorData;

            // Nguồn 1: Watch page (chính xác nhất — visitorData đi kèm video)
            if (!string.IsNullOrEmpty(videoId))
            {
                string vd = await FetchVisitorDataFromWatchPage(videoId);
                if (!string.IsNullOrEmpty(vd))
                {
                    _cachedVisitorData = vd;
                    return vd;
                }
            }

            // Nguồn 2: sw.js_data (giống MetroTube)
            string vd2 = await FetchVisitorDataFromSwJs();
            if (!string.IsNullOrEmpty(vd2))
            {
                _cachedVisitorData = vd2;
                return vd2;
            }

            // Nguồn 3: youtube.com homepage
            vd2 = await FetchVisitorDataFromHomepage();
            if (!string.IsNullOrEmpty(vd2))
            {
                _cachedVisitorData = vd2;
                return vd2;
            }

            return null;
        }

        private async Task<string> FetchVisitorDataFromWatchPage(string videoId)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

                var response = await _httpClient.GetAsync(new Uri("https://www.youtube.com/watch?v=" + videoId));
                if (!response.IsSuccessStatusCode) return null;

                string html = await response.Content.ReadAsStringAsync();
                return ExtractVisitorData(html);
            }
            catch { return null; }
        }

        private async Task<string> FetchVisitorDataFromSwJs()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Linux; Android 9; BRAVIA 8K UR2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.125 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await _httpClient.GetAsync(new Uri("https://www.youtube.com/sw.js_data"));
                if (!response.IsSuccessStatusCode) return null;

                string result = await response.Content.ReadAsStringAsync();

                if (result.StartsWith(")]}'"))
                    result = result.Substring(4);

                // Tìm visitorData bằng Regex: base64 protobuf string bắt đầu bằng Cg
                return ExtractVisitorData(result);
            }
            catch { return null; }
        }

        private async Task<string> FetchVisitorDataFromHomepage()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var response = await _httpClient.GetAsync(new Uri("https://www.youtube.com/"));
                if (!response.IsSuccessStatusCode) return null;

                string html = await response.Content.ReadAsStringAsync();

                // HTML chứa: "visitorData":"CgXXXXX"
                return ExtractVisitorData(html);
            }
            catch { return null; }
        }

        private string ExtractVisitorData(string text)
        {
            // Tìm visitorData":"CgXXX" hoặc "visitorData":"CgXXX"
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
                        string vd = text.Substring(start, end - start);
                        if (vd.StartsWith("Cg")) return vd;
                    }
                }
            }

            // Tìm "CgXXX" (không có key name, trong array format)
            // Scan for quoted strings starting with "Cg" that are 20+ chars (visitorData length)
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
                        string candidate = text.Substring(start2, len);
                        // Verify: visitorData là base64 protobuf, chỉ chứa A-Za-z0-9_-=
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
                searchPos = quotePos + 3;
            }

            return null;
        }

        private async Task<string> ResolveViaInnerTubeDirectAsync(string videoId)
        {
            _innerTubeDebug = "";
            
            // Layer 1: InnerTube ANDROID_VR (giống MetroTube)
            string url = await TryInnerTubeClient(videoId, "ANDROID_VR", "1.60.19", "28", "Oculus", "Quest 3", "12L",
                "com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip");
            if (!string.IsNullOrEmpty(url)) return url;

            // Layer 2: Invidious API fallback (giống MetroTube)
            string[] invInstances = new string[] { "yewtu.be", "iv.duti.dev", "invidious.schenkel.eti.br", "inv.nadeko.net" };
            foreach (var inst in invInstances)
            {
                try
                {
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                    // Invidious API
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var resp = await _httpClient.GetAsync(new Uri("https://" + inst + "/api/v1/videos/" + videoId + "?fields=adaptiveFormats,formatStreams")).AsTask(cts.Token);
                    if (resp.StatusCode != Windows.Web.Http.HttpStatusCode.Ok) continue;
                    
                    string json = await resp.Content.ReadAsStringAsync();
                    
                    // Parse itag 140 URL bằng string matching (không có Newtonsoft.Json)
                    string audioUrl = ExtractInvidiousUrl(json, "140");
                    if (string.IsNullOrEmpty(audioUrl))
                        audioUrl = ExtractInvidiousUrl(json, "139");
                    if (string.IsNullOrEmpty(audioUrl))
                        audioUrl = ExtractInvidiousUrl(json, "18");
                    
                    if (!string.IsNullOrEmpty(audioUrl))
                    {
                        _innerTubeDebug += " inv:" + inst.Substring(0, Math.Min(6, inst.Length));
                        return audioUrl;
                    }
                }
                catch { continue; }
            }

            // Layer 3: Invidious Embed proxy (bypass geo-block)
            foreach (var inst in invInstances)
            {
                try
                {
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var resp = await _httpClient.GetAsync(new Uri("https://" + inst + "/embed/" + videoId + "?local=1")).AsTask(cts.Token);
                    if (resp.StatusCode != Windows.Web.Http.HttpStatusCode.Ok) continue;
                    
                    string html = await resp.Content.ReadAsStringAsync();
                    
                    // Parse <source src="...itag=140..."> from embed HTML
                    string[] itags = new string[] { "itag=140", "itag=139", "itag=18" };
                    foreach (var itagSearch in itags)
                    {
                        int srcIdx = html.IndexOf(itagSearch);
                        if (srcIdx < 0) continue;
                        
                        int tagStart = html.LastIndexOf("<source", srcIdx);
                        if (tagStart < 0) continue;
                        
                        int srcAttr = html.IndexOf("src=\"", tagStart);
                        if (srcAttr < 0 || srcAttr > srcIdx + 20) continue;
                        
                        int urlStart = srcAttr + 5;
                        int urlEnd = html.IndexOf("\"", urlStart);
                        if (urlEnd <= urlStart) continue;
                        
                        string rawUrl = html.Substring(urlStart, urlEnd - urlStart).Replace("&amp;", "&");
                        if (rawUrl.StartsWith("/"))
                            rawUrl = "https://" + inst + rawUrl;
                        
                        if (!string.IsNullOrEmpty(rawUrl))
                        {
                            _innerTubeDebug += " emb:" + inst.Substring(0, Math.Min(6, inst.Length));
                            return rawUrl;
                        }
                    }
                }
                catch { continue; }
            }

            return null;
        }

        /// <summary>
        /// Extract URL for a specific itag from Invidious JSON response using string parsing
        /// (AudioTask doesn't have Newtonsoft.Json)
        /// </summary>
        private string ExtractInvidiousUrl(string json, string targetItag)
        {
            // Tìm pattern: "itag":"140" hoặc "itag":140 rồi tìm "url":"..." gần đó
            string pattern1 = "\"itag\":\"" + targetItag + "\"";
            string pattern2 = "\"itag\":" + targetItag;
            
            int pos = json.IndexOf(pattern1);
            if (pos < 0) pos = json.IndexOf(pattern2);
            if (pos < 0) return null;
            
            // Tìm "url":"..." trong vùng xung quanh (trước hoặc sau itag, trong cùng object {})
            // Tìm lùi về đầu object
            int objStart = json.LastIndexOf("{", pos);
            // Tìm tiến đến cuối object (tìm }, nhưng cẩn thận nested)
            int objEnd = pos + 500; // rough estimate
            if (objEnd > json.Length) objEnd = json.Length;
            string segment = json.Substring(objStart, objEnd - objStart);
            
            string urlMarker = "\"url\":\"";
            int urlPos = segment.IndexOf(urlMarker);
            if (urlPos < 0) return null;
            
            int urlStart = urlPos + urlMarker.Length;
            int urlEnd = segment.IndexOf("\"", urlStart);
            if (urlEnd <= urlStart) return null;
            
            string rawUrl = segment.Substring(urlStart, urlEnd - urlStart);
            // Unescape JSON string
            rawUrl = rawUrl.Replace("\\u0026", "&").Replace("\\/", "/");
            return rawUrl;
        }

        private async Task<string> TryInnerTubeClient(string videoId, string clientName, string clientVersion, 
            string clientId, string deviceMake, string deviceModel, string osVersion, string userAgent)
        {
            try
            {
                string visitorData = await GetVisitorDataAsync(videoId);
                string vdShort = visitorData != null ? visitorData.Substring(0, Math.Min(8, visitorData.Length)) : "NULL";

                _httpClient.DefaultRequestHeaders.Clear();

                string vdField = "";
                if (!string.IsNullOrEmpty(visitorData))
                    vdField = ",\"visitorData\":\"" + visitorData + "\"";

                string requestBody = "{" +
                    "\"contentCheckOk\":true," +
                    "\"context\":{\"client\":{" +
                        "\"clientName\":\"" + clientName + "\"," +
                        "\"clientVersion\":\"" + clientVersion + "\"," +
                        "\"deviceMake\":\"" + deviceMake + "\"," +
                        "\"deviceModel\":\"" + deviceModel + "\"," +
                        "\"osName\":\"ANDROID\"," +
                        "\"osVersion\":\"" + osVersion + "\"," +
                        "\"platform\":\"MOBILE\"," +
                        "\"clientScreen\":0," +
                        "\"hl\":\"en\"," +
                        "\"gl\":\"US\"" +
                        vdField +
                    "}}," +
                    "\"videoId\":\"" + videoId + "\"" +
                "}";

                var content = new Windows.Web.Http.HttpStringContent(
                    requestBody,
                    Windows.Storage.Streams.UnicodeEncoding.Utf8,
                    "application/json"
                );

                _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
                _httpClient.DefaultRequestHeaders.Add("X-YouTube-Client-Name", clientId);
                _httpClient.DefaultRequestHeaders.Add("X-YouTube-Client-Version", clientVersion);

                // Giống MetroTube: thêm &fields= để giảm response size
                var response = await _httpClient.PostAsync(
                    new Uri("https://www.youtube.com/youtubei/v1/player?key=AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI&prettyPrint=false&fields=playabilityStatus,streamingData"),
                    content
                );

                if (!response.IsSuccessStatusCode)
                {
                    _innerTubeDebug = clientName + ":HTTP" + (int)response.StatusCode;
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();

                // Kiểm tra playabilityStatus
                string status = "";
                int statusPos = json.IndexOf("\"status\":\"");
                if (statusPos >= 0)
                {
                    int sStart = statusPos + 10;
                    int sEnd = json.IndexOf("\"", sStart);
                    if (sEnd > sStart)
                        status = json.Substring(sStart, sEnd - sStart);
                }

                if (status != "OK")
                {
                    _innerTubeDebug = clientName + ":" + status;
                    return null;
                }

                string audioUrl = FindUrlByItag(json, "140");
                if (string.IsNullOrEmpty(audioUrl))
                    audioUrl = FindUrlByItag(json, "139");
                if (string.IsNullOrEmpty(audioUrl))
                    audioUrl = FindUrlByItag(json, "18");

                if (!string.IsNullOrEmpty(audioUrl))
                {
                    _innerTubeDebug = clientName + ":OK";
                    return audioUrl;
                }

                _innerTubeDebug = clientName + ":NO_URL";
                _cachedVisitorData = null;
                return null;
            }
            catch (Exception ex)
            {
                _innerTubeDebug = clientName + ":EX:" + ex.Message.Substring(0, Math.Min(30, ex.Message.Length));
                return null;
            }
        }

        /// <summary>
        /// Parse JSON thủ công để tìm URL theo itag.
        /// Tìm pattern: "itag":140,..."url":"https://..."
        /// </summary>
        private string FindUrlByItag(string json, string itag)
        {
            // Tìm "itag":140 hoặc "itag": 140
            string marker = "\"itag\":" + itag;
            int pos = json.IndexOf(marker);
            if (pos < 0)
            {
                marker = "\"itag\": " + itag;
                pos = json.IndexOf(marker);
            }
            if (pos < 0) return null;

            // Tìm "url":"..." gần nhất SAU itag
            string urlMarker = "\"url\":\"";
            int urlPos = json.IndexOf(urlMarker, pos);
            if (urlPos < 0 || urlPos - pos > 500) return null; // Quá xa = sai format

            int urlStart = urlPos + urlMarker.Length;
            int urlEnd = json.IndexOf("\"", urlStart);
            if (urlEnd <= urlStart) return null;

            string url = json.Substring(urlStart, urlEnd - urlStart)
                .Replace("\\/", "/")
                .Replace("\\u0026", "&");

            if (url.StartsWith("http")) return url;
            return null;
        }


        // ==========================================
        // MAIN PLAYBACK — InnerTube direct only
        // ==========================================
        private async void StartPlaybackAsync()
        {
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _trackList.Count) return;

            string vidId = _videoIdList[_currentTrackIndex];

            // Offline track → phát trực tiếp
            if (vidId.StartsWith("LOCAL:"))
            {
                PlayUrl(_trackList[_currentTrackIndex], vidId);
                return;
            }

            // Skip nếu bài cũ vẫn đang phát (không retry)
            if (vidId == _currentLoadedVidId && _mediaPlayer.CurrentState != MediaPlayerState.Closed && _retryCount == 0)
            {
                try { _mediaPlayer.Position = TimeSpan.Zero; _mediaPlayer.Play(); _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing; UpdateSystemMediaControls(); }
                catch { }
                return;
            }

            // Nếu đã có URL resolved (từ retry)
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

                string directUrl = await ResolveViaInnerTubeDirectAsync(vidId);
                if (!string.IsNullOrEmpty(directUrl))
                {
                    directUrl = PrepareStreamUrl(directUrl);
                    PlayUrl(directUrl, vidId);
                    return;
                }
            }

            // FALLBACK: URL từ MainPage — nếu rỗng thì resolve InnerTube lần nữa
            string fallbackUrl = _trackList[_currentTrackIndex];
            if (string.IsNullOrEmpty(fallbackUrl))
            {
                fallbackUrl = await ResolveViaInnerTubeDirectAsync(vidId);
            }
            if (!string.IsNullOrEmpty(fallbackUrl))
                PlayUrl(PrepareStreamUrl(fallbackUrl), vidId);
            else
                ReportErrorToUI("No stream available");
        }

        /// <summary>
        /// Thêm params chống throttle vào googlevideo URL:
        /// - ratebypass=yes: bỏ giới hạn tốc độ
        /// - range=0-: ép server gửi toàn bộ audio trong 1 response (full buffer)
        /// </summary>
        private string PrepareStreamUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.Contains("googlevideo"))
                return url;
            if (!url.Contains("ratebypass"))
                url += "&ratebypass=yes";
            if (!url.Contains("range="))
                url += "&range=0-";
            return url;
        }

        private void PlayUrl(string trackUrl, string vidId)
        {
            _mediaPlayer.AutoPlay = false;
            try
            {
                StopCrossfadeMonitor();

                // Normalize Volume: set consistent volume level
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                bool normalize = ls.ContainsKey("NormalizeVolume") ? (bool)ls["NormalizeVolume"] : false;
                _mediaPlayer.Volume = normalize ? 0.75 : 1.0;

                UpdateSystemMediaControls();
                _mediaPlayer.SetUriSource(new Uri(trackUrl));
                _currentLoadedVidId = vidId;
                _mediaPlayer.Play();
                _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;

                // Start crossfade monitoring and gapless pre-resolve
                StartCrossfadeMonitor();
                PreResolveNextTrack();
            }
            catch (Exception ex)
            {
                ReportErrorToUI("Stream Error: " + ex.Message.Split('\n')[0]);
            }
        }

        // ==========================================
        // RETRY FLOW — InnerTube only
        // Retry 1-2: Lấy URL InnerTube mới (URL cũ hết hạn)
        // Retry 3-4: Dùng URL từ MainPage
        // ==========================================
        private async void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            // [FIX-SOF] Guard against re-entrancy — prevents StackOverflowException
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

            // Retry 3-4: Dùng URL từ MainPage
            await Task.Delay(800);
            _isRetrying = false; // Allow next failure to re-enter
            _innerTubeAttempted = true;
            _resolvedUrl = null;
            StartPlaybackAsync();
        }

        private void SendToast(string message)
        {
            try { BackgroundMediaPlayer.SendMessageToForeground(new ValueSet { { "ToastMessage", message } }); } catch { }
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
                // FIX Bug 12: Dùng ContainsKey trước — ApplicationDataContainer throws KeyNotFoundException nếu key chưa tồn tại
                string storedTitle = ls.ContainsKey("CurrentTitle") ? ls["CurrentTitle"]?.ToString() : null;
                string storedArtist = ls.ContainsKey("CurrentArtist") ? ls["CurrentArtist"]?.ToString() : null;
                string storedVid = ls.ContainsKey("CurrentVideoId") ? ls["CurrentVideoId"]?.ToString() : null;
                string storedThumb = ls.ContainsKey("CurrentThumbnail") ? ls["CurrentThumbnail"]?.ToString() : null;

                if (storedTitle != title) ls["CurrentTitle"] = title;
                if (storedArtist != artist) ls["CurrentArtist"] = artist;
                if (storedVid != vidId) ls["CurrentVideoId"] = vidId;
                if (storedThumb != thumb) ls["CurrentThumbnail"] = thumb;
            }
            catch { }
            try
            {
                var updater = TileUpdateManager.CreateTileUpdaterForApplication(); updater.EnableNotificationQueue(true); updater.Clear();
                string xml = string.Format("<tile><visual version=\"2\"><binding template=\"TileSquare150x150Image\"><image id=\"1\" src=\"{0}\" placement=\"background\"/></binding><binding template=\"TileWide310x150ImageAndText01\"><image id=\"1\" src=\"{0}\" placement=\"background\"/><text id=\"1\">{1}</text></binding></visual></tile>", System.Net.WebUtility.HtmlEncode(thumb), System.Net.WebUtility.HtmlEncode(title));
                var doc = new XmlDocument(); doc.LoadXml(xml); updater.Update(new TileNotification(doc));
            }
            catch { }
            try { BackgroundMediaPlayer.SendMessageToForeground(new ValueSet { { "TrackChanged", "" }, { "NewTitle", title }, { "NewArtist", artist }, { "NewVideoId", vidId }, { "NewThumbnail", thumb } }); } catch { }
        }

        // ── Crossfade & Gapless ──
        private Windows.System.Threading.ThreadPoolTimer _crossfadeTimer;
        private bool _isCrossfading = false;
        private string _preResolvedNextUrl = null;

        private void StartCrossfadeMonitor()
        {
            StopCrossfadeMonitor();
            // Poll every 500ms to check if we're near end of track
            _crossfadeTimer = Windows.System.Threading.ThreadPoolTimer.CreatePeriodicTimer(
                CrossfadeTimer_Tick, TimeSpan.FromMilliseconds(500));
        }

        private void StopCrossfadeMonitor()
        {
            if (_crossfadeTimer != null) { _crossfadeTimer.Cancel(); _crossfadeTimer = null; }
            _isCrossfading = false;
        }

        private void CrossfadeTimer_Tick(Windows.System.Threading.ThreadPoolTimer timer)
        {
            try
            {
                if (_mediaPlayer == null || _mediaPlayer.CurrentState != MediaPlayerState.Playing) return;
                if (_isCrossfading) return;

                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                int crossfadeSec = ls.ContainsKey("CrossfadeSeconds") ? (int)ls["CrossfadeSeconds"] : 0;
                if (crossfadeSec <= 0) return;

                double pos = _mediaPlayer.Position.TotalSeconds;
                double dur = _mediaPlayer.NaturalDuration.TotalSeconds;
                if (dur <= 0) return;

                double remaining = dur - pos;
                if (remaining <= crossfadeSec && remaining > 0.5)
                {
                    _isCrossfading = true;
                    // Start volume fade out
                    StartVolumeFade(crossfadeSec);
                }
            }
            catch { }
        }

        private async void StartVolumeFade(int fadeDurationSec)
        {
            try
            {
                double startVol = _mediaPlayer.Volume;
                int steps = fadeDurationSec * 5; // 5 steps per second (200ms each)
                double volStep = startVol / steps;

                for (int i = 0; i < steps; i++)
                {
                    await Task.Delay(200);
                    try
                    {
                        double newVol = startVol - (volStep * (i + 1));
                        if (newVol < 0) newVol = 0;
                        _mediaPlayer.Volume = newVol;
                    }
                    catch { break; }
                }

                // Crossfade complete → move to next
                _isCrossfading = false;
                MoveNext();
            }
            catch { _isCrossfading = false; }
        }

        private async void PreResolveNextTrack()
        {
            _preResolvedNextUrl = null;
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                bool gapless = ls.ContainsKey("GaplessPlayback") ? (bool)ls["GaplessPlayback"] : true;
                if (!gapless) return;
                if (_trackList.Count <= 1) return;

                bool shuffle = ls.ContainsKey("ShuffleMode") ? (bool)ls["ShuffleMode"] : false;
                int repeat = ls.ContainsKey("RepeatMode") ? (int)ls["RepeatMode"] : 0;

                int nextIdx;
                if (repeat == 2) nextIdx = _currentTrackIndex;
                else if (shuffle) nextIdx = _rand.Next(0, _trackList.Count);
                else
                {
                    nextIdx = _currentTrackIndex + 1;
                    if (nextIdx >= _trackList.Count)
                    {
                        if (repeat == 1) nextIdx = 0;
                        else return;
                    }
                }

                string nextVidId = _videoIdList[nextIdx];
                if (nextVidId.StartsWith("LOCAL:"))
                {
                    _preResolvedNextUrl = _trackList[nextIdx];
                    return;
                }

                string url = await ResolveViaInnerTubeDirectAsync(nextVidId);
                if (!string.IsNullOrEmpty(url))
                    _preResolvedNextUrl = PrepareStreamUrl(url);
            }
            catch { }
        }

        private void MoveNext()
        {
            if (_trackList.Count == 0) return;
            var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            bool shuffle = ls.ContainsKey("ShuffleMode") ? (bool)ls["ShuffleMode"] : false;
            int repeat = ls.ContainsKey("RepeatMode") ? (int)ls["RepeatMode"] : 0;
            bool autoplay = ls.ContainsKey("Autoplay") ? (bool)ls["Autoplay"] : true;
            if (repeat == 2) { ResetRetryState(); StartPlaybackAsync(); return; }
            ResetRetryState();

            // Use pre-resolved URL if available (gapless)
            string preUrl = _preResolvedNextUrl;
            _preResolvedNextUrl = null;

            if (shuffle) _currentTrackIndex = _rand.Next(0, _trackList.Count);
            else
            {
                _currentTrackIndex++;
                if (_currentTrackIndex >= _trackList.Count)
                {
                    if (repeat == 1) _currentTrackIndex = 0;
                    else if (!autoplay) { _currentTrackIndex = _trackList.Count - 1; return; } // Stop if autoplay off
                    else { _currentTrackIndex = _trackList.Count - 1; return; }
                }
            }

            // If we have a pre-resolved URL, use it directly for gapless transition
            if (!string.IsNullOrEmpty(preUrl))
            {
                _trackList[_currentTrackIndex] = preUrl;
                _innerTubeAttempted = true;
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
            if (shuffle) _currentTrackIndex = _rand.Next(0, _trackList.Count);
            else { _currentTrackIndex--; if (_currentTrackIndex < 0) { if (repeat == 1) _currentTrackIndex = _trackList.Count - 1; else { _currentTrackIndex = 0; return; } } }
            StartPlaybackAsync();
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            // If crossfade already triggered MoveNext, skip
            if (_isCrossfading) return;
            StopCrossfadeMonitor();
            MoveNext();
        }

        private void MediaPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            try
            {
                if (sender.CurrentState == MediaPlayerState.Playing)
                {
                    // [FIX-SOF] Only reset retryCount if NOT in a retry cycle
                    // Without this guard, player briefly entering Playing before failing
                    // would reset _retryCount → infinite retry → StackOverflow
                    if (!_isRetrying) _retryCount = 0;
                    _isRetrying = false;
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                }
                else if (sender.CurrentState == MediaPlayerState.Paused) _systemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                else if (sender.CurrentState == MediaPlayerState.Closed) _systemControls.PlaybackStatus = MediaPlaybackStatus.Closed;
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
    }
}