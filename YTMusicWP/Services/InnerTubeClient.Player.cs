using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace YTMusicWP
{
    public static partial class InnerTubeClient
    {
        public static string LastResolveDebug = "";

        public static async Task<string> ResolveStreamUrlAsync(string videoId)
        {
            LastResolveDebug = "";
            if (string.IsNullOrEmpty(videoId) || videoId.StartsWith("LOCAL:") || videoId.StartsWith("CHANNEL:") || videoId.StartsWith("PLAYLIST:"))
                return null;

            // Lấy visitorData giống MetroTube (sw.js_data hoặc homepage)
            string vd = await GetVisitorDataAsync();
            LastResolveDebug = "vd:" + (vd != null ? "OK" : "NULL");

            // Giống MetroTube: chỉ dùng ANDROID_VR (Oculus Quest 3)
            try
            {
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

                    // Giống MetroTube: thêm &fields= để giảm response size + chỉ lấy cần thiết
                    var req = new HttpRequestMessage(HttpMethod.Post,
                        "https://www.youtube.com/youtubei/v1/player?key=AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI&prettyPrint=false&fields=playabilityStatus,streamingData,captions");
                    req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                    req.Headers.Add("User-Agent",
                        "com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip");
                    req.Headers.Add("X-YouTube-Client-Name", "28");
                    req.Headers.Add("X-YouTube-Client-Version", "1.60.19");

                    var resp = await _client.SendAsync(req);
                    if (!resp.IsSuccessStatusCode)
                    {
                        LastResolveDebug += " H" + (int)resp.StatusCode;
                        return null;
                    }

                    string json = await resp.Content.ReadAsStringAsync();
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

                    // Select itag based on Audio Quality setting
                    // Low=48kbps (itag 139), Normal=128kbps (itag 140), High=256kbps (itag 251/141)
                    var qualitySettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                    string qualityKbps = qualitySettings.ContainsKey("AudioQualityKbps") ? qualitySettings["AudioQualityKbps"].ToString() : "128";
                    int[] preferredItags;
                    if (qualityKbps == "48") preferredItags = new[] { 139, 140, 18 };        // Low: 48kbps first
                    else if (qualityKbps == "256") preferredItags = new[] { 251, 141, 140, 18 }; // High: 256kbps opus/m4a
                    else preferredItags = new[] { 140, 139, 18 };                            // Normal: 128kbps (default)

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
                                        LastResolveDebug += " i" + itag + ":OK(q" + qualityKbps + ")";
                                        return PrepareStreamUrl(url);
                                    }
                                }
                            }
                        }
                    }

                    // Fallback: formats (itag 18 = video+audio 360p)
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

                    // Status OK nhưng không có URL
                    LastResolveDebug += " NOURL";
            }
            catch (Exception ex)
            {
                    LastResolveDebug += " EX:" + ex.Message.Substring(0, Math.Min(25, ex.Message.Length));
            }

            // ========================================
            // FALLBACK: Invidious (API + Embed proxy)
            // Dùng HttpClient riêng với timeout 5s, chạy song song
            // ========================================
            try
            {
                string[] invInstances = new[] { "yewtu.be", "iv.duti.dev", "invidious.schenkel.eti.br", "inv.nadeko.net" };
                
                var invClient = new HttpClient();
                invClient.Timeout = TimeSpan.FromSeconds(5);
                invClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                // Chạy song song: tất cả API + Embed requests cùng lúc
                var tasks = new List<Task<string>>();
                foreach (var inst in invInstances)
                {
                    // Invidious API
                    tasks.Add(TryInvidiousApiAsync(invClient, inst, videoId));
                    // Invidious Embed (proxy qua server, bypass geo-block)
                    tasks.Add(TryInvidiousEmbedAsync(invClient, inst, videoId));
                }

                // Đợi task đầu tiên hoàn thành thành công
                while (tasks.Count > 0)
                {
                    var completed = await Task.WhenAny(tasks);
                    tasks.Remove(completed);
                    try
                    {
                        string result = await completed;
                        if (!string.IsNullOrEmpty(result))
                        {
                            return result;
                        }
                    }
                    catch { }
                }
                LastResolveDebug += " inv:ALL_FAIL";
            }
            catch (Exception ex)
            {
                LastResolveDebug += " inv:EX:" + ex.Message.Substring(0, Math.Min(15, ex.Message.Length));
            }

            return null;
        }

        /// <summary>
        /// Invidious API: /api/v1/videos/{id} → parse JSON → itag 140 URL
        /// </summary>
        private static async Task<string> TryInvidiousApiAsync(HttpClient client, string instance, string videoId)
        {
            try
            {
                var resp = await client.GetAsync("https://" + instance + "/api/v1/videos/" + videoId + "?fields=adaptiveFormats,formatStreams");
                if (!resp.IsSuccessStatusCode) return null;
                
                string json = await resp.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                
                var adaptiveFormats = data["adaptiveFormats"] as JArray;
                if (adaptiveFormats != null)
                {
                    foreach (var fmt in adaptiveFormats)
                    {
                        string itagStr = fmt["itag"]?.ToString();
                        if (itagStr == "140" || itagStr == "139")
                        {
                            string url = fmt["url"]?.ToString();
                            if (!string.IsNullOrEmpty(url))
                            {
                                LastResolveDebug += " api:" + instance.Substring(0, Math.Min(6, instance.Length));
                                return PrepareStreamUrl(url);
                            }
                        }
                    }
                }
                
                var fmtStreams = data["formatStreams"] as JArray;
                if (fmtStreams != null)
                {
                    foreach (var fmt in fmtStreams)
                    {
                        if (fmt["itag"]?.ToString() == "18")
                        {
                            string url = fmt["url"]?.ToString();
                            if (!string.IsNullOrEmpty(url))
                            {
                                LastResolveDebug += " api18:" + instance.Substring(0, Math.Min(6, instance.Length));
                                return PrepareStreamUrl(url);
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Invidious Embed: /embed/{id}?local=1 → parse HTML source tags
        /// Stream proxy qua Invidious server → bypass geo-block
        /// </summary>
        private static async Task<string> TryInvidiousEmbedAsync(HttpClient client, string instance, string videoId)
        {
            try
            {
                var resp = await client.GetAsync("https://" + instance + "/embed/" + videoId + "?local=1");
                if (!resp.IsSuccessStatusCode) return null;
                
                string html = await resp.Content.ReadAsStringAsync();
                
                // Tìm <source src="..." type="audio/mp4"> cho itag 140
                // Pattern: <source src="/videoplayback?...itag=140...&local=true..." type="audio/mp4"
                string[] priorities = new[] { "itag=140", "itag=139", "itag=18" };
                foreach (var itagSearch in priorities)
                {
                    int srcIdx = html.IndexOf(itagSearch);
                    if (srcIdx < 0) continue;
                    
                    // Tìm lùi lại <source src="
                    int tagStart = html.LastIndexOf("<source", srcIdx);
                    if (tagStart < 0) continue;
                    
                    int srcAttr = html.IndexOf("src=\"", tagStart);
                    if (srcAttr < 0 || srcAttr > srcIdx + 20) continue;
                    
                    int urlStart = srcAttr + 5;
                    int urlEnd = html.IndexOf("\"", urlStart);
                    if (urlEnd <= urlStart) continue;
                    
                    string rawUrl = html.Substring(urlStart, urlEnd - urlStart);
                    // Decode HTML entities
                    rawUrl = rawUrl.Replace("&amp;", "&");
                    
                    // Nếu URL bắt đầu bằng / → thêm scheme+host
                    if (rawUrl.StartsWith("/"))
                        rawUrl = "https://" + instance + rawUrl;
                    
                    if (!string.IsNullOrEmpty(rawUrl))
                    {
                        LastResolveDebug += " emb:" + instance.Substring(0, Math.Min(6, instance.Length));
                        return rawUrl; // KHÔNG PrepareStreamUrl vì URL này đã qua proxy
                    }
                }
            }
            catch { }
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
            if (!url.Contains("range="))
                url += "&range=0-";
            return url;
        }

        // ==========================================
        // HELPERS
        // ==========================================
        private static string CleanChannelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
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
                req.Headers.Add("User-Agent",
                    "com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip");

                var resp = await _client.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return tracks;

                string json = await resp.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                var captionTracks = data?["captions"]?["playerCaptionsTracklistRenderer"]?["captionTracks"];
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

                var resp = await _client.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return lines;

                string xml = await resp.Content.ReadAsStringAsync();

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
    }

    public class CaptionTrack
    {
        public string BaseUrl { get; set; } = "";
        public string LanguageCode { get; set; } = "";
        public string LanguageName { get; set; } = "";
    }
}
