using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private static YouTubeTrack ParseTrackItem(JToken item)
        {
            string vidId = item["id"]?["videoId"]?.ToString();
            if (string.IsNullOrEmpty(vidId)) vidId = item["id"]?["channelId"]?.ToString() != null ? "CHANNEL:" + item["id"]["channelId"].ToString() : null;
            if (string.IsNullOrEmpty(vidId)) vidId = item["id"]?["playlistId"]?.ToString() != null ? "PLAYLIST:" + item["id"]["playlistId"].ToString() : null;
            if (string.IsNullOrEmpty(vidId)) return null;

            string title   = System.Net.WebUtility.HtmlDecode(item["snippet"]?["title"]?.ToString() ?? "");
            string channel = CleanChannelName(System.Net.WebUtility.HtmlDecode(item["snippet"]?["channelTitle"]?.ToString() ?? ""));
            string channelId = item["snippet"]?["channelId"]?.ToString() ?? item["id"]?["channelId"]?.ToString();
            var    thumbs  = item["snippet"]?["thumbnails"];
            string thumbUrl = thumbs?["maxres"]?["url"]?.ToString()
                           ?? thumbs?["standard"]?["url"]?.ToString()
                           ?? thumbs?["high"]?["url"]?.ToString()
                           ?? thumbs?["medium"]?["url"]?.ToString()
                           ?? thumbs?["default"]?["url"]?.ToString();
            return new YouTubeTrack { VideoId = vidId, Title = title, ChannelName = channel, ChannelId = channelId, ThumbnailUrl = thumbUrl };
        }

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
                return url.Replace("hqdefault.jpg", "hqdefault.jpg"); // giữ hqdefault (480x360), ko dùng maxresdefault (1280x720)
            if (url.Contains("mqdefault.jpg"))
                return url.Replace("mqdefault.jpg", "hqdefault.jpg");
            if (url.Contains("-s120-"))
                return url.Replace("-s120-", "-s480-");
            if (url.Contains("-s68-"))
                return url.Replace("-s68-", "-s480-");
            return url;
        }

        public async Task<List<YouTubeTrack>> FetchMusicList(string query, string pageToken = "", bool requireApiKey = false, string searchFilter = null)
        {
            var list = new List<YouTubeTrack>(20);

            // --- CONTINUATION: Load more using token ---
            if (!string.IsNullOrEmpty(pageToken) && pageToken != "NEW")
            {
                // If token looks like InnerTube continuation (long base64), use InnerTube
                if (pageToken.Length > 30)
                {
                    try
                    {
                        var contResult = await InnerTubeClient.SearchContinueAsync(pageToken);
                        if (contResult != null && contResult.Tracks != null)
                        {
                            list.AddRange(contResult.Tracks);
                            _nextSearchToken = contResult.ContinuationToken ?? "";
                        }
                        return list;
                    }
                    catch { return list; }
                }
                else
                {
                    // Short token = YouTube API v3 pageToken
                    string apiKey2 = GetApiKey();
                    if (!string.IsNullOrEmpty(apiKey2))
                    {
                        try
                        {
                            string ytUrl2 = "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=20&q=" + Uri.EscapeDataString(query) + "&type=video,channel,playlist&key=" + apiKey2 + "&pageToken=" + pageToken;
                            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
                            {
                                string region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();
                                if (region != "US") ytUrl2 += "&regionCode=" + region;
                            }
                            var response2 = await _apiClient.GetStringAsync(ytUrl2);
                            var json2 = JObject.Parse(response2);
                            _nextSearchToken = json2["nextPageToken"]?.ToString() ?? "";
                            var items2 = json2["items"];
                            if (items2 != null) foreach (var item in items2) { try { var t = ParseTrackItem(item); if (t != null) list.Add(t); } catch { } }
                        }
                        catch { }
                    }
                    return list;
                }
            }

            // ═══════════════════════════════════════════════════
            // SEARCH CONTEXT: API Key bắt buộc (giống MetroTube)
            // ═══════════════════════════════════════════════════
            if (requireApiKey)
            {
                // LAYER 1: If filter active, use InnerTube YouTube Music search (returns proper music results)
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    try
                    {
                        // YouTube Music search params for each filter type
                        string ytmParams = null;
                        switch (searchFilter)
                        {
                            case "songs": ytmParams = "EgWKAQIIAWoKEAMQBBAKEAkQBQ%3D%3D"; break;
                            case "videos": ytmParams = "EgWKAQIQAWoKEAMQBBAKEAkQBQ%3D%3D"; break;
                            case "playlists": ytmParams = "EgeKAQQoAEABagoQAxAEEAoQCRAF"; break;
                            case "artists": ytmParams = "EgWKAQIgAWoKEAMQBBAKEAkQBQ%3D%3D"; break;
                        }
                        var innerResult = await InnerTubeClient.SearchWithContinuationAsync(query, 20, ytmParams);
                        if (innerResult != null && innerResult.Tracks != null && innerResult.Tracks.Count > 0)
                        {
                            list.AddRange(innerResult.Tracks);
                            _nextSearchToken = innerResult.ContinuationToken ?? "";
                            return list;
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine("[API] InnerTube filtered search failed, falling back to API v3"); }
                }

                // LAYER 2: YouTube Data API v3 (primary for unfiltered)
                string apiKey = GetApiKey();
                if (!string.IsNullOrEmpty(apiKey))
                {
                    try
                    {
                        string searchType = "video,channel,playlist";
                        if (searchFilter == "songs" || searchFilter == "videos") searchType = "video";
                        else if (searchFilter == "playlists") searchType = "playlist";
                        else if (searchFilter == "artists") searchType = "channel";

                        string ytUrl = "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=20&q=" + Uri.EscapeDataString(query) + "&type=" + searchType + "&key=" + apiKey;
                        
                        // Add music category filter for songs
                        if (searchFilter == "songs") ytUrl += "&videoCategoryId=10";
                        
                        if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
                        {
                            string region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();
                            if (region != "US") ytUrl += "&regionCode=" + region;
                        }

                        var response = await _apiClient.GetStringAsync(ytUrl);
                        var json = JObject.Parse(response);
                        _nextSearchToken = json["nextPageToken"]?.ToString() ?? "";
                        var items = json["items"];
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                try { var track = ParseTrackItem(item); if (track != null) list.Add(track); }
                                catch { continue; }
                            }
                        }
                        if (list.Count > 0) return list;
                    }
                    catch { System.Diagnostics.Debug.WriteLine("[API] YouTube API v3 failed, trying InnerTube fallback"); }
                }
                else
                {
                    return null; // Caller sẽ hiện thông báo yêu cầu key
                }

                // LAYER 3: InnerTube fallback (nếu API v3 lỗi)
                try
                {
                    var innerResult = await InnerTubeClient.SearchWithContinuationAsync(query, 20);
                    if (innerResult != null && innerResult.Tracks != null && innerResult.Tracks.Count > 0)
                    {
                        list.AddRange(innerResult.Tracks);
                        _nextSearchToken = innerResult.ContinuationToken ?? "";
                    }
                }
                catch { }
                return list;
            }

            // ═══════════════════════════════════════════════════
            // HOME CONTEXT: InnerTube only (không cần API key)
            // ═══════════════════════════════════════════════════
            try
            {
                var innerResult = await InnerTubeClient.SearchWithContinuationAsync(query, 20);
                if (innerResult != null && innerResult.Tracks != null && innerResult.Tracks.Count > 0)
                {
                    list.AddRange(innerResult.Tracks);
                    _nextSearchToken = innerResult.ContinuationToken ?? "";
                }
            }
            catch { }
            return list;
        }

        private string GetApiKey()
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("YouTubeApiKey"))
                {
                    string key = ApplicationData.Current.LocalSettings.Values["YouTubeApiKey"].ToString().Trim();
                    if (!string.IsNullOrEmpty(key)) return key;
                }
            }
            catch { }
            return null;
        }
    }
}
