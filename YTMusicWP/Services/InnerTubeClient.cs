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

        // ==========================================
        // VISITOR DATA
        // ==========================================
        public static async Task<string> GetVisitorDataAsync()
        {
            // Cache 30 phút
            if (_cachedVisitorData != null && (DateTime.Now - _vdCacheTime).TotalMinutes < 30)
                return _cachedVisitorData;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
                var resp = await _client.SendAsync(request);
                if (resp.IsSuccessStatusCode)
                {
                    string html = await resp.Content.ReadAsStringAsync();
                    string marker = "\"visitorData\":\"";
                    int idx = html.IndexOf(marker);
                    if (idx >= 0)
                    {
                        int start = idx + marker.Length;
                        int end = html.IndexOf("\"", start);
                        if (end > start && end - start >= 20 && end - start < 600)
                        {
                            string vd = html.Substring(start, end - start);
                            if (vd.StartsWith("Cg"))
                            {
                                _cachedVisitorData = vd;
                                _vdCacheTime = DateTime.Now;
                                return vd;
                            }
                        }
                    }
                }
            }
            catch { }
            return _cachedVisitorData; // Return old cache if fetch fails
        }

        // ==========================================
        // CONTEXT BUILDERS
        // ==========================================
        private static JObject BuildMusicContext(string visitorData)
        {
            var client = new JObject
            {
                ["clientName"] = "WEB_REMIX",
                ["clientVersion"] = "1.20241016.01.00",
                ["hl"] = "en",
                ["gl"] = "US"
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
                ["hl"] = "en",
                ["gl"] = "US"
            };
            if (!string.IsNullOrEmpty(visitorData))
                client["visitorData"] = visitorData;

            return new JObject { ["client"] = client };
        }

        private static async Task<JObject> PostInnerTubeAsync(string url, JObject body, bool isMusic = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
            if (isMusic)
            {
                request.Headers.Add("Origin", "https://music.youtube.com");
                request.Headers.Add("Referer", "https://music.youtube.com/");
            }

            var resp = await _client.SendAsync(request);
            string json = await resp.Content.ReadAsStringAsync();
            return JObject.Parse(json);
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
}
