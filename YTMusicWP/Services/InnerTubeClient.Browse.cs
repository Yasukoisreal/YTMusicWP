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
        public static async Task<PlaylistResult> BrowsePlaylistAsync(string playlistId)
        {
            var result = new PlaylistResult();
            try
            {
                string vd = await GetVisitorDataAsync();

                // MPREb_ = album browseId → use WEB_REMIX client with raw browseId
                bool isAlbum = playlistId.StartsWith("MPREb_") || playlistId.StartsWith("OLAK5");
                string browseId = isAlbum ? playlistId : (playlistId.StartsWith("VL") ? playlistId : "VL" + playlistId);
                
                var body = new JObject
                {
                    ["context"] = isAlbum ? BuildMusicContext(vd) : BuildWebContext(vd),
                    ["browseId"] = browseId
                };

                string apiUrl = isAlbum 
                    ? "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false"
                    : "https://www.youtube.com/youtubei/v1/browse?prettyPrint=false";

                var data = await PostInnerTubeAsync(apiUrl, body, isAlbum);

                // Title
                result.Title = data?["metadata"]?["playlistMetadataRenderer"]?["title"]?.ToString() ?? "";

                // Thumbnail from sidebar
                try
                {
                    var sidebar = data?["sidebar"];
                    if (sidebar != null)
                    {
                        string sidebarStr = sidebar.ToString();
                        // Quick search for thumbnail URL
                        string tMarker = "\"url\":\"https://i.ytimg.com";
                        int tIdx = sidebarStr.IndexOf(tMarker);
                        if (tIdx >= 0)
                        {
                            int tStart = tIdx + 7; // skip "url":"
                            int tEnd = sidebarStr.IndexOf("\"", tStart);
                            if (tEnd > tStart)
                                result.ThumbnailUrl = sidebarStr.Substring(tStart, tEnd - tStart);
                        }
                    }
                }
                catch { }

                // Tracks — lockupViewModel format (new YouTube)
                var tabs = data?["contents"]?["twoColumnBrowseResultsRenderer"]?["tabs"];
                if (tabs != null && tabs.HasValues)
                {
                    var sections = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];
                    if (sections != null)
                    {
                        foreach (var sec in sections)
                        {
                            var isr = sec["itemSectionRenderer"];
                            if (isr == null) continue;

                            var items = isr["contents"];
                            if (items == null) continue;

                            foreach (var item in items)
                            {
                                try
                                {
                                    var track = ParseLockupViewModel(item);
                                    if (track != null)
                                        result.Tracks.Add(track);
                                }
                                catch { continue; }
                            }
                        }
                    }
                }

                // Fallback for albums: singleColumnBrowseResultsRenderer → musicShelfRenderer
                if (result.Tracks.Count == 0)
                {
                    var tabs2 = data?["contents"]?["singleColumnBrowseResultsRenderer"]?["tabs"];
                    if (tabs2 != null && tabs2.HasValues)
                    {
                        var sections2 = tabs2[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];
                        if (sections2 != null)
                        {
                            foreach (var sec in sections2)
                            {
                                var shelf = sec["musicShelfRenderer"];
                                if (shelf == null) continue;
                                var shelfItems = shelf["contents"];
                                if (shelfItems == null) continue;
                                foreach (var sItem in shelfItems)
                                {
                                    try
                                    {
                                        var track = ParseMusicListItem(sItem);
                                        if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                            result.Tracks.Add(track);
                                    }
                                    catch { continue; }
                                }
                            }
                        }
                    }
                }

                // Album title fallback
                if (string.IsNullOrEmpty(result.Title))
                {
                    result.Title = data?["header"]?["musicImmersiveHeaderRenderer"]?["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                }
                // Album thumbnail fallback
                if (string.IsNullOrEmpty(result.ThumbnailUrl))
                {
                    var hdrThumbs = data?["header"]?["musicImmersiveHeaderRenderer"]?["thumbnail"]?["musicThumbnailRenderer"]?["thumbnail"]?["thumbnails"];
                    if (hdrThumbs != null && hdrThumbs.HasValues)
                        result.ThumbnailUrl = hdrThumbs.Last?["url"]?.ToString() ?? "";
                }
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

            var items = new List<DiscoverItem>();
            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["browseId"] = "FEmusic_explore"
                };

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

                if (items.Count > 0)
                {
                    _cachedDiscover = items;
                    _discoverCacheTime = DateTime.Now;
                }
            }
            catch { }
            return items;
        }

        // ==========================================
        // BROWSE HOME — YouTube Music Home Page sections
        // ==========================================
        public class HomeSection
        {
            public string Title { get; set; }
            public List<YouTubeTrack> Tracks { get; set; }
            public HomeSection() { Tracks = new List<YouTubeTrack>(); }
        }

        private static List<HomeSection> _cachedHomeSections = null;
        private static DateTime _homeCacheTime = DateTime.MinValue;

        public static async Task<List<HomeSection>> BrowseHomeAsync()
        {
            // Cache 2 hours
            if (_cachedHomeSections != null && _cachedHomeSections.Count > 0
                && (DateTime.Now - _homeCacheTime).TotalHours < 2)
                return _cachedHomeSections;

            var sections = new List<HomeSection>();
            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["browseId"] = "FE_music_home"
                };

                var data = await PostInnerTubeAsync(
                    "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false", body, true);

                // Parse: singleColumnBrowseResultsRenderer → tabs[0] → sectionListRenderer → contents[]
                var tabs = data?["contents"]?["singleColumnBrowseResultsRenderer"]?["tabs"];
                if (tabs == null || !tabs.HasValues) return sections;

                var secs = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];
                if (secs == null) return sections;

                foreach (var sec in secs)
                {
                    if (sections.Count >= 8) break; // Max 8 sections for home

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
                }

                if (sections.Count > 0)
                {
                    _cachedHomeSections = sections;
                    _homeCacheTime = DateTime.Now;
                }
            }
            catch { }
            return sections;
        }
    }
}
