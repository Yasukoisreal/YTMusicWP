using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {

        private static string CleanChannelName(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return channel;
            if (channel == "Nghệ sĩ") return "Artist";
            if (channel.EndsWith(" - Topic")) return channel.Substring(0, channel.Length - 8);
            if (channel.EndsWith(" - Chủ đề")) return channel.Substring(0, channel.Length - 9);
            return channel;
        }

        private static string GetHighResThumbnail(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            // [OPT-M7] Giảm 1080→480 cho ảnh bìa — tiết kiệm ~4x RAM khi decode trên thiết bị 512MB
            if (url.Contains("=w120-h120"))
                return url.Replace("=w120-h120", "=w480-h480");
            if (url.Contains("=w60-h60"))
                return url.Replace("=w60-h60", "=w480-h480");
            if (url.Contains("=w226-h226"))
                return url.Replace("=w226-h226", "=w480-h480");
            if (url.Contains("hqdefault.jpg"))
                return url; // giữ hqdefault (480x360), ko dùng maxresdefault (1280x720)
            if (url.Contains("mqdefault.jpg"))
                return url.Replace("mqdefault.jpg", "hqdefault.jpg");
            if (url.Contains("-s120-"))
                return url.Replace("-s120-", "-s480-");
            if (url.Contains("-s68-"))
                return url.Replace("-s68-", "-s480-");
            return url;
        }

        /// <summary>
        /// Get properly-sized avatar for artist display (72dp circles).
        /// Requests 176px (72dp × ~2.5x for sharp rendering on high DPI).
        /// </summary>
        private static string GetArtistAvatar(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            // Google CDN (lh3/yt3) — request square at exact size, no center crop
            if (url.Contains("lh3.googleusercontent.com") || url.Contains("yt3.ggpht.com"))
            {
                int eqIdx = url.LastIndexOf("=");
                if (eqIdx > 0)
                    return url.Substring(0, eqIdx) + "=s176-k-no-rj";
                return url + "=s176-k-no-rj";
            }
            return url;
        }

        /// <summary>
        /// Get best thumbnail for 1:1 square display (playlist covers).
        /// YouTube Music thumbnails (lh3.googleusercontent.com) → request 1:1 crop.
        /// YouTube thumbnails (i.ytimg.com) → use mqdefault (16:9, no letterbox bars).
        /// </summary>
        private static string GetSquareThumbnail(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            // YouTube Music thumbnails — request square crop from CDN
            if (url.Contains("lh3.googleusercontent.com") || url.Contains("yt3.ggpht.com"))
            {
                // Remove any existing size params and request square
                int eqIdx = url.LastIndexOf("=");
                if (eqIdx > 0)
                    return url.Substring(0, eqIdx) + "=w480-h480-l90-rj";
                return url + "=w480-h480-l90-rj";
            }
            // YouTube video thumbnails — use sddefault (true 16:9, no black bars)
            // hqdefault is 480x360 (4:3 with black bars baked in!) — BAD for square crop
            // sddefault is 640x480 (4:3 but most videos actually fill 16:9 area)
            // Use hqdefault but rely on UniformToFill to crop center
            return url;
        }

        /// <summary>
        /// High-res thumbnail for Now Playing (300×300 display).
        /// YTM thumbnails → 1:1 square crop at 480px.
        /// YouTube thumbnails → maxresdefault (1280×720, true 16:9, no letterbox).
        /// DecodePixelWidth=480 limits memory usage on 512MB devices.
        /// </summary>
        private static string GetNowPlayingThumbnail(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            // YouTube Music thumbnails — request high-res square
            if (url.Contains("lh3.googleusercontent.com") || url.Contains("yt3.ggpht.com"))
            {
                int eqIdx = url.LastIndexOf("=");
                if (eqIdx > 0)
                    return url.Substring(0, eqIdx) + "=w480-h480-l90-rj";
                return url + "=w480-h480-l90-rj";
            }
            // YouTube video thumbnails — use maxresdefault (true 16:9, no letterbox bars)
            // hqdefault is 480×360 (4:3 with black bars) — UNUSABLE for 1:1
            // maxresdefault is 1280×720 (true 16:9) — DecodePixelWidth keeps RAM low
            if (url.Contains("hqdefault.jpg"))
                return url.Replace("hqdefault.jpg", "maxresdefault.jpg");
            if (url.Contains("mqdefault.jpg"))
                return url.Replace("mqdefault.jpg", "maxresdefault.jpg");
            if (url.Contains("sddefault.jpg"))
                return url.Replace("sddefault.jpg", "maxresdefault.jpg");
            // General size params
            if (url.Contains("=w120-h120"))
                return url.Replace("=w120-h120", "=w480-h480-l90-rj");
            if (url.Contains("=w60-h60"))
                return url.Replace("=w60-h60", "=w480-h480-l90-rj");
            if (url.Contains("=w226-h226"))
                return url.Replace("=w226-h226", "=w480-h480-l90-rj");
            return url;
        }

        public async Task<List<YouTubeTrack>> FetchMusicList(string query, string pageToken = "", string searchFilter = null)
        {
            var list = new List<YouTubeTrack>(20);

            // --- CONTINUATION: Load more using token ---
            if (!string.IsNullOrEmpty(pageToken) && pageToken != "NEW")
            {
                try
                {
                    var contResult = await InnerTubeClient.SearchContinueAsync(pageToken);
                    if (contResult != null && contResult.Tracks != null)
                    {
                        list.AddRange(contResult.Tracks);
                        _nextSearchToken = contResult.ContinuationToken ?? "";
                    }
                }
                catch { }
                return list;
            }

            // ═══════════════════════════════════════════════════
            // InnerTube YouTube Music search (không cần API key)
            // ═══════════════════════════════════════════════════
            try
            {
                // YouTube Music search params for each filter type
                string ytmParams = null;
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    switch (searchFilter)
                    {
                        case "songs": ytmParams = "EgWKAQIIAWoKEAMQBBAKEAkQBQ%3D%3D"; break;
                        case "videos": ytmParams = "EgWKAQIQAWoKEAMQBBAKEAkQBQ%3D%3D"; break;
                        case "playlists": ytmParams = "EgeKAQQoAEABagoQAxAEEAoQCRAF"; break;
                        case "artists": ytmParams = "EgWKAQIgAWoKEAMQBBAKEAkQBQ%3D%3D"; break;
                    }
                }

                var innerResult = await InnerTubeClient.SearchWithContinuationAsync(query, 20, ytmParams);
                if (innerResult != null && innerResult.Tracks != null && innerResult.Tracks.Count > 0)
                {
                    list.AddRange(innerResult.Tracks);
                    _nextSearchToken = innerResult.ContinuationToken ?? "";
                }
            }
            catch { }
            return list;
        }
    }
}
