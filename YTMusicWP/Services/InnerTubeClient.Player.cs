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
        public static string LastResolveDebug = "";

        // [OPT] Cache captions from player response — avoids duplicate InnerTube call in GetCaptionTracksAsync
        private static string _cachedCaptionsVideoId;
        private static JToken _cachedCaptionsData;

        public static async Task<string> ResolveStreamUrlAsync(string videoId)
        {
            LastResolveDebug = "";
            if (string.IsNullOrEmpty(videoId) || videoId.StartsWith("LOCAL:") || videoId.StartsWith("CHANNEL:") || videoId.StartsWith("PLAYLIST:"))
                return null;

            // Lấy visitorData giống MetroTube (sw.js_data hoặc homepage)
            string vd = await GetVisitorDataAsync();
            LastResolveDebug = "vd:" + (vd != null ? "OK" : "NULL");

            // ANDROID_VR — returns direct URLs (no signatureCipher) + BRAVIA visitorData
            try
            {
                    string vdField = !string.IsNullOrEmpty(vd) ? ",\"visitorData\":\"" + vd + "\"" : "";
                    string requestBody = "{" +
                        "\"contentCheckOk\":true," +
                        "\"context\":{\"client\":{" +
                            "\"clientName\":\"ANDROID\"," +
                            "\"clientVersion\":\"20.49.37\"," +
                            "\"deviceMake\":\"Nokia\"," +
                            "\"deviceModel\":\"LumiaWP\"," +
                            "\"userAgent\":\"com.google.android.youtube/20.49.37 (Linux; U; Android 11) gzip\"," +
                            "\"osName\":\"Android\"," +
                            "\"osVersion\":\"11\"," +
                            "\"platform\":\"MOBILE\"," +
                            "\"androidSdkVersion\":30," +
                            "\"clientFormFactor\":0," +
                            "\"hl\":\"en\",\"gl\":\"US\"" +
                            vdField +
                        "}}," +
                        "\"videoId\":\"" + videoId + "\"" +
                    "}";

                    var req = new HttpRequestMessage(HttpMethod.Post,
                        "https://www.youtube.com/youtubei/v1/player?key=AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI&prettyPrint=false&fields=playabilityStatus,streamingData,captions");
                    req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                    req.Headers.TryAddWithoutValidation("User-Agent",
                        "com.google.android.youtube/20.49.37 (Linux; U; Android 11) gzip");
                    req.Headers.Add("X-YouTube-Client-Name", "3");
                    req.Headers.Add("X-YouTube-Client-Version", "20.49.37");

                    string json;
                    using (var resp = await _client.SendAsync(req))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            LastResolveDebug += " H" + (int)resp.StatusCode;
                            return null;
                        }
                        json = await resp.Content.ReadAsStringAsync();
                    }
                    LastResolveDebug += " len:" + json.Length;
                    var data = JObject.Parse(json);

                    string status = data["playabilityStatus"]?["status"]?.ToString() ?? "?";
                    string reason = data["playabilityStatus"]?["reason"]?.ToString() ?? "";
                    LastResolveDebug += " s:" + status;
                    
                    if (status != "OK")
                    {
                        if (!string.IsNullOrEmpty(reason))
                            LastResolveDebug += " r:" + reason.Substring(0, Math.Min(20, reason.Length));
                        return null;
                    }

                    // [OPT] Cache captions from this response — avoids duplicate API call in GetCaptionTracksAsync
                    _cachedCaptionsVideoId = videoId;
                    _cachedCaptionsData = data["captions"];

                    int[] preferredItags = new[] { 18, 140, 141, 139 };

                    // 1. Ưu tiên itag 18 (video 360p) vì nó không bị bóp băng thông
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

                    // 2. Fallback xuống adaptiveFormats (có thể bị bóp/403)
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

                    // Status OK nhưng không có URL
                    LastResolveDebug += " NOURL";
            }
            catch (Exception ex)
            {
                    LastResolveDebug += " EX:" + ex.Message.Substring(0, Math.Min(25, ex.Message.Length));
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
    }

    public class CaptionTrack
    {
        public string BaseUrl { get; set; } = "";
        public string LanguageCode { get; set; } = "";
        public string LanguageName { get; set; } = "";
    }
}
