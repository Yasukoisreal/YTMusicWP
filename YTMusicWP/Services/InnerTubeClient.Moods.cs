using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace YTMusicWP
{
    public static partial class InnerTubeClient
    {
        private static Dictionary<string, string> _moodArtworkCache = null;
        private static bool _isCacheLoaded = false;
        private const string MOOD_CACHE_FILE = "mood_artwork_cache.json";

        private static List<MoodCategory> _cachedMoods = null;
        private static DateTime _moodsCacheTime = DateTime.MinValue;
        private static string _cachedMoodsLanguage = null;

        private static async Task EnsureMoodArtworkCacheLoadedAsync()
        {
            if (_isCacheLoaded) return;
            _moodArtworkCache = new Dictionary<string, string>();
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(MOOD_CACHE_FILE);
                if (file != null)
                {
                    string json = await FileIO.ReadTextAsync(file);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                        if (dict != null)
                        {
                            _moodArtworkCache = dict;
                        }
                    }
                }
            }
            catch { }
            _isCacheLoaded = true;
        }

        private static async Task SaveMoodArtworkCacheAsync()
        {
            try
            {
                if (_moodArtworkCache == null) return;
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(MOOD_CACHE_FILE, CreationCollisionOption.ReplaceExisting);
                string json = JsonConvert.SerializeObject(_moodArtworkCache);
                await FileIO.WriteTextAsync(file, json);
            }
            catch { }
        }

        public static async Task<List<MoodCategory>> BrowseMoodsAndGenresAsync()
        {
            if (_cachedMoods != null && _cachedMoods.Count > 0
                && (DateTime.Now - _moodsCacheTime).TotalHours < 24
                && _cachedMoodsLanguage == CurrentLanguage)
            {
                return _cachedMoods;
            }

            var categories = new List<MoodCategory>();
            try
            {
                await EnsureMoodArtworkCacheLoadedAsync();

                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["browseId"] = "FEmusic_moods_and_genres"
                };

                string apiUrl = "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false";
                JObject data = null;

                if (HasCookieAuth)
                {
                    var extraBody = new JObject { ["browseId"] = "FEmusic_moods_and_genres" };
                    data = await CookieInnerTubePostAsync("browse", extraBody, "WEB_REMIX", "1.20260304.03.00");
                }
                else
                {
                    data = await PostInnerTubeAsync(apiUrl, body, true);
                }

                if (data == null) return categories;

                var sectionContents = data.SelectToken("$..sectionListRenderer.contents") as JArray;
                if (sectionContents == null) return categories;

                foreach (var sec in sectionContents)
                {
                    var gridRenderer = sec["gridRenderer"];
                    if (gridRenderer == null) continue;

                    string secTitle = gridRenderer["header"]?["gridHeaderRenderer"]?["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                    var itemsArray = gridRenderer["items"] as JArray;
                    if (itemsArray == null) continue;

                    var moodCategory = new MoodCategory { Title = secTitle };

                    foreach (var itemWrapper in itemsArray)
                    {
                        var btn = itemWrapper["musicNavigationButtonRenderer"];
                        if (btn == null) continue;

                        string title = btn["buttonText"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(title)) continue;

                        string browseId = btn["clickCommand"]?["browseEndpoint"]?["browseId"]?.ToString() ?? "FEmusic_moods_and_genres_category";
                        string p = btn["clickCommand"]?["browseEndpoint"]?["params"]?.ToString() ?? "";

                        uint stripeColor = 0;
                        string colorHex = "#333333";
                        try
                        {
                            var scToken = btn["solid"]?["leftStripeColor"];
                            if (scToken != null)
                            {
                                stripeColor = scToken.Value<uint>();
                                colorHex = "#" + (stripeColor & 0x00FFFFFF).ToString("X6");
                            }
                        }
                        catch { }

                        string cachedThumb = null;
                        if (!string.IsNullOrEmpty(p) && _moodArtworkCache != null && _moodArtworkCache.TryGetValue(p, out cachedThumb))
                        {
                        }

                        var item = new MoodCategoryItem
                        {
                            Title = title,
                            BrowseId = browseId,
                            Params = p,
                            StripeColor = stripeColor,
                            Color = colorHex,
                            SectionTitle = secTitle,
                            ThumbnailUrl = cachedThumb
                        };

                        moodCategory.Items.Add(item);
                    }

                    if (moodCategory.Items.Count > 0)
                    {
                        categories.Add(moodCategory);
                    }
                }
            }
            catch { }

            if (categories.Count > 0)
            {
                _cachedMoods = categories;
                _moodsCacheTime = DateTime.Now;
                _cachedMoodsLanguage = CurrentLanguage;
            }

            return categories;
        }

        private static async Task<string> FetchFirstMoodThumbnailAsync(string paramsStr)
        {
            string vd = await GetVisitorDataAsync().ConfigureAwait(false);

            if (HasCookieAuth)
            {
                // Same request as CookieInnerTubePostAsync, but streamed
                var client = new JObject
                {
                    ["clientName"] = "WEB_REMIX",
                    ["clientVersion"] = "1.20260304.03.00",
                    ["hl"] = CurrentLanguage,
                    ["gl"] = CurrentRegion,
                    ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"
                };
                if (!string.IsNullOrEmpty(vd)) client["visitorData"] = vd;
                var body = new JObject
                {
                    ["context"] = new JObject { ["client"] = client },
                    ["browseId"] = "FEmusic_moods_and_genres_category",
                    ["params"] = paramsStr
                };
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                    "https://music.youtube.com/youtubei/v1/browse?key=AIzaSyC9XL3ZjWddXya6X74dJoCTL-WEYFDNX30&prettyPrint=false");
                request.Content = new System.Net.Http.StringContent(body.ToString(Formatting.None), System.Text.Encoding.UTF8, "application/json");
                request.Headers.Add("User-Agent", (string)client["userAgent"]);
                if (!string.IsNullOrEmpty(CurrentLanguage)) request.Headers.Add("Accept-Language", CurrentLanguage);
                request.Headers.Add("Origin", "https://music.youtube.com");
                request.Headers.Add("Referer", "https://music.youtube.com/");
                request.Headers.Add("X-Goog-Authuser", "0");
                request.Headers.Add("Cookie", _cookieString);
                request.Headers.Add("Authorization", GenerateSAPISIDHash(_sapisid));

                using (request)
                using (var response = await _client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        return ReadFirstThumbnailUrl(stream);
                }
            }
            else
            {
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["browseId"] = "FEmusic_moods_and_genres_category",
                    ["params"] = paramsStr
                };
                var resolved = await Services.SecureDnsResolver.RewriteUrlAsync("https://music.youtube.com/youtubei/v1/browse?prettyPrint=false").ConfigureAwait(false);
                using (var request = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Post, new Uri(resolved.Url)))
                {
                    if (resolved.WasResolved && !string.IsNullOrEmpty(resolved.OriginalHost))
                        request.Headers.Host = new Windows.Networking.HostName(resolved.OriginalHost);
                    request.Content = new Windows.Web.Http.HttpStringContent(body.ToString(Formatting.None), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
                    request.Headers.TryAppendWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
                    if (!string.IsNullOrEmpty(CurrentLanguage)) request.Headers.TryAppendWithoutValidation("Accept-Language", CurrentLanguage);
                    request.Headers.TryAppendWithoutValidation("Origin", "https://music.youtube.com");
                    request.Headers.TryAppendWithoutValidation("Referer", "https://music.youtube.com/");

                    using (var resp = await GetWinrtClient().SendRequestAsync(request, Windows.Web.Http.HttpCompletionOption.ResponseHeadersRead).AsTask().ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        using (var input = await resp.Content.ReadAsInputStreamAsync().AsTask().ConfigureAwait(false))
                        using (var stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(input))
                            return ReadFirstThumbnailUrl(stream);
                    }
                }
            }
        }

        /// <summary>
        /// Scans a browse response token by token and returns the last (largest) url of the first "thumbnails"
        /// array that is a usable artwork (not an avatar/channel image). Stops reading as soon as one is found.
        /// </summary>
        internal static string ReadFirstThumbnailUrl(Stream json)
        {
            using (var reader = new JsonTextReader(new StreamReader(json)))
            {
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.PropertyName || (string)reader.Value != "thumbnails") continue;
                    if (!reader.Read() || reader.TokenType != JsonToken.StartArray) continue;

                    string lastUrl = null;
                    int depth = reader.Depth;
                    while (reader.Read() && !(reader.TokenType == JsonToken.EndArray && reader.Depth == depth))
                    {
                        if (reader.TokenType == JsonToken.PropertyName && (string)reader.Value == "url" && reader.Read() && reader.TokenType == JsonToken.String)
                            lastUrl = (string)reader.Value;
                    }

                    if (!string.IsNullOrEmpty(lastUrl) && !lastUrl.Contains("avatar") && !lastUrl.Contains("channel") &&
                        (lastUrl.StartsWith("http://") || lastUrl.StartsWith("https://")))
                        return lastUrl;
                }
            }
            return null;
        }

        public static async Task<string> GetMoodCategoryArtworkAsync(string paramsStr)
        {
            if (string.IsNullOrEmpty(paramsStr)) return null;

            await EnsureMoodArtworkCacheLoadedAsync();
            if (_moodArtworkCache != null && _moodArtworkCache.ContainsKey(paramsStr))
            {
                return _moodArtworkCache[paramsStr];
            }

            try
            {
                // The category page is a large response but only its first usable thumbnail is needed: read it as a
                // token stream and stop there, instead of building the whole JObject (this ran ~60 times on the
                // Search tab and pushed 512MB phones out of memory).
                string thumbUrl = await FetchFirstMoodThumbnailAsync(paramsStr).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(thumbUrl))
                {
                    if (_moodArtworkCache != null)
                    {
                        _moodArtworkCache[paramsStr] = thumbUrl;
                    }
                    var ignored = SaveMoodArtworkCacheAsync();
                    return thumbUrl;
                }
            }
            catch { }

            return null;
        }
    }
}
