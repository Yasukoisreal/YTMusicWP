using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace YTMusicWP
{
    public static partial class InnerTubeClient
    {
        public class RemotePoTokenResult
        {
            public string PoToken { get; set; }
            public string VisitorData { get; set; }
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

        private static string _cachedPoTokenVideoId = null;
        private static RemotePoTokenResult _cachedPoTokenResult = null;

        private static async Task<RemotePoTokenResult> FetchRemotePoTokenAsync(string videoId, string clientName)
        {
            if (_cachedPoTokenVideoId == videoId && _cachedPoTokenResult != null)
            {
                return _cachedPoTokenResult;
            }

            try
            {
                // Cloudflare Worker acts as TLS proxy to Render (bgutil-ytdlp-pot-provider)
                string serverUrl = "https://potoken-api.nguyentruongan06052007.workers.dev/?content_binding=" + Uri.EscapeDataString(videoId ?? "");
                
                var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter();
                filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Untrusted);
                filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
                
                using (var httpClient = new Windows.Web.Http.HttpClient(filter))
                {
                    using (var resp = await httpClient.GetAsync(new Uri(serverUrl)))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        string json = await resp.Content.ReadAsStringAsync();
                        
                        var result = new RemotePoTokenResult();
                        try
                        {
                            var data = JObject.Parse(json);
                            result.PoToken = data["po_token"]?.ToString() ?? data["poToken"]?.ToString();
                            result.VisitorData = data["visitor_data"]?.ToString() ?? data["visitorData"]?.ToString() ?? data["visit_identifier"]?.ToString() ?? data["contentBinding"]?.ToString() ?? data["content_binding"]?.ToString();
                        }
                        catch
                        {
                            result.PoToken = ExtractJsonField(json, "po_token") ?? ExtractJsonField(json, "poToken");
                            result.VisitorData = ExtractJsonField(json, "visitor_data") ?? ExtractJsonField(json, "visitorData") ?? ExtractJsonField(json, "visit_identifier") ?? ExtractJsonField(json, "contentBinding") ?? ExtractJsonField(json, "content_binding");
                        }

                        if (!string.IsNullOrEmpty(result.PoToken))
                        {
                            _cachedPoTokenVideoId = videoId;
                            _cachedPoTokenResult = result;
                            return result;
                        }
                        return null;
                    }
                }
            }
            catch { return null; }
        }

        public static string LastResolveDebug = "";

        // [OPT] Cache captions from player response — avoids duplicate InnerTube call in GetCaptionTracksAsync
        private static string _cachedCaptionsVideoId;
        private static JToken _cachedCaptionsData;

        public class PlayerClientConfig
        {
            public string ClientName { get; set; }
            public string ClientVersion { get; set; }
            public string UserAgent { get; set; }
            public string ExtraClientParams { get; set; } = "";
            public string ApiKey { get; set; } = "AIzaSyB-63vPrdThhKuerbB2N_l7Kwwcxj6yUAc";
            public bool RequireCookie { get; set; } = false;
            public string RequestClientNameHeader { get; set; } = null;
            public bool SupportsPoToken { get; set; } = false;
        }

        private static readonly PlayerClientConfig[] _playerClients = new PlayerClientConfig[]
        {
            // 0. ANDROID v20.49.37 - Ưu tiên số 1: Không bị bóp băng thông (throttling), lấy itag 18 trực tiếp
            new PlayerClientConfig {
                ClientName = "ANDROID",
                ClientVersion = "20.49.37",
                UserAgent = "com.google.android.youtube/20.49.37 (Linux; U; Android 11) gzip",
                ExtraClientParams = ",\"deviceMake\":\"Nokia\",\"deviceModel\":\"LumiaWP\",\"osName\":\"Android\",\"osVersion\":\"11\",\"platform\":\"MOBILE\",\"androidSdkVersion\":30,\"clientFormFactor\":0",
                ApiKey = "AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI",
                RequireCookie = false,
                RequestClientNameHeader = "3"
            },
            // 1. ANDROID v21.02.35 (Pixel 7) - Dự phòng Android version mới của yt-dlp
            new PlayerClientConfig {
                ClientName = "ANDROID",
                ClientVersion = "21.02.35",
                UserAgent = "com.google.android.youtube/21.02.35 (Linux; U; Android 11) gzip",
                ExtraClientParams = ",\"deviceMake\":\"Google\",\"deviceModel\":\"Pixel 7\",\"osName\":\"Android\",\"osVersion\":\"11\",\"platform\":\"MOBILE\",\"androidSdkVersion\":30,\"clientFormFactor\":0",
                ApiKey = "AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI",
                RequireCookie = false,
                RequestClientNameHeader = "3"
            },
            // 2. WEB_REMIX (YouTube Music) - Best for premium/cookie users
            new PlayerClientConfig {
                ClientName = "WEB_REMIX",
                ClientVersion = "1.20260304.03.00",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
                ExtraClientParams = "",
                ApiKey = "AIzaSyC9XL3ZjWddXya6X74dJoCTL-WEYFDNX30",
                RequireCookie = true,
                RequestClientNameHeader = "67",
                SupportsPoToken = true
            },
            // 3. VISIONOS - Fallback Apple Vision (lấy itag 140 trực tiếp không mã hóa)
            new PlayerClientConfig {
                ClientName = "VISIONOS",
                ClientVersion = "1.02",
                UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15",
                ExtraClientParams = ",\"deviceMake\":\"Apple\",\"deviceModel\":\"RealityDevice14,1\",\"osName\":\"visionOS\",\"osVersion\":\"1.0.2.21O209\",\"timeZone\":\"UTC\",\"utcOffsetMinutes\":0",
                ApiKey = "AIzaSyB-63vPrdThhKuerbB2N_l7Kwwcxj6yUAc",
                RequireCookie = false,
                RequestClientNameHeader = "101"
            },
            // 4. ANDROID_VR (Meta Oculus Quest) - Fallback VR lấy itag 18 và itag 140 trực tiếp
            new PlayerClientConfig {
                ClientName = "ANDROID_VR",
                ClientVersion = "1.65.10",
                UserAgent = "com.google.android.apps.youtube.vr.oculus/1.65.10 (Linux; U; Android 12) gzip",
                ExtraClientParams = ",\"deviceMake\":\"Oculus\",\"deviceModel\":\"Quest 3\",\"osName\":\"Android\",\"osVersion\":\"12\",\"platform\":\"MOBILE\",\"clientFormFactor\":0",
                ApiKey = "AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI",
                RequireCookie = false,
                RequestClientNameHeader = "28"
            },
            // 5. VISIONOS với poToken - Fallback cuối cùng vượt qua BotGuard
            new PlayerClientConfig {
                ClientName = "VISIONOS",
                ClientVersion = "1.02",
                UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15",
                ExtraClientParams = ",\"deviceMake\":\"Apple\",\"deviceModel\":\"RealityDevice14,1\",\"osName\":\"visionOS\",\"osVersion\":\"1.0.2.21O209\",\"timeZone\":\"UTC\",\"utcOffsetMinutes\":0",
                ApiKey = "AIzaSyB-63vPrdThhKuerbB2N_l7Kwwcxj6yUAc",
                RequireCookie = false,
                RequestClientNameHeader = "101",
                SupportsPoToken = true
            }
        };

        public static async Task<string> ResolveStreamUrlAsync(string videoId)
        {
            LastResolveDebug = "";
            if (string.IsNullOrEmpty(videoId) || videoId.StartsWith("LOCAL:") || videoId.StartsWith("CHANNEL:") || videoId.StartsWith("PLAYLIST:"))
                return null;

            // Lấy visitorData giống MetroTube (sw.js_data hoặc homepage)
            string defaultVd = await GetVisitorDataAsync();
            LastResolveDebug = "vd:" + (defaultVd != null ? "OK" : "NULL");

            foreach (var client in _playerClients)
            {
                if (client.RequireCookie && !HasCookieAuth)
                    continue; // Bỏ qua nếu client yêu cầu cookie mà chưa đăng nhập

                string vd = defaultVd;
                string vdField = !string.IsNullOrEmpty(vd) ? ",\"visitorData\":\"" + vd + "\"" : "";

                try
                {
                    LastResolveDebug += " [" + client.ClientName + "]";

                    string poTokenField = "";
                    if (client.SupportsPoToken)
                    {
                        var tokenInfo = await FetchRemotePoTokenAsync(videoId, client.ClientName);
                        if (tokenInfo != null && !string.IsNullOrEmpty(tokenInfo.PoToken))
                        {
                            poTokenField = ",\"serviceIntegrityDimensions\":{\"poToken\":\"" + tokenInfo.PoToken + "\"}";
                            // CRITICAL: Synchronize visitorData with the matching token from Render
                            if (!string.IsNullOrEmpty(tokenInfo.VisitorData))
                            {
                                vd = tokenInfo.VisitorData;
                                vdField = ",\"visitorData\":\"" + vd + "\"";
                            }
                        }
                    }

                    string requestBody = "{" +
                        "\"contentCheckOk\":true," +
                        "\"racyCheckOk\":true," +
                        "\"context\":{\"client\":{" +
                            "\"clientName\":\"" + client.ClientName + "\"," +
                            "\"clientVersion\":\"" + client.ClientVersion + "\"," +
                            "\"userAgent\":\"" + client.UserAgent + "\"," +
                            "\"hl\":\"en\",\"gl\":\"US\"" +
                            vdField +
                            client.ExtraClientParams +
                        "}}" + poTokenField + "," +
                        "\"videoId\":\"" + videoId + "\"" +
                    "}";

                    var req = new HttpRequestMessage(HttpMethod.Post,
                        "https://www.youtube.com/youtubei/v1/player?key=" + client.ApiKey + "&prettyPrint=false&fields=playabilityStatus,streamingData,captions");
                    req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                    req.Headers.TryAddWithoutValidation("User-Agent", client.UserAgent);
                    
                    if (!string.IsNullOrEmpty(client.RequestClientNameHeader))
                        req.Headers.Add("X-YouTube-Client-Name", client.RequestClientNameHeader);
                    
                    req.Headers.Add("X-YouTube-Client-Version", client.ClientVersion);

                    if (HasCookieAuth)
                    {
                        req.Headers.Add("Cookie", _cookieString);
                        req.Headers.Add("Authorization", GenerateSAPISIDHash(_sapisid, "https://www.youtube.com"));
                    }

                    string json;
                    using (var resp = await _client.SendAsync(req))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            LastResolveDebug += " H" + (int)resp.StatusCode;
                            continue;
                        }
                        json = await resp.Content.ReadAsStringAsync();
                    }
                    var data = JObject.Parse(json);

                    string status = data["playabilityStatus"]?["status"]?.ToString() ?? "?";
                    string reason = data["playabilityStatus"]?["reason"]?.ToString() ?? "";
                    LastResolveDebug += " s:" + status;
                    
                    if (status != "OK")
                    {
                        if (!string.IsNullOrEmpty(reason))
                            LastResolveDebug += " r:" + reason.Substring(0, Math.Min(20, reason.Length));
                        continue;
                    }

                    // Cache captions from the first successful response
                    if (string.IsNullOrEmpty(_cachedCaptionsVideoId) || _cachedCaptionsVideoId != videoId)
                    {
                        _cachedCaptionsVideoId = videoId;
                        _cachedCaptionsData = data["captions"];
                    }

                    int[] preferredItags = new[] { 18, 140, 141, 139 };

                    var fmts2 = data["streamingData"]?["formats"];
                    if (fmts2 != null)
                    {
                        foreach (var fmt in fmts2)
                        {
                            int itag = fmt["itag"]?.Value<int>() ?? 0;
                            if (itag == 18)
                            {
                                string url = fmt["url"]?.ToString();
                                if (!string.IsNullOrEmpty(url))
                                {
                                    LastResolveDebug += " i18:OK";
                                    return PrepareStreamUrl(url);
                                }
                            }
                        }
                    }

                    var formats = data["streamingData"]?["adaptiveFormats"];
                    if (formats != null)
                    {
                        foreach (int targetItag in preferredItags)
                        {
                            foreach (var fmt in formats)
                            {
                                int itag = fmt["itag"]?.Value<int>() ?? 0;
                                if (itag == targetItag)
                                {
                                    string url = fmt["url"]?.ToString();
                                    if (!string.IsNullOrEmpty(url))
                                    {
                                        LastResolveDebug += " i" + itag + ":OK";
                                        return PrepareStreamUrl(url);
                                    }
                                }
                            }
                        }
                    }

                    LastResolveDebug += " NOURL";
                }
                catch (Exception ex)
                {
                    LastResolveDebug += " EX:" + ex.Message.Substring(0, Math.Min(25, ex.Message.Length));
                }
            }

            // Fallback to Piped API if all InnerTube clients fail to find a direct URL
            try
            {
                LastResolveDebug += " PIPED";
                string pipedUrl = "https://pipedapi.kavin.rocks/streams/" + videoId;
                var req = new HttpRequestMessage(HttpMethod.Get, pipedUrl);
                using (var resp = await _client.SendAsync(req))
                {
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        var data = JObject.Parse(json);
                        var audioStreams = data["audioStreams"];
                        if (audioStreams != null)
                        {
                            foreach (var stream in audioStreams)
                            {
                                string url = stream["url"]?.ToString();
                                if (!string.IsNullOrEmpty(url))
                                {
                                    LastResolveDebug += " P:OK";
                                    return url; // Piped URLs usually don't need ratebypass
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastResolveDebug += " P_EX:" + ex.Message.Substring(0, Math.Min(25, ex.Message.Length));
            }

            return null;
        }

        /// <summary>
        /// Chuẩn bị URL stream: thêm ratebypass=yes và range=0- để tránh throttle/cut
        /// </summary>
        private static string PrepareStreamUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (!url.Contains("ratebypass="))
                url += "&ratebypass=yes";
            return url;
        }

        // ==========================================
        // HELPERS
        // ==========================================
        private static string CleanChannelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name == "Nghệ sĩ") return "Artist";
            if (name.EndsWith(" - Topic")) return name.Substring(0, name.Length - 8);
            if (name.EndsWith(" - Chủ đề")) return name.Substring(0, name.Length - 9);
            return name;
        }

        // ==========================================
        // CAPTIONS / SUBTITLES
        // ==========================================
        public static async Task<List<CaptionTrack>> GetCaptionTracksAsync(string videoId)
        {
            var tracks = new List<CaptionTrack>();
            try
            {
                // [OPT] Use cached captions from ResolveStreamUrlAsync if available (same videoId)
                JToken captionsNode = null;
                if (_cachedCaptionsVideoId == videoId && _cachedCaptionsData != null)
                {
                    captionsNode = _cachedCaptionsData;
                    System.Diagnostics.Debug.WriteLine("[Captions] Using cached data from player response");
                }
                else
                {
                    // Fallback: make a separate API call (only when cache miss)
                    string vd = await GetVisitorDataAsync();
                    string vdField = !string.IsNullOrEmpty(vd) ? ",\"visitorData\":\"" + vd + "\"" : "";
                    string requestBody = "{" +
                        "\"contentCheckOk\":true," +
                        "\"context\":{\"client\":{" +
                            "\"clientName\":\"ANDROID_VR\"," +
                            "\"clientVersion\":\"1.60.19\"," +
                            "\"deviceMake\":\"Oculus\"," +
                            "\"deviceModel\":\"Quest 3\"," +
                            "\"osName\":\"ANDROID\"," +
                            "\"osVersion\":\"12L\"," +
                            "\"platform\":\"MOBILE\"," +
                            "\"clientScreen\":0," +
                            "\"hl\":\"en\",\"gl\":\"US\"" +
                            vdField +
                        "}}," +
                        "\"videoId\":\"" + videoId + "\"" +
                    "}";

                    var req = new HttpRequestMessage(HttpMethod.Post,
                        "https://www.youtube.com/youtubei/v1/player?key=AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI&prettyPrint=false&fields=captions");
                    req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                    req.Headers.TryAddWithoutValidation("User-Agent",
                        "com.google.android.youtube/20.49.37 (Linux; U; Android 11) gzip");

                    string json;
                    using (var resp = await _client.SendAsync(req))
                    {
                        if (!resp.IsSuccessStatusCode) return tracks;
                        json = await resp.Content.ReadAsStringAsync();
                    }
                    var data = JObject.Parse(json);
                    captionsNode = data?["captions"];
                }

                var captionTracks = captionsNode?["playerCaptionsTracklistRenderer"]?["captionTracks"];
                if (captionTracks != null)
                {
                    foreach (var ct in captionTracks)
                    {
                        var track = new CaptionTrack
                        {
                            BaseUrl = ct["baseUrl"]?.ToString() ?? "",
                            LanguageCode = ct["languageCode"]?.ToString() ?? "",
                            LanguageName = ct["name"]?["simpleText"]?.ToString() ?? ct["name"]?["runs"]?[0]?["text"]?.ToString() ?? ""
                        };
                        if (!string.IsNullOrEmpty(track.BaseUrl))
                            tracks.Add(track);
                    }
                }
            }
            catch { }
            return tracks;
        }

        public static async Task<List<LyricLine>> FetchCaptionTextAsync(string captionUrl)
        {
            var lines = new List<LyricLine>();
            try
            {
                // Request XML format (default)
                string url = captionUrl;
                if (!url.Contains("fmt="))
                    url += "&fmt=srv3";

                string xml;
                using (var resp = await _client.GetAsync(url))
                {
                    if (!resp.IsSuccessStatusCode) return lines;
                    xml = await resp.Content.ReadAsStringAsync();
                }

                // Parse <text start="1.5" dur="3.2">Hello world</text>
                int pos = 0;
                while (pos < xml.Length)
                {
                    int textStart = xml.IndexOf("<text ", pos);
                    if (textStart < 0) break;

                    // Get start attribute
                    int startAttr = xml.IndexOf("start=\"", textStart);
                    if (startAttr < 0) break;
                    int startValBegin = startAttr + 7;
                    int startValEnd = xml.IndexOf("\"", startValBegin);
                    if (startValEnd < 0) break;
                    string startStr = xml.Substring(startValBegin, startValEnd - startValBegin);

                    // Get content
                    int contentStart = xml.IndexOf(">", textStart) + 1;
                    int contentEnd = xml.IndexOf("</text>", contentStart);
                    if (contentEnd < 0) break;

                    string content = xml.Substring(contentStart, contentEnd - contentStart);
                    // Decode HTML entities
                    content = content.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                                     .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("\n", " ");

                    double startSeconds;
                    if (double.TryParse(startStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out startSeconds))
                    {
                        int ms = (int)(startSeconds * 1000);

                        lines.Add(new LyricLine
                        {
                            Time = TimeSpan.FromMilliseconds(ms),
                            Text = content.Trim(),
                            FontSize = 22
                        });
                    }

                    pos = contentEnd + 7;
                }
            }
            catch { }
            return lines;
        }
        /// <summary>
        /// Get video metadata (title, author, thumbnail) via InnerTube player.
        /// Uses ANDROID_VR client with videoDetails field for lightweight response.
        /// </summary>
        public static async Task<Tuple<string, string, string, bool, string>> GetVideoMetadataAsync(string videoId)
        {
            try
            {
                string vd = await GetVisitorDataAsync();
                string vdField = !string.IsNullOrEmpty(vd) ? ",\"visitorData\":\"" + vd + "\"" : "";
                string requestBody = "{" +
                    "\"contentCheckOk\":true," +
                    "\"context\":{\"client\":{" +
                        "\"clientName\":\"ANDROID_VR\"," +
                        "\"clientVersion\":\"1.60.19\"," +
                        "\"deviceMake\":\"Oculus\"," +
                        "\"deviceModel\":\"Quest 3\"," +
                        "\"osName\":\"ANDROID\"," +
                        "\"osVersion\":\"12L\"," +
                        "\"platform\":\"MOBILE\"," +
                        "\"clientScreen\":0," +
                        "\"hl\":\"en\",\"gl\":\"US\"" +
                        vdField +
                    "}}," +
                    "\"videoId\":\"" + videoId + "\"" +
                "}";

                var req = new HttpRequestMessage(HttpMethod.Post,
                    "https://www.youtube.com/youtubei/v1/player?key=AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI&prettyPrint=false&fields=videoDetails,microformat");
                req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("User-Agent",
                    "com.google.android.youtube/20.49.37 (Linux; U; Android 11) gzip");

                string json;
                using (var resp = await _client.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode)
                        return new Tuple<string, string, string, bool, string>("", "", "", false, "");
                    json = await resp.Content.ReadAsStringAsync();
                }
                var data = JObject.Parse(json);

                var details = data["videoDetails"];
                string title = details?["title"]?.ToString() ?? "";
                string author = details?["author"]?.ToString() ?? "";
                string channelId = details?["channelId"]?.ToString() ?? "";
                string thumbUrl = details?.SelectToken("thumbnail.thumbnails[-1:].url")?.ToString()
                    ?? details?.SelectToken("thumbnail.thumbnails[0].url")?.ToString() ?? "";

                // Strict filter: only YouTube Music audio tracks (ATV)
                // MUSIC_VIDEO_TYPE_ATV = official audio track (song on YouTube Music)
                // Rejects: OMV (music videos), UGC (user content), regular YouTube videos
                bool isMusic = false;
                string musicVideoType = details?["musicVideoType"]?.ToString() ?? "";
                if (musicVideoType == "MUSIC_VIDEO_TYPE_ATV")
                    isMusic = true;

                // Also accept Topic channel tracks (auto-generated YouTube Music content)
                if (!isMusic)
                {
                    string ch = author ?? "";
                    if (ch.EndsWith(" - Topic") || ch.EndsWith(" - Chủ đề"))
                        isMusic = true;
                }

                // If thumbUrl is a YouTube video thumbnail (16:9), fetch true 1:1 square album art from YTM
                if (isMusic && !string.IsNullOrEmpty(title) && (thumbUrl.Contains("i.ytimg.com") || !thumbUrl.Contains("googleusercontent.com")))
                {
                    string squareArt = await GetSquareArtworkForTrackAsync(title, author, thumbUrl);
                    if (!string.IsNullOrEmpty(squareArt) && squareArt.Contains("googleusercontent.com"))
                    {
                        thumbUrl = squareArt;
                    }
                }

                return new Tuple<string, string, string, bool, string>(title, author, thumbUrl, isMusic, channelId);
            }
            catch
            {
                return new Tuple<string, string, string, bool, string>("", "", "", false, "");
            }
        }

        /// <summary>
        /// Search YouTube Music for genuine 1:1 square album art (googleusercontent.com).
        /// </summary>
        public static async Task<string> GetSquareArtworkForTrackAsync(string title, string artist, string fallbackUrl = "")
        {
            if (string.IsNullOrEmpty(title)) return fallbackUrl;

            try
            {
                string query = string.IsNullOrEmpty(artist) ? title : (title + " " + artist);
                var req = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/search?prettyPrint=false");
                var bodyObj = new JObject
                {
                    ["context"] = new JObject
                    {
                        ["client"] = new JObject
                        {
                            ["clientName"] = "WEB_REMIX",
                            ["clientVersion"] = "1.20241016.01.00",
                            ["hl"] = CurrentLanguage,
                            ["gl"] = CurrentRegion
                        }
                    },
                    ["query"] = query
                };

                req.Content = new StringContent(bodyObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
                req.Headers.Add("Origin", "https://music.youtube.com");
                req.Headers.Add("Referer", "https://music.youtube.com/");

                string json;
                using (var resp = await _client.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return fallbackUrl;
                    json = await resp.Content.ReadAsStringAsync();
                }
                int idx = json.IndexOf("googleusercontent.com");
                if (idx != -1)
                {
                    int start = json.LastIndexOf("http", idx);
                    int end = json.IndexOf("\"", idx);
                    if (start != -1 && end != -1)
                    {
                        string u = json.Substring(start, end - start);
                        int eq = u.LastIndexOf("=");
                        if (eq > 0)
                            return u.Substring(0, eq) + "=w480-h480-l90-rj";
                        return u + "=w480-h480-l90-rj";
                    }
                }
            }
            catch { }

            return fallbackUrl;
        }

        /// <summary>
        /// Fetch algorithmic radio tracks for a video from YouTube Music /next endpoint.
        /// </summary>
        public static async Task<List<YouTubeTrack>> GetRadioTracksAsync(string videoId)
        {
            var list = new List<YouTubeTrack>();
            if (string.IsNullOrEmpty(videoId) || videoId.StartsWith("LOCAL:")) return list;

            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["videoId"] = videoId,
                    ["playlistId"] = "RDAMVM" + videoId,
                    ["isAudioOnly"] = true
                };

                string apiUrl = "https://music.youtube.com/youtubei/v1/next?prettyPrint=false";
                var data = await PostInnerTubeAsync(apiUrl, body, true);
                if (data == null) return list;

                var items = data.SelectToken("$..playlistPanelRenderer.contents") as JArray;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var renderer = item["playlistPanelVideoRenderer"];
                        if (renderer != null)
                        {
                            string vid = renderer["videoId"]?.ToString();
                            string title = renderer.SelectToken("title.runs[0].text")?.ToString() ?? "";

                            string artist = "";
                            var bylineRuns = renderer.SelectToken("shortBylineText.runs") as JArray
                                         ?? renderer.SelectToken("longBylineText.runs") as JArray;
                            if (bylineRuns != null && bylineRuns.Count > 0)
                            {
                                var artistList = new List<string>();
                                foreach (var run in bylineRuns)
                                {
                                    string text = run["text"]?.ToString();
                                    if (!string.IsNullOrWhiteSpace(text) && text != " • " && !text.Contains("views") && !text.Contains("likes"))
                                    {
                                        artistList.Add(text.Trim());
                                    }
                                }
                                artist = artistList.Count > 0 ? string.Join(", ", artistList) : bylineRuns[0]["text"]?.ToString() ?? "";
                            }

                            string thumb = "";
                            var thumbs = renderer.SelectToken("thumbnail.thumbnails") as JArray;
                            if (thumbs != null && thumbs.Count > 0)
                            {
                                thumb = thumbs[thumbs.Count - 1]["url"]?.ToString() ?? "";
                            }

                            if (!string.IsNullOrEmpty(vid) && !string.IsNullOrEmpty(title))
                            {
                                list.Add(new YouTubeTrack
                                {
                                    VideoId = vid,
                                    Title = title,
                                    ChannelName = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                                    ThumbnailUrl = thumb
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[InnerTube] GetRadioTracksAsync error: " + ex.Message);
            }

            return list;
        }

        /// <summary>
        /// Retrieves song credits (Performed by, Written by, Produced by, Source) and detailed metadata.
        /// </summary>
        public static async Task<SongCredits> GetSongCreditsAsync(string videoId, string title, string artist, string creditsBrowseId = null, string albumName = null)
        {
            var credits = new SongCredits
            {
                Title = title ?? "",
                Artist = artist ?? "",
                Album = !string.IsNullOrEmpty(albumName) ? albumName : "Single / Album",
                HasCredits = false
            };

            if (string.IsNullOrEmpty(videoId) || videoId.StartsWith("LOCAL:"))
            {
                credits.AudioFormat = "Local Audio File";
                return credits;
            }

            try
            {
                // 1. If creditsBrowseId is missing, try to search for the track to obtain its MPTC browseId
                if (string.IsNullOrEmpty(creditsBrowseId))
                {
                    try
                    {
                        string q = string.IsNullOrEmpty(artist) ? title : (title + " " + artist);
                        string vd = await GetVisitorDataAsync();
                        var searchBody = new JObject
                        {
                            ["context"] = BuildMusicContext(vd),
                            ["query"] = q
                        };
                        var searchData = await PostInnerTubeAsync("https://music.youtube.com/youtubei/v1/search?prettyPrint=false", searchBody, true);
                        var allItems = searchData?.SelectTokens("$..musicResponsiveListItemRenderer");
                        if (allItems != null)
                        {
                            foreach (var item in allItems)
                            {
                                var menuItems = item["menu"]?["menuRenderer"]?["items"];
                                if (menuItems != null)
                                {
                                    foreach (var mi in menuItems)
                                    {
                                        string bId = mi["menuNavigationItemRenderer"]?["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                                        if (!string.IsNullOrEmpty(bId) && bId.StartsWith("MPTC"))
                                        {
                                            creditsBrowseId = bId;
                                            break;
                                        }
                                    }
                                }
                                if (!string.IsNullOrEmpty(creditsBrowseId)) break;
                            }
                        }
                    }
                    catch { }
                }

                // 2. If we have a creditsBrowseId (MPTC...), fetch the official song credits dialog
                if (!string.IsNullOrEmpty(creditsBrowseId))
                {
                    try
                    {
                        string vd = await GetVisitorDataAsync();
                        var browseBody = new JObject
                        {
                            ["context"] = BuildMusicContext(vd),
                            ["browseId"] = creditsBrowseId
                        };
                        var browseData = await PostInnerTubeAsync("https://music.youtube.com/youtubei/v1/browse?prettyPrint=false", browseBody, true);
                        var sections = browseData?.SelectTokens("$..dismissableDialogContentSectionRenderer");
                        if (sections != null)
                        {
                            foreach (var sec in sections)
                            {
                                string secTitle = sec["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                var subRuns = sec["subtitle"]?["runs"];
                                string secContent = "";
                                if (subRuns != null)
                                {
                                    foreach (var r in subRuns)
                                    {
                                        secContent += r["text"]?.ToString();
                                    }
                                }
                                secContent = secContent.Trim();

                                if (secTitle.IndexOf("Performed", StringComparison.OrdinalIgnoreCase) >= 0)
                                    credits.PerformedBy = secContent;
                                else if (secTitle.IndexOf("Written", StringComparison.OrdinalIgnoreCase) >= 0)
                                    credits.WrittenBy = secContent;
                                else if (secTitle.IndexOf("Produced", StringComparison.OrdinalIgnoreCase) >= 0)
                                    credits.ProducedBy = secContent;
                                else if (secTitle.IndexOf("Source", StringComparison.OrdinalIgnoreCase) >= 0 || secTitle.IndexOf("Provided", StringComparison.OrdinalIgnoreCase) >= 0)
                                    credits.ProvidedBy = secContent;
                            }
                        }
                        if (!string.IsNullOrEmpty(credits.PerformedBy) || !string.IsNullOrEmpty(credits.WrittenBy) || !string.IsNullOrEmpty(credits.ProvidedBy))
                        {
                            credits.HasCredits = true;
                        }
                    }
                    catch { }
                }

                // 3. Supplement with video details (Plays / Views, Publish Date, Format)
                try
                {
                    string vd = await GetVisitorDataAsync();
                    string vdField = !string.IsNullOrEmpty(vd) ? ",\"visitorData\":\"" + vd + "\"" : "";
                    string reqBody = "{\"contentCheckOk\":true,\"context\":{\"client\":{\"clientName\":\"WEB_REMIX\",\"clientVersion\":\"1.20260304.03.00\",\"hl\":\"en\",\"gl\":\"US\"" + vdField + "}},\"videoId\":\"" + videoId + "\"}";
                    var req = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/player?prettyPrint=false&fields=videoDetails,microformat");
                    req.Content = new StringContent(reqBody, Encoding.UTF8, "application/json");
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0");
                    req.Headers.TryAddWithoutValidation("Referer", "https://music.youtube.com/");

                    using (var resp = await _client.SendAsync(req))
                    {
                        if (resp.IsSuccessStatusCode)
                        {
                            string json = await resp.Content.ReadAsStringAsync();
                            var data = JObject.Parse(json);
                            var details = data["videoDetails"];
                            if (details != null)
                            {
                                string vc = details["viewCount"]?.ToString();
                                if (!string.IsNullOrEmpty(vc))
                                {
                                    long viewNum;
                                    if (long.TryParse(vc, out viewNum))
                                    {
                                        if (viewNum >= 1000000000)
                                            credits.ViewCount = (viewNum / 1000000000.0).ToString("0.#") + "B plays";
                                        else if (viewNum >= 1000000)
                                            credits.ViewCount = (viewNum / 1000000.0).ToString("0.#") + "M plays";
                                        else if (viewNum >= 1000)
                                            credits.ViewCount = (viewNum / 1000.0).ToString("0.#") + "K plays";
                                        else
                                            credits.ViewCount = viewNum.ToString("N0") + " plays";
                                    }
                                }

                                string pubDate = data["microformat"]?["microformatDataRenderer"]?["publishDate"]?.ToString()
                                    ?? data["microformat"]?["microformatDataRenderer"]?["uploadDate"]?.ToString();
                                if (!string.IsNullOrEmpty(pubDate))
                                {
                                    DateTime dt;
                                    if (DateTime.TryParse(pubDate, out dt))
                                    {
                                        credits.PublishDate = dt.ToString("MMM d, yyyy");
                                    }
                                    else
                                    {
                                        credits.PublishDate = pubDate;
                                    }
                                }

                                if (string.IsNullOrEmpty(credits.PerformedBy))
                                {
                                    credits.PerformedBy = details["author"]?.ToString() ?? artist;
                                }
                            }
                        }
                    }
                }
                catch { }

                credits.AudioFormat = "Opus / AAC 128-256 kbps";
            }
            catch { }

            return credits;
        }
    }

    public class CaptionTrack
    {
        public string BaseUrl { get; set; } = "";
        public string LanguageCode { get; set; } = "";
        public string LanguageName { get; set; } = "";
    }
}
