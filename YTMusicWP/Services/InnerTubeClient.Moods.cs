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
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["browseId"] = "FEmusic_moods_and_genres_category",
                    ["params"] = paramsStr
                };

                string apiUrl = "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false";
                JObject data = null;

                if (HasCookieAuth)
                {
                    var extraBody = new JObject
                    {
                        ["browseId"] = "FEmusic_moods_and_genres_category",
                        ["params"] = paramsStr
                    };
                    data = await CookieInnerTubePostAsync("browse", extraBody, "WEB_REMIX", "1.20260304.03.00");
                }
                else
                {
                    data = await PostInnerTubeAsync(apiUrl, body, true);
                }

                if (data == null) return null;

                // Find first available playlist / item thumbnail
                string thumbUrl = null;
                var allThumbs = data.SelectTokens("$..thumbnails").ToList();
                foreach (var th in allThumbs)
                {
                    var arr = th as JArray;
                    if (arr != null && arr.Count > 0)
                    {
                        var last = arr.Last;
                        string url = last?["url"]?.ToString();
                        if (!string.IsNullOrEmpty(url) && !url.Contains("avatar") && !url.Contains("channel") && (url.StartsWith("http://") || url.StartsWith("https://")))
                        {
                            thumbUrl = url;
                            break;
                        }
                    }
                }

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
