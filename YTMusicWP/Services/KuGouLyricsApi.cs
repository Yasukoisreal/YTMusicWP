using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Lyrics từ KuGou. Ưu tiên KRC (có thời gian theo TỪNG TỪ), không có thì lùi về LRC theo dòng.
    /// Kết quả trả về dạng enhanced LRC ("[mm:ss.xx]&lt;mm:ss.xx&gt;từ...") để dùng chung parser và logic
    /// chèn dấu chấm dạo nhạc có sẵn (ParseAndDisplaySyncedLyrics). Logic thuần nằm ở KuGouLyricsParser.
    /// </summary>
    public static class KuGouLyricsApi
    {
        public sealed class Result
        {
            public string Lrc;
            public bool HasWordSync;
        }

        private const int MaxSongsToTry = 3;

        private static readonly HttpClient _client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            try
            {
                if (handler.SupportsAutomaticDecompression)
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            }
            catch { }
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        }

        public static async Task<Result> GetLyricsAsync(string title, string artist, int durationSec, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            string keyword = KuGouLyricsParser.BuildKeyword(title, artist);
            var candidates = new List<JToken>();

            // 1. Tìm bài hát → lọc theo thời lượng & phiên bản → tìm lyrics theo hash của bài
            try
            {
                string songUrl = "https://mobileservice.kugou.com/api/v3/search/song?version=9108&plat=0&pagesize=8&showtype=0&keyword="
                                 + Uri.EscapeDataString(keyword);
                var songs = JObject.Parse(await GetStringAsync(songUrl, token))["data"]?["info"] as JArray;
                if (songs != null)
                {
                    int tried = 0;
                    foreach (var song in songs)
                    {
                        if (tried >= MaxSongsToTry) break;
                        int songDur = song["duration"]?.Value<int>() ?? 0;
                        string songName = (song["songname"]?.ToString() ?? "") + " " + (song["filename"]?.ToString() ?? "");
                        string hash = song["hash"]?.ToString();
                        if (string.IsNullOrEmpty(hash) || !KuGouLyricsParser.IsAcceptable(title, songName, durationSec, songDur)) continue;
                        tried++;

                        var byHash = await SearchLyricsAsync("hash=" + Uri.EscapeDataString(hash), token);
                        if (byHash != null && byHash.Count > 0) candidates.Add(byHash[0]);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            // 2. Dự phòng: tìm lyrics trực tiếp theo từ khoá (+ thời lượng nếu biết)
            if (candidates.Count == 0)
            {
                try
                {
                    string q = "keyword=" + Uri.EscapeDataString(keyword);
                    if (durationSec > 0) q += "&duration=" + (durationSec * 1000);
                    var byKeyword = await SearchLyricsAsync(q, token);
                    if (byKeyword != null)
                    {
                        foreach (var c in byKeyword)
                        {
                            string candName = (c["song"]?.ToString() ?? "") + " " + (c["singer"]?.ToString() ?? "");
                            int candDur = (int)Math.Round((c["duration"]?.Value<double>() ?? 0) / 1000.0);
                            if (KuGouLyricsParser.IsAcceptable(title, candName, durationSec, candDur))
                            {
                                candidates.Add(c);
                                break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            // 3. Tải: KRC (word-by-word) trước, LRC theo dòng sau
            foreach (var cand in candidates)
            {
                string id = cand["id"]?.ToString();
                string accessKey = cand["accesskey"]?.ToString();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(accessKey)) continue;

                try
                {
                    string krcText = KuGouLyricsParser.DecodeKrcBase64(await DownloadAsync(id, accessKey, "krc", token));
                    if (krcText != null && !KuGouLyricsParser.HeaderIsOtherVersion(krcText, title))
                    {
                        string lrc = KuGouLyricsParser.KrcToEnhancedLrc(krcText, title);
                        if (!string.IsNullOrEmpty(lrc)) return new Result { Lrc = lrc, HasWordSync = true };
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }

                try
                {
                    string lrcContent = await DownloadAsync(id, accessKey, "lrc", token);
                    if (!string.IsNullOrEmpty(lrcContent))
                    {
                        byte[] lrcBytes = Convert.FromBase64String(lrcContent);
                        string lrc = KuGouLyricsParser.CleanLrc(Encoding.UTF8.GetString(lrcBytes, 0, lrcBytes.Length), title);
                        if (!string.IsNullOrEmpty(lrc)) return new Result { Lrc = lrc, HasWordSync = false };
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            return null;
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken token)
        {
            using (var resp = await _client.GetAsync(url, token))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync();
            }
        }

        private static async Task<JArray> SearchLyricsAsync(string query, CancellationToken token)
        {
            string url = "https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&" + query;
            return JObject.Parse(await GetStringAsync(url, token))["candidates"] as JArray;
        }

        private static async Task<string> DownloadAsync(string id, string accessKey, string fmt, CancellationToken token)
        {
            string url = "https://lyrics.kugou.com/download?ver=1&client=pc&charset=utf8&fmt=" + fmt
                         + "&id=" + Uri.EscapeDataString(id) + "&accesskey=" + Uri.EscapeDataString(accessKey);
            var json = JObject.Parse(await GetStringAsync(url, token));
            if ((json["status"]?.Value<int>() ?? 0) != 200) return null;
            return json["content"]?.ToString();
        }
    }
}
