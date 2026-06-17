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
    }
}
