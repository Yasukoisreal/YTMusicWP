using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace YTMusicWP
{
    /// <summary>
    /// InnerTube API client — gọi trực tiếp YouTube Music/YouTube API từ phone.
    /// Không cần proxy, API key, hay backend.
    /// </summary>
    public static partial class InnerTubeClient
    {
        private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        private static string _cachedVisitorData = null;
        private static DateTime _vdCacheTime = DateTime.MinValue;

        // Region & Language — set from app settings on startup
        public static string CurrentRegion { get; set; } = "US";
        public static string CurrentLanguage { get; set; } = "en";

        public static void SetRegion(string regionCode)
        {
            CurrentRegion = regionCode ?? "US";
            switch (CurrentRegion)
            {
                case "VN": CurrentLanguage = "vi"; break;
                case "KR": CurrentLanguage = "ko"; break;
                case "JP": CurrentLanguage = "ja"; break;
                case "TW": CurrentLanguage = "zh-TW"; break;
                case "TH": CurrentLanguage = "th"; break;
                case "ID": CurrentLanguage = "id"; break;
                case "FR": CurrentLanguage = "fr"; break;
                case "DE": CurrentLanguage = "de"; break;
                case "ES": CurrentLanguage = "es"; break;
                case "BR": CurrentLanguage = "pt"; break;
                case "RU": CurrentLanguage = "ru"; break;
                default: CurrentLanguage = "en"; break;
            }
        }

        // ==========================================
        // VISITOR DATA
        // ==========================================
        public static async Task<string> GetVisitorDataAsync()
        {
            // Cache 2 hours (visitorData doesn't change often)
            if (_cachedVisitorData != null && (DateTime.Now - _vdCacheTime).TotalMinutes < 120)
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
                        _vdCacheTime = DateTime.Now;
                        return savedVd;
                    }
                }
            }
            catch { }

            try
            {
                // Use lightweight sw.js_data endpoint instead of full homepage (~500KB → ~2KB)
                var request = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/sw.js_data");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Linux; Andr0id 9; BRAVIA 8K UR2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.125 Safari/537.36 OPR/46.0.2207.0 OMI/4.21.0.273.DIA6.149 Model/Sony-BRAVIA-8K-UR2,gzip(gfe)");
                using (var resp = await _client.SendAsync(request))
                {
                    if (resp.IsSuccessStatusCode)
                    {
                        string body = await resp.Content.ReadAsStringAsync();
                        string vd = ExtractVisitorData(body);
                        if (!string.IsNullOrEmpty(vd))
                        {
                            _cachedVisitorData = vd;
                            _vdCacheTime = DateTime.Now;
                            try { Windows.Storage.ApplicationData.Current.LocalSettings.Values["CachedVisitorData"] = vd; } catch { }
                            return vd;
                        }
                    }
                }
            }
            catch { }

            return _cachedVisitorData;
        }

        private static string ExtractVisitorData(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // 1. Tìm visitorData":"CgXXX"
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

            // 2. Tìm "CgXXX" trong array format của sw.js_data
            int searchPos = 0;
            while (searchPos < text.Length)
            {
                int quotePos = text.IndexOf("\"Cg", searchPos);
                if (quotePos < 0) break;

                int start2 = quotePos + 1;
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

        // ==========================================
        // CONTEXT BUILDERS
        // ==========================================
        public static JObject BuildMusicContext(string visitorData)
        {
            var client = new JObject
            {
                ["clientName"] = "WEB_REMIX",
                ["clientVersion"] = "1.20241016.01.00",
                ["hl"] = CurrentLanguage,
                ["gl"] = CurrentRegion
            };
            if (!string.IsNullOrEmpty(visitorData))
                client["visitorData"] = visitorData;

            return new JObject { ["client"] = client };
        }

        private static JObject BuildWebContext(string visitorData)
        {
            var client = new JObject
            {
                ["clientName"] = "WEB",
                ["clientVersion"] = "2.20241016.00.00",
                ["hl"] = CurrentLanguage,
                ["gl"] = CurrentRegion
            };
            if (!string.IsNullOrEmpty(visitorData))
                client["visitorData"] = visitorData;

            return new JObject { ["client"] = client };
        }

        public static async Task<JObject> PostInnerTubeAsync(string url, JObject body, bool isMusic = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
            if (isMusic)
            {
                request.Headers.Add("Origin", "https://music.youtube.com");
                request.Headers.Add("Referer", "https://music.youtube.com/");
            }

            using (var resp = await _client.SendAsync(request))
            {
                using (var stream = await resp.Content.ReadAsStreamAsync())
                using (var reader = new System.IO.StreamReader(stream))
                using (var jsonReader = new Newtonsoft.Json.JsonTextReader(reader))
                {
                    return JObject.Load(jsonReader);
                }
            }
        }

        // ==========================================
        // SEARCH
        // ==========================================
        /// <summary>
        /// Tìm kiếm bài hát qua InnerTube (YouTube Music).
        /// Trả về danh sách YouTubeTrack.
        /// </summary>

    }

    // ==========================================
    public class PlaylistResult
    {
        public string Title { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
        public List<YouTubeTrack> Tracks { get; set; } = new List<YouTubeTrack>();
    }

    public class ArtistResult
    {
        public string Name { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public string SubscriberCount { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsYouTubeMusicArtist { get; set; } = false;
        public List<YouTubeTrack> Tracks { get; set; } = new List<YouTubeTrack>();
        public List<ArtistAlbum> Albums { get; set; } = new List<ArtistAlbum>();
    }

    public class ArtistAlbum
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
        public string BrowseId { get; set; } = "";
        public string SectionTitle { get; set; } = "";
    }

    public class DiscoverItem
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
        public string VideoId { get; set; } = "";
        public string PlaylistId { get; set; } = "";
        public string SearchQuery { get; set; } = "";
    }
}
