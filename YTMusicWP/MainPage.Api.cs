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

        public async Task<List<YouTubeTrack>> FetchMusicList(string query, string pageToken = "")
        {
            var list = new List<YouTubeTrack>(8);

            // --- LỚP 1 (ƯU TIÊN): INNERTUBE TRỰC TIẾP (không cần proxy) ---
            try
            {
                var innerResults = await InnerTubeClient.SearchAsync(query, 12);
                if (innerResults != null && innerResults.Count > 0)
                {
                    list.AddRange(innerResults);
                    return list;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine("FetchMusicList InnerTube lỗi, chuyển sang Lớp 2."); }

            // --- LỚP 2: YOUTUBE API V3 (CHANNELS/PLAYLISTS & FALLBACK) ---
            string apiKey = ApiKeyTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(apiKey))
            {
                // Nếu Lớp 1 đã có dữ liệu (có pageToken hay không), lấy thêm channel/playlist ở trang đầu
                if (list.Count > 0 && !string.IsNullOrEmpty(pageToken)) return list; 

                string ytUrl = "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=" + (list.Count > 0 ? "3" : "8") + "&q=" + Uri.EscapeDataString(query) + "&type=" + (list.Count > 0 ? "channel,playlist" : "video,channel,playlist") + "&key=" + apiKey;

                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
                {
                    string region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();
                    if (region != "US") ytUrl += "&regionCode=" + region;
                }
                if (!string.IsNullOrEmpty(pageToken) && list.Count == 0) ytUrl += "&pageToken=" + pageToken;

                try
                {
                    var response = await _apiClient.GetStringAsync(ytUrl);
                    var json = JObject.Parse(response);
                    var items = json["items"];
                    if (items != null)
                    {
                        var ytList = new List<YouTubeTrack>();
                        foreach (var item in items)
                        {
                            try { var track = ParseTrackItem(item); if (track != null) ytList.Add(track); }
                            catch { continue; }
                        }
                        
                        if (list.Count > 0)
                        {
                            ytList.AddRange(list);
                            return ytList;
                        }
                        else
                        {
                            return ytList;
                        }
                    }
                }
                catch { }
            }
            return list;
        }

    }
}
