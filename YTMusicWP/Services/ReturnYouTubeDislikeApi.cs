using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Like, dislike and view counts from Return YouTube Dislike (returnyoutubedislikeapi.com, a public API without a key).
    /// YouTube hides dislike counts; RYD estimates them from its extension users and archived data.
    /// </summary>
    public static class ReturnYouTubeDislikeApi
    {
        public sealed class Votes
        {
            public long Likes;
            public long Dislikes;
            public long Views;
        }

        private const int MaxCached = 50;
        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly Dictionary<string, Votes> _cache = new Dictionary<string, Votes>();
        private static readonly List<string> _cacheOrder = new List<string>();

        /// <summary>The counts for a video, or null when unknown (offline file, network error, video not in RYD).</summary>
        public static async Task<Votes> GetVotesAsync(string videoId, CancellationToken token)
        {
            if (string.IsNullOrEmpty(videoId) || videoId.StartsWith("LOCAL:")) return null;
            lock (_cache)
            {
                Votes cached;
                if (_cache.TryGetValue(videoId, out cached)) return cached;
            }

            try
            {
                string url = "https://returnyoutubedislikeapi.com/Votes?videoId=" + Uri.EscapeDataString(videoId);
                using (var resp = await _client.GetAsync(url, token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var json = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var votes = new Votes
                    {
                        Likes = json["likes"]?.Value<long>() ?? 0,
                        Dislikes = json["dislikes"]?.Value<long>() ?? 0,
                        Views = json["viewCount"]?.Value<long>() ?? 0
                    };
                    lock (_cache)
                    {
                        if (!_cache.ContainsKey(videoId))
                        {
                            if (_cacheOrder.Count >= MaxCached)
                            {
                                _cache.Remove(_cacheOrder[0]);
                                _cacheOrder.RemoveAt(0);
                            }
                            _cacheOrder.Add(videoId);
                        }
                        _cache[videoId] = votes;
                    }
                    return votes;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A short count for the UI: 845, 23K, 1.2M, 1.8B.</summary>
        public static string Compact(long n)
        {
            if (n < 1000) return n.ToString(CultureInfo.InvariantCulture);
            if (n < 1000000) return Shorten(n / 1000.0) + "K";
            if (n < 1000000000) return Shorten(n / 1000000.0) + "M";
            return Shorten(n / 1000000000.0) + "B";
        }

        private static string Shorten(double value)
        {
            // One decimal below 10 (1.2M), none above (23K, 520K)
            return value < 10
                ? (Math.Floor(value * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture)
                : Math.Floor(value).ToString(CultureInfo.InvariantCulture);
        }
    }
}
