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
        public static async Task<PlaylistResult> BrowsePlaylistAsync(string playlistId, string continuationToken = null)
        {
            var result = new PlaylistResult();
            try
            {
                string vd = await GetVisitorDataAsync();

                // Build request body for WEB_REMIX (YouTube Music)
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd)
                };

                if (!string.IsNullOrEmpty(continuationToken))
                {
                    body["continuation"] = continuationToken;
                }
                else
                {
                    // Prefix with VL for regular playlists, unless it's already an album/mix prefix
                    bool isAlbum = playlistId.StartsWith("MPREb_") || playlistId.StartsWith("OLAK5");
                    string browseId = playlistId;
                    if (!isAlbum && !playlistId.StartsWith("VL"))
                    {
                        browseId = "VL" + playlistId;
                    }
                    body["browseId"] = browseId;
                    
                    // wAEB params often needed for full playlist track list in YouTube Music
                    if (!isAlbum)
                    {
                        body["params"] = "wAEB";
                    }
                }

                string apiUrl = "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false";
                
                JObject data = null;
                if (HasCookieAuth)
                {
                    // Use authenticated WEB_REMIX if cookie is available (needed for private playlists)
                    var extraBody = new JObject();
                    foreach (var prop in body.Properties())
                    {
                        if (prop.Name != "context") extraBody[prop.Name] = prop.Value;
                    }
                    data = await CookieInnerTubePostAsync("browse", extraBody, "WEB_REMIX", "1.20260304.03.00");
                }
                else
                {
                    var dataStr = await PostInnerTubeAsync(apiUrl, body, true);
                    data = dataStr;
                }

                // If not continuation, parse Title and Thumbnail
                if (string.IsNullOrEmpty(continuationToken))
                {
                    result.Title = data?["header"]?.SelectToken("$..title.runs[0].text")?.ToString() 
                        ?? data?["metadata"]?["playlistMetadataRenderer"]?["title"]?.ToString() 
                        ?? "";

                    result.ThumbnailUrl = data?["header"]?.SelectToken("$..thumbnails[0].url")?.ToString() 
                        ?? data?["microformat"]?.SelectToken("$..thumbnails[0].url")?.ToString() 
                        ?? "";
                }

                // Parse tracks
                var allItems = data?.SelectTokens("$..musicResponsiveListItemRenderer");
                if (allItems != null)
                {
                    foreach (var mrlir in allItems)
                    {
                        try
                        {
                            var wrapper = new JObject { ["musicResponsiveListItemRenderer"] = mrlir };
                            var track = ParseMusicListItem(wrapper);
                            if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                result.Tracks.Add(track);
                        }
                        catch { continue; }
                    }
                }

                // Look for Continuation Token
                string newToken = null;
                var tokens = data?.SelectTokens("$..continuationCommand.token");
                if (tokens != null)
                {
                    newToken = tokens.LastOrDefault()?.ToString();
                }
                if (string.IsNullOrEmpty(newToken))
                {
                    tokens = data?.SelectTokens("$..nextContinuationData.continuation");
                    if (tokens != null)
                    {
                        newToken = tokens.LastOrDefault()?.ToString();
                    }
                }
                result.ContinuationToken = newToken;

            }
            catch { }
            return result;
        }

        /// <summary>
        /// Parse lockupViewModel → YouTubeTrack (format mới của YouTube playlist)
        /// </summary>
        private static YouTubeTrack ParseLockupViewModel(JToken item)
        {
            var lvm = item["lockupViewModel"];
            if (lvm == null) return null;

            string videoId = lvm["contentId"]?.ToString();
            if (string.IsNullOrEmpty(videoId)) return null;

            // Title
            string title = lvm["metadata"]?["lockupMetadataViewModel"]
                ?["title"]?["content"]?.ToString() ?? "";

            // Artist — from metadataRows
            string artist = "";
            var rows = lvm["metadata"]?["lockupMetadataViewModel"]
                ?["metadata"]?["contentMetadataViewModel"]?["metadataRows"];
            if (rows != null && rows.HasValues)
            {
                var parts = rows[0]?["metadataParts"];
                if (parts != null && parts.HasValues)
                    artist = parts[0]?["text"]?["content"]?.ToString() ?? "";
            }

            // Thumbnail
            string thumbUrl = "";
            var sources = lvm["contentImage"]?["collectionThumbnailViewModel"]
                ?["primaryThumbnail"]?["thumbnailViewModel"]?["image"]?["sources"];
            if (sources != null && sources.HasValues)
                thumbUrl = sources[0]?["url"]?.ToString() ?? "";

            // Fallback thumbnail from videoId
            if (string.IsNullOrEmpty(thumbUrl))
                thumbUrl = "https://i.ytimg.com/vi/" + videoId + "/hqdefault.jpg";

            return new YouTubeTrack
            {
                VideoId = videoId,
                Title = title,
                ChannelName = CleanChannelName(artist),
                ThumbnailUrl = thumbUrl
            };
        }

        // ==========================================
        // BROWSE ARTIST
        // ==========================================
        public static async Task<ArtistResult> BrowseArtistAsync(string channelId)
        {
            var result = new ArtistResult();
            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["browseId"] = channelId
                };

                var data = await PostInnerTubeAsync(
                    "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false", body, true);

                // Header — artist name + avatar
                var header = data?["header"];
                if (header != null)
                {
                    // Try musicImmersiveHeaderRenderer or musicVisualHeaderRenderer
                    var mih = header["musicImmersiveHeaderRenderer"] ?? header["musicVisualHeaderRenderer"];
                    if (mih != null)
                    {
                        result.IsYouTubeMusicArtist = true;
                        result.Name = mih["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";

                        // Avatar
                        var thumbs = mih["thumbnail"]?["musicThumbnailRenderer"]
                            ?["thumbnail"]?["thumbnails"];
                        if (thumbs != null && thumbs.HasValues)
                            result.AvatarUrl = thumbs.Last?["url"]?.ToString() ?? "";

                        // Banner/Cover
                        var fg = mih["foregroundThumbnail"]?["musicThumbnailRenderer"]
                            ?["thumbnail"]?["thumbnails"];
                        if (fg != null && fg.HasValues)
                            result.CoverUrl = fg.Last?["url"]?.ToString() ?? "";

                        // Subscriber count (monthly listeners)
                        var subText = mih["subscriptionButton"]?["subscribeButtonRenderer"]
                            ?["subscriberCountText"]?["runs"]?[0]?["text"]?.ToString();
                        if (!string.IsNullOrEmpty(subText))
                            result.SubscriberCount = subText;
                        // Also try subtitle runs for listener count
                        if (string.IsNullOrEmpty(result.SubscriberCount))
                        {
                            var subtitleRuns = mih["subtitle"]?["runs"];
                            if (subtitleRuns != null)
                            {
                                string subtitleText = "";
                                foreach (var sr in subtitleRuns)
                                    subtitleText += sr["text"]?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(subtitleText))
                                    result.SubscriberCount = subtitleText;
                            }
                        }

                        // Description
                        var descRuns = mih["description"]?["musicDescriptionShelfRenderer"]
                            ?["description"]?["runs"];
                        if (descRuns != null)
                        {
                            string desc = "";
                            foreach (var dr in descRuns)
                                desc += dr["text"]?.ToString() ?? "";
                            result.Description = desc;
                        }
                    }
                }

                // Songs — first musicShelfRenderer section
                // Albums/Singles — musicCarouselShelfRenderer sections
                var tabs = data?["contents"]?["singleColumnBrowseResultsRenderer"]?["tabs"];
                if (tabs != null && tabs.HasValues)
                {
                    var sections = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];
                    if (sections != null)
                    {
                        foreach (var sec in sections)
                        {
                            // Songs shelf
                            var shelf = sec["musicShelfRenderer"];
                            if (shelf != null && result.Tracks.Count == 0)
                            {
                                var items = shelf["contents"];
                                if (items != null)
                                {
                                    foreach (var item in items)
                                    {
                                        try
                                        {
                                            var track = ParseMusicListItem(item);
                                            if (track != null && !string.IsNullOrEmpty(track.VideoId)
                                                && !track.VideoId.StartsWith("CHANNEL:"))
                                            {
                                                result.Tracks.Add(track);
                                            }
                                        }
                                        catch { continue; }
                                    }
                                }
                                continue;
                            }

                            // Albums/Singles/Videos carousel
                            var carousel = sec["musicCarouselShelfRenderer"];
                            if (carousel != null)
                            {
                                var hdr = carousel["header"]?["musicCarouselShelfBasicHeaderRenderer"];
                                string sectionTitle = hdr?["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                var cItems = carousel["contents"];
                                if (cItems != null)
                                {
                                    foreach (var cItem in cItems)
                                    {
                                        try
                                        {
                                            var twoRow = cItem["musicTwoRowItemRenderer"];
                                            if (twoRow == null) continue;

                                            string itemTitle = twoRow["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                            string itemSub = "";
                                            var subRuns = twoRow["subtitle"]?["runs"];
                                            if (subRuns != null)
                                            {
                                                foreach (var sr in subRuns)
                                                {
                                                    string st = sr["text"]?.ToString();
                                                    if (!string.IsNullOrEmpty(st)) itemSub += st;
                                                }
                                            }

                                            string itemThumb = "";
                                            var thumbs = twoRow["thumbnailRenderer"]?["musicThumbnailRenderer"]
                                                ?["thumbnail"]?["thumbnails"];
                                            if (thumbs != null && thumbs.HasValues)
                                                itemThumb = thumbs.Last?["url"]?.ToString() ?? "";

                                            string browseId2 = twoRow["navigationEndpoint"]
                                                ?["browseEndpoint"]?["browseId"]?.ToString() ?? "";

                                            result.Albums.Add(new ArtistAlbum
                                            {
                                                Title = itemTitle,
                                                Subtitle = itemSub,
                                                ThumbnailUrl = itemThumb,
                                                BrowseId = browseId2,
                                                SectionTitle = sectionTitle
                                            });
                                        }
                                        catch { continue; }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        // ==========================================
        // BROWSE EXPLORE — Trending/Discover content
        // ==========================================
        private static List<DiscoverItem> _cachedDiscover = null;
        private static DateTime _discoverCacheTime = DateTime.MinValue;

        public static async Task<List<DiscoverItem>> BrowseExploreAsync()
        {
            // Cache 24 hours
            if (_cachedDiscover != null && _cachedDiscover.Count > 0 
                && (DateTime.Now - _discoverCacheTime).TotalHours < 24)
                return _cachedDiscover;

            var items = await FetchCarouselItemsAsync("FEmusic_explore");
            if (items != null && items.Count > 0)
            {
                _cachedDiscover = items;
                _discoverCacheTime = DateTime.Now;
            }
            return items;
        }

        private static List<DiscoverItem> _cachedCharts = null;
        private static DateTime _chartsCacheTime = DateTime.MinValue;

        public static async Task<List<DiscoverItem>> BrowseChartsAsync()
        {
            if (_cachedCharts != null && _cachedCharts.Count > 0 
                && (DateTime.Now - _chartsCacheTime).TotalHours < 24)
                return _cachedCharts;

            var items = await FetchCarouselItemsAsync("FEmusic_charts");
            if (items != null && items.Count > 0)
            {
                _cachedCharts = items;
                _chartsCacheTime = DateTime.Now;
            }
            return items;
        }

        private static async Task<List<DiscoverItem>> FetchCarouselItemsAsync(string browseId)
        {
            var items = new List<DiscoverItem>();
            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["browseId"] = browseId
                };

                if (browseId == "FEmusic_charts")
                {
                    var formData = new JObject();
                    var selectedValues = new JArray();
                    selectedValues.Add(CurrentRegion);
                    formData["selectedValues"] = selectedValues;
                    body["formData"] = formData;
                }

                var data = await PostInnerTubeAsync(
                    "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false", body, true);

                // Parse sections from singleColumnBrowseResultsRenderer
                var tabs = data?["contents"]?["singleColumnBrowseResultsRenderer"]?["tabs"];
                if (tabs != null && tabs.HasValues)
                {
                    var sections = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];
                    if (sections != null)
                    {
                        foreach (var sec in sections)
                        {
                            // musicCarouselShelfRenderer = trending carousels
                            var carousel = sec["musicCarouselShelfRenderer"];
                            if (carousel == null) continue;

                            var cItems = carousel["contents"];
                            if (cItems == null) continue;

                            foreach (var cItem in cItems)
                            {
                                try
                                {
                                    var twoRow = cItem["musicTwoRowItemRenderer"];
                                    if (twoRow == null) continue;

                                    string title = twoRow["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                    if (string.IsNullOrEmpty(title)) continue;

                                    // Subtitle (artist/type)
                                    string subtitle = "";
                                    var subRuns = twoRow["subtitle"]?["runs"];
                                    if (subRuns != null)
                                    {
                                        foreach (var sr in subRuns)
                                        {
                                            string st = sr["text"]?.ToString();
                                            if (!string.IsNullOrEmpty(st)) subtitle += st;
                                        }
                                    }

                                    // Thumbnail
                                    string thumbUrl = "";
                                    var thumbs = twoRow["thumbnailRenderer"]?["musicThumbnailRenderer"]
                                        ?["thumbnail"]?["thumbnails"];
                                    if (thumbs != null && thumbs.HasValues)
                                        thumbUrl = thumbs.Last?["url"]?.ToString() ?? "";

                                    // VideoId or PlaylistId from navigation
                                    string videoId = twoRow["navigationEndpoint"]
                                        ?["watchEndpoint"]?["videoId"]?.ToString();
                                    string playlistId = twoRow["navigationEndpoint"]
                                        ?["browseEndpoint"]?["browseId"]?.ToString();

                                    items.Add(new DiscoverItem
                                    {
                                        Title = title,
                                        Subtitle = subtitle,
                                        ThumbnailUrl = thumbUrl,
                                        VideoId = videoId ?? "",
                                        PlaylistId = playlistId ?? "",
                                        SearchQuery = title
                                    });

                                    if (items.Count >= 12) break; // Max 12 items
                                }
                                catch { continue; }
                            }
                            if (items.Count >= 12) break;
                        }
                    }
                }

            }
            catch { }
            return items;
        }

        // ==========================================
        // BROWSE HOME — YouTube Music Home Page sections
        // ==========================================
        public enum HomeSectionLayout
        {
            Normal,
            QuickPicks,
            Video
        }

        public class HomeSection
        {
            public string Title { get; set; }
            public HomeSectionLayout Layout { get; set; }
            public List<YouTubeTrack> Tracks { get; set; }
            public HomeSection() { Tracks = new List<YouTubeTrack>(); Layout = HomeSectionLayout.Normal; }
        }

        public static void ClearHomeCache()
        {
        }

        public static async Task<List<HomeSection>> BrowseHomeAsync(string accessToken = null, Action<List<HomeSection>> onPageLoaded = null)
        {
            var sections = new List<HomeSection>();
            try
            {
                string vd = await GetVisitorDataAsync();
                
                string continuation = null;
                int maxPages = HasCookieAuth ? 8 : 4; // Fetch more pages if logged in

                for (int page = 0; page < maxPages; page++)
                {
                    JObject data = null;
                    if (HasCookieAuth)
                    {
                        // Priority 1: Cookie-based auth (SAPISIDHASH) — works perfectly with WEB_REMIX
                        var extraParams = new JObject();
                        if (continuation == null)
                            extraParams["browseId"] = "FEmusic_home";
                        else
                            extraParams["continuation"] = continuation;
                            
                        data = await CookieInnerTubePostAsync("browse", extraParams);
                    }
                    else
                    {
                        var body = new JObject
                        {
                            ["context"] = BuildMusicContext(vd)
                        };
                        if (continuation == null)
                            body["browseId"] = "FEmusic_home";
                        else
                            body["continuation"] = continuation;
                        
                        string url = "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false";
                        data = await PostInnerTubeAsync(url, body, true);
                    }

                    if (data == null) break;

                    if (string.IsNullOrEmpty(vd))
                    {
                        var returnedVd = data["responseContext"]?["visitorData"]?.ToString();
                        if (!string.IsNullOrEmpty(returnedVd))
                        {
                            vd = returnedVd;
                            _cachedVisitorData = vd;
                            _vdCacheTime = DateTime.Now;
                            try { Windows.Storage.ApplicationData.Current.LocalSettings.Values["CachedVisitorData"] = vd; } catch { }
                        }
                    }

                    JToken secs = null;
                    JToken continuations = null;

                    if (continuation == null)
                    {
                        var tabs = data["contents"]?["singleColumnBrowseResultsRenderer"]?["tabs"];
                        if (tabs == null || !tabs.HasValues) break;
                        var sectionList = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"];
                        secs = sectionList?["contents"];
                        continuations = sectionList?["continuations"];
                    }
                    else
                    {
                        var sectionList = data["continuationContents"]?["sectionListContinuation"];
                        secs = sectionList?["contents"];
                        continuations = sectionList?["continuations"];
                    }

                    if (secs == null) break;

                    foreach (var sec in secs)
                    {
                        // musicCarouselShelfRenderer = horizontal carousel (most common)
                        var carousel = sec["musicCarouselShelfRenderer"];
                    if (carousel != null)
                    {
                        string sectionTitle = "";
                        var hdr = carousel["header"]?["musicCarouselShelfBasicHeaderRenderer"];
                        if (hdr != null)
                        {
                            sectionTitle = hdr["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                        }

                        if (string.IsNullOrEmpty(sectionTitle)) continue;

                        var homeSection = new HomeSection { Title = sectionTitle };
                        string lowerTitle = sectionTitle.ToLowerInvariant();
                        if (lowerTitle.Contains("nhanh") || lowerTitle.Contains("quick") || lowerTitle.Contains("start radio") || lowerTitle.Contains("bắt đầu một đài phát"))
                            homeSection.Layout = HomeSectionLayout.QuickPicks;
                        else if (lowerTitle.Contains("video") || lowerTitle.Contains("trình diễn") || lowerTitle.Contains("biểu diễn"))
                            homeSection.Layout = HomeSectionLayout.Video;

                        var cItems = carousel["contents"];
                        if (cItems != null)
                        {
                            foreach (var cItem in cItems)
                            {
                                if (homeSection.Tracks.Count >= 20) break;
                                try
                                {
                                    // musicTwoRowItemRenderer (albums, playlists, singles)
                                    var twoRow = cItem["musicTwoRowItemRenderer"];
                                    if (twoRow != null)
                                    {
                                        string title = twoRow["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                        if (string.IsNullOrEmpty(title)) continue;

                                        string subtitle = "";
                                        var subRuns = twoRow["subtitle"]?["runs"];
                                        if (subRuns != null)
                                        {
                                            foreach (var sr in subRuns)
                                            {
                                                string st = sr["text"]?.ToString();
                                                if (st != null && st != " • " && st != " · ")
                                                    subtitle = st; // Take last meaningful part (usually artist)
                                            }
                                        }

                                        string thumbUrl = "";
                                        var thumbs = twoRow["thumbnailRenderer"]?["musicThumbnailRenderer"]
                                            ?["thumbnail"]?["thumbnails"];
                                        if (thumbs != null && thumbs.HasValues)
                                            thumbUrl = thumbs.Last?["url"]?.ToString() ?? "";

                                        // Get videoId or browseId
                                        string videoId = twoRow["navigationEndpoint"]
                                            ?["watchEndpoint"]?["videoId"]?.ToString();
                                        string browseId = twoRow["navigationEndpoint"]
                                            ?["browseEndpoint"]?["browseId"]?.ToString();
                                        string watchPlaylistId = twoRow["navigationEndpoint"]
                                            ?["watchPlaylistEndpoint"]?["playlistId"]?.ToString();

                                        string finalId = videoId ?? "";
                                        if (string.IsNullOrEmpty(finalId))
                                        {
                                            if (!string.IsNullOrEmpty(watchPlaylistId))
                                                finalId = "PLAYLIST:" + watchPlaylistId;
                                            else if (!string.IsNullOrEmpty(browseId))
                                            {
                                                if (browseId.StartsWith("MPREb_") || browseId.StartsWith("OLAK5"))
                                                    finalId = "PLAYLIST:" + browseId;
                                                else if (browseId.StartsWith("UC"))
                                                    finalId = "CHANNEL:" + browseId;
                                                else if (browseId.StartsWith("VL"))
                                                    finalId = "PLAYLIST:" + browseId.Substring(2);
                                            }
                                        }
                                        if (string.IsNullOrEmpty(finalId)) continue;

                                        homeSection.Tracks.Add(new YouTubeTrack
                                        {
                                            VideoId = finalId,
                                            Title = title,
                                            ChannelName = CleanChannelName(subtitle),
                                            ThumbnailUrl = thumbUrl
                                        });
                                        continue;
                                    }

                                    // musicResponsiveListItemRenderer (individual songs)
                                    var track = ParseMusicListItem(cItem);
                                    if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                        homeSection.Tracks.Add(track);
                                }
                                catch { continue; }
                            }
                        }

                        if (homeSection.Tracks.Count > 0)
                            sections.Add(homeSection);
                        continue;
                    }

                    // musicShelfRenderer = vertical list of songs
                    var shelf = sec["musicShelfRenderer"];
                    if (shelf != null)
                    {
                        string shelfTitle = shelf["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(shelfTitle)) continue;

                        var homeSection2 = new HomeSection { Title = shelfTitle };
                        var sItems = shelf["contents"];
                        if (sItems != null)
                        {
                            foreach (var sItem in sItems)
                            {
                                if (homeSection2.Tracks.Count >= 20) break;
                                try
                                {
                                    var track = ParseMusicListItem(sItem);
                                    if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                        homeSection2.Tracks.Add(track);
                                }
                                catch { continue; }
                            }
                        }
                        if (homeSection2.Tracks.Count > 0)
                            sections.Add(homeSection2);
                    }
                } // end foreach (sec)

                    onPageLoaded?.Invoke(new List<HomeSection>(sections));

                    if (continuations != null && continuations.HasValues)
                    {
                        continuation = continuations[0]?["nextContinuationData"]?["continuation"]?.ToString();
                        if (string.IsNullOrEmpty(continuation)) break;
                    }
                    else
                    {
                        break;
                    }
                } // end for (page)
            }
            catch { }
            return sections;
        }
    }
}
