using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace YTMusicWP
{
    public static partial class InnerTubeClient
    {
        public static async Task<PlaylistResult> BrowsePlaylistAsync(string playlistId, string continuationToken = null, string accessToken = null)
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
                    
                    // wAEB params often needed for full playlist track list in YouTube Music, but breaks system playlists like Liked Music (VLLM)
                    bool isSystemPlaylist = playlistId == "LM" || playlistId == "VLLM" || browseId == "VLLM" || playlistId == "VLLL" || browseId == "VLLL";
                    if (!isAlbum && !isSystemPlaylist)
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
                else if (!string.IsNullOrEmpty(accessToken))
                {
                    var extraBody = new JObject();
                    foreach (var prop in body.Properties())
                    {
                        if (prop.Name != "context") extraBody[prop.Name] = prop.Value;
                    }
                    data = await AuthInnerTubePostAsync("browse", extraBody, accessToken, "WEB_REMIX", "1.20260304.03.00");
                }
                else
                {
                    var dataStr = await PostInnerTubeAsync(apiUrl, body, true);
                    data = dataStr;
                }

                // If not continuation, parse Title, Thumbnail, Subtitle, Artist
                string albumArtistFallback = "";
                if (string.IsNullOrEmpty(continuationToken))
                {
                    result.Title = data?["header"]?.SelectToken("$..title.runs[0].text")?.ToString() 
                        ?? data?["metadata"]?["playlistMetadataRenderer"]?["title"]?.ToString()
                        ?? data?.SelectToken("$..musicResponsiveHeaderRenderer.title.runs[0].text")?.ToString()
                        ?? "";

                    result.ThumbnailUrl = data?["header"]?.SelectToken("$..thumbnails[0].url")?.ToString() 
                        ?? data?["microformat"]?.SelectToken("$..thumbnails[0].url")?.ToString() 
                        ?? "";

                    // === Extract subtitle + artist from multiple header formats ===
                    JToken subtitleRuns = null;

                    // 1) musicDetailHeaderRenderer (album old format)
                    var mdhr = data?["header"]?["musicDetailHeaderRenderer"];
                    if (mdhr != null)
                    {
                        subtitleRuns = mdhr["subtitle"]?["runs"];
                    }

                    // 2) musicEditablePlaylistDetailHeaderRenderer (editable playlists)
                    if (subtitleRuns == null)
                    {
                        var editable = data?["header"]?["musicEditablePlaylistDetailHeaderRenderer"];
                        if (editable != null)
                        {
                            subtitleRuns = editable["header"]?["musicDetailHeaderRenderer"]?["subtitle"]?["runs"];
                            if (subtitleRuns == null)
                                subtitleRuns = editable["header"]?["musicResponsiveHeaderRenderer"]?["subtitle"]?["runs"];
                        }
                    }

                    // 3) musicImmersiveHeaderRenderer (artist page, some playlists)
                    if (subtitleRuns == null)
                    {
                        subtitleRuns = data?["header"]?["musicImmersiveHeaderRenderer"]?["subtitle"]?["runs"];
                    }

                    // 4) musicVisualHeaderRenderer
                    if (subtitleRuns == null)
                    {
                        subtitleRuns = data?["header"]?["musicVisualHeaderRenderer"]?["subtitle"]?["runs"];
                    }

                    // 5) Generic deep-search fallback on header, contents, and entire data
                    if (subtitleRuns == null)
                    {
                        subtitleRuns = data?["header"]?.SelectToken("$..subtitle.runs");
                    }
                    if (subtitleRuns == null)
                    {
                        subtitleRuns = data?["contents"]?.SelectToken("$..musicDetailHeaderRenderer.subtitle.runs");
                    }
                    if (subtitleRuns == null)
                    {
                        subtitleRuns = data?["contents"]?.SelectToken("$..musicResponsiveHeaderRenderer.subtitle.runs");
                    }

                    JToken secondSubtitleRuns = mdhr?["secondSubtitle"]?["runs"] 
                        ?? data?["header"]?.SelectToken("$..secondSubtitle.runs") 
                        ?? data?["contents"]?.SelectToken("$..secondSubtitle.runs");

                    // First try explicit artist from new API format
                    var strapline = data?.SelectToken("$..straplineTextOne.runs");
                    if (strapline != null && strapline.HasValues)
                    {
                        albumArtistFallback = InnerTubeClient.ExtractArtistFromRuns(strapline);
                        if (string.IsNullOrEmpty(albumArtistFallback))
                        {
                            albumArtistFallback = strapline[0]?["text"]?.ToString() ?? "";
                        }
                    }

                    if (subtitleRuns != null && subtitleRuns.HasValues)
                    {
                        string subtitle = "";
                        foreach(var r in subtitleRuns) subtitle += r["text"]?.ToString();
                        result.Subtitle = subtitle;
                        
                        // If we didn't get artist from strapline, try extracting from subtitle
                        if (string.IsNullOrEmpty(albumArtistFallback))
                        {
                            albumArtistFallback = InnerTubeClient.ExtractArtistFromRuns(subtitleRuns);
                        }
                    }

                    // If still no artist, try secondSubtitle
                    if (string.IsNullOrEmpty(albumArtistFallback) && secondSubtitleRuns != null && secondSubtitleRuns.HasValues)
                    {
                        albumArtistFallback = InnerTubeClient.ExtractArtistFromRuns(secondSubtitleRuns);
                    }

                    // Append artist to subtitle if it was missing from the main subtitle but we found it
                    if (!string.IsNullOrEmpty(albumArtistFallback) && !string.IsNullOrEmpty(result.Subtitle) && !result.Subtitle.Contains(albumArtistFallback))
                    {
                        result.Subtitle = result.Subtitle + " • " + albumArtistFallback;
                    }
                    
                    // Also append secondSubtitle if it exists (for track count / duration)
                    if (secondSubtitleRuns != null && secondSubtitleRuns.HasValues)
                    {
                        string secondSub = "";
                        foreach (var r in secondSubtitleRuns) secondSub += r["text"]?.ToString();
                        if (!string.IsNullOrEmpty(secondSub))
                        {
                            result.Subtitle = result.Subtitle + " • " + secondSub;
                        }
                    }

                    // 7) Fallback: metadata.playlistMetadataRenderer.description for subtitle
                    if (string.IsNullOrEmpty(result.Subtitle))
                    {
                        result.Subtitle = data?["metadata"]?["playlistMetadataRenderer"]?["description"]?.ToString() ?? "";
                    }

                    System.Diagnostics.Debug.WriteLine("[BrowsePlaylist] Title=" + result.Title + " Subtitle=" + result.Subtitle + " ArtistFallback=" + albumArtistFallback);
                }

                // Parse tracks and continuation token scoped strictly to the playlist/album shelf
                string newToken = null;

                if (string.IsNullOrEmpty(continuationToken))
                {
                    // Initial browse: locate the playlist or album shelf
                    JToken shelf = data?.SelectToken("$..musicPlaylistShelfRenderer") 
                                ?? data?.SelectToken("$..musicShelfRenderer");

                    if (shelf != null)
                    {
                        var shelfContents = shelf["contents"] as JArray;
                        if (shelfContents != null)
                        {
                            foreach (var item in shelfContents)
                            {
                                try
                                {
                                    var mrlir = item["musicResponsiveListItemRenderer"];
                                    if (mrlir != null)
                                    {
                                        var wrapper = new JObject { ["musicResponsiveListItemRenderer"] = mrlir };
                                        var track = ParseMusicListItem(wrapper);
                                        if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                        {
                                            if (string.IsNullOrEmpty(track.ChannelName) && !string.IsNullOrEmpty(albumArtistFallback))
                                            {
                                                track.ChannelName = albumArtistFallback;
                                            }
                                            if (string.IsNullOrEmpty(track.ThumbnailUrl) && !string.IsNullOrEmpty(result.ThumbnailUrl))
                                            {
                                                track.ThumbnailUrl = result.ThumbnailUrl;
                                            }
                                            result.Tracks.Add(track);
                                        }
                                    }
                                    else if (item["continuationItemRenderer"] != null)
                                    {
                                        var cir = item["continuationItemRenderer"];
                                        newToken = cir?["continuationEndpoint"]?["continuationCommand"]?["token"]?.ToString();
                                    }
                                }
                                catch { continue; }
                            }
                        }

                        // Also check shelf.continuations if continuationItemRenderer was not found
                        if (string.IsNullOrEmpty(newToken))
                        {
                            var shelfConts = shelf["continuations"] as JArray;
                            if (shelfConts != null && shelfConts.Count > 0)
                            {
                                newToken = shelfConts[0]?["nextContinuationData"]?["continuation"]?.ToString()
                                        ?? shelfConts[0]?["continuationCommand"]?["token"]?.ToString();
                            }
                        }
                    }
                    else
                    {
                        // Fallback in case shelf wasn't matched
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
                                    {
                                        if (string.IsNullOrEmpty(track.ChannelName) && !string.IsNullOrEmpty(albumArtistFallback))
                                        {
                                            track.ChannelName = albumArtistFallback;
                                        }
                                        if (string.IsNullOrEmpty(track.ThumbnailUrl) && !string.IsNullOrEmpty(result.ThumbnailUrl))
                                        {
                                            track.ThumbnailUrl = result.ThumbnailUrl;
                                        }
                                        result.Tracks.Add(track);
                                    }
                                }
                                catch { continue; }
                            }
                        }
                    }
                }
                else
                {
                    // Continuation browse: check appendContinuationItemsAction first, then continuationContents
                    var actionItems = data?.SelectToken("$..appendContinuationItemsAction.continuationItems") as JArray;
                    if (actionItems != null)
                    {
                        foreach (var item in actionItems)
                        {
                            try
                            {
                                var mrlir = item["musicResponsiveListItemRenderer"];
                                if (mrlir != null)
                                {
                                    var wrapper = new JObject { ["musicResponsiveListItemRenderer"] = mrlir };
                                    var track = ParseMusicListItem(wrapper);
                                    if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                    {
                                        result.Tracks.Add(track);
                                    }
                                }
                                else if (item["continuationItemRenderer"] != null)
                                {
                                    var cir = item["continuationItemRenderer"];
                                    newToken = cir?["continuationEndpoint"]?["continuationCommand"]?["token"]?.ToString();
                                }
                            }
                            catch { continue; }
                        }
                    }
                    else
                    {
                        // Check continuationContents -> musicPlaylistShelfContinuation or musicShelfContinuation
                        var shelfCont = data?.SelectToken("$..musicPlaylistShelfContinuation") 
                                     ?? data?.SelectToken("$..musicShelfContinuation");

                        if (shelfCont != null)
                        {
                            var contContents = shelfCont["contents"] as JArray;
                            if (contContents != null)
                            {
                                foreach (var item in contContents)
                                {
                                    try
                                    {
                                        var mrlir = item["musicResponsiveListItemRenderer"];
                                        if (mrlir != null)
                                        {
                                            var wrapper = new JObject { ["musicResponsiveListItemRenderer"] = mrlir };
                                            var track = ParseMusicListItem(wrapper);
                                            if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                            {
                                                result.Tracks.Add(track);
                                            }
                                        }
                                        else if (item["continuationItemRenderer"] != null)
                                        {
                                            var cir = item["continuationItemRenderer"];
                                            newToken = cir?["continuationEndpoint"]?["continuationCommand"]?["token"]?.ToString();
                                        }
                                    }
                                    catch { continue; }
                                }
                            }

                            if (string.IsNullOrEmpty(newToken))
                            {
                                var conts = shelfCont["continuations"] as JArray;
                                if (conts != null && conts.Count > 0)
                                {
                                    newToken = conts[0]?["nextContinuationData"]?["continuation"]?.ToString()
                                            ?? conts[0]?["continuationCommand"]?["token"]?.ToString();
                                }
                            }
                        }
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
                        var descRuns = mih["description"]?["runs"];
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
                                            double coverWidth = 140;
                                            var thumbs = twoRow["thumbnailRenderer"]?["musicThumbnailRenderer"]
                                                ?["thumbnail"]?["thumbnails"];
                                            if (thumbs != null && thumbs.HasValues)
                                            {
                                                itemThumb = thumbs.Last?["url"]?.ToString() ?? "";
                                                int tW = 0, tH = 0;
                                                int.TryParse(thumbs.Last?["width"]?.ToString() ?? "0", out tW);
                                                int.TryParse(thumbs.Last?["height"]?.ToString() ?? "0", out tH);
                                                if (tH > 0 && (double)tW / tH > 1.3) coverWidth = 249;
                                            }

                                            string browseId2 = twoRow["navigationEndpoint"]
                                                ?["browseEndpoint"]?["browseId"]?.ToString() ?? "";

                                            string videoId = twoRow["navigationEndpoint"]
                                                ?["watchEndpoint"]?["videoId"]?.ToString() ?? "";
                                                
                                            string playlistId = twoRow["navigationEndpoint"]
                                                ?["watchEndpoint"]?["playlistId"]?.ToString() ?? "";

                                            result.Albums.Add(new ArtistAlbum
                                            {
                                                Title = itemTitle,
                                                Subtitle = itemSub,
                                                ThumbnailUrl = itemThumb,
                                                BrowseId = browseId2,
                                                VideoId = videoId,
                                                PlaylistId = playlistId,
                                                SectionTitle = sectionTitle,
                                                CoverWidth = coverWidth
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

        public static async Task<List<HomeSection>> BrowseMoodCategoryAsync(string browseId, string paramsStr)
        {
            var sectionsList = new List<HomeSection>();
            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["browseId"] = browseId
                };
                if (!string.IsNullOrEmpty(paramsStr))
                {
                    body["params"] = paramsStr;
                }

                JObject data = null;
                if (HasCookieAuth)
                {
                    var extraBody = new JObject();
                    extraBody["browseId"] = browseId;
                    if (!string.IsNullOrEmpty(paramsStr)) extraBody["params"] = paramsStr;
                    data = await CookieInnerTubePostAsync("browse", extraBody, "WEB_REMIX", "1.20260304.03.00");
                }
                else
                {
                    var dataStr = await PostInnerTubeAsync(
                        "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false", body, true);
                    data = dataStr;
                }

                var tabs = data?["contents"]?["singleColumnBrowseResultsRenderer"]?["tabs"];
                var sections = tabs?[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];

                if (sections != null)
                {
                    foreach (var sec in sections)
                    {
                        var carousel = sec["musicCarouselShelfRenderer"];
                        if (carousel != null)
                        {
                            string sectionTitle = carousel["header"]?["musicCarouselShelfBasicHeaderRenderer"]?["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                            if (string.IsNullOrEmpty(sectionTitle)) continue;

                            var homeSection = new HomeSection { Title = sectionTitle };
                            string lowerTitle = sectionTitle.ToLowerInvariant();
                            if (lowerTitle.Contains("nhanh") || lowerTitle.Contains("quick") || lowerTitle.Contains("start radio") || lowerTitle.Contains("đài phát"))
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
                                        var twoRow = cItem["musicTwoRowItemRenderer"];
                                        if (twoRow != null)
                                        {
                                            string title = twoRow["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                            if (string.IsNullOrEmpty(title)) continue;

                                            string subtitle = "";
                                            var subRuns = twoRow["subtitle"]?["runs"];
                                            if (subRuns != null)
                                            {
                                                subtitle = ExtractArtistFromRuns(subRuns);
                                            }

                                            string thumbUrl = "";
                                            var thumbs = twoRow["thumbnailRenderer"]?["musicThumbnailRenderer"]?["thumbnail"]?["thumbnails"];
                                            if (thumbs != null && thumbs.HasValues)
                                                thumbUrl = thumbs.Last?["url"]?.ToString() ?? "";

                                            string itemBrowseId = twoRow["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                                            string vId = twoRow["navigationEndpoint"]?["watchEndpoint"]?["videoId"]?.ToString();

                                            string finalId = vId;
                                            if (string.IsNullOrEmpty(finalId) && !string.IsNullOrEmpty(itemBrowseId))
                                            {
                                                if (itemBrowseId.StartsWith("MPREb_") || itemBrowseId.StartsWith("OLAK5"))
                                                    finalId = "PLAYLIST:" + itemBrowseId;
                                                else if (itemBrowseId.StartsWith("UC"))
                                                    finalId = "CHANNEL:" + itemBrowseId;
                                                else if (itemBrowseId.StartsWith("VL"))
                                                    finalId = "PLAYLIST:" + itemBrowseId.Substring(2);
                                                else
                                                    finalId = "PLAYLIST:" + itemBrowseId;
                                            }

                                            if (!string.IsNullOrEmpty(finalId))
                                            {
                                                homeSection.Tracks.Add(new YouTubeTrack
                                                {
                                                    Title = title,
                                                    ChannelName = subtitle,
                                                    ThumbnailUrl = thumbUrl,
                                                    VideoId = finalId
                                                });
                                            }
                                        }
                                    }
                                    catch { continue; }
                                }
                            }
                            if (homeSection.Tracks.Count > 0)
                            {
                                sectionsList.Add(homeSection);
                            }
                        }
                    }
                }
            }
            catch { }
            return sectionsList;
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
            Normal,             // 1-row horizontal carousel (Albums, Playlists)
            QuickPicks,         // Legacy/Compat
            MultiTrackColumn,   // 4-track vertical columns carousel with "Phát tất cả" button
            SpeedDial,          // 3x3 square grid with progress bar and pagination dots ("Phát nhanh")
            FeaturedCard,       // Large card with cover, 3 preview tracks, and 3 round action buttons
            MostDiscussed,      // Cards with track info + quoted comment bubble & comment count
            LandscapeVideo,     // 16:9 widescreen video cards with duration
            Video,              // Compat
            EditorialBanner     // Promotional discovery banner
        }

        public class HomeTrackColumn
        {
            public List<YouTubeTrack> Tracks { get; set; }
            public HomeTrackColumn() { Tracks = new List<YouTubeTrack>(); }
        }

        public class HomeSpeedDialPage
        {
            public List<YouTubeTrack> Items { get; set; }
            public double PageWidth { get; set; }
            public Windows.UI.Xaml.Thickness PageMargin { get; set; }
            public HomeSpeedDialPage()
            {
                Items = new List<YouTubeTrack>();
                PageWidth = 390;
                PageMargin = new Windows.UI.Xaml.Thickness(0, 0, 20, 0);
            }
        }

        public class HomeSection : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string propName)
            {
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs(propName));
            }

            public string Title { get; set; }
            public string Subtitle { get; set; }
            public string AvatarUrl { get; set; }
            public string UserInitial { get; set; }

            private Windows.UI.Xaml.Thickness _speedDialHeaderMargin = new Windows.UI.Xaml.Thickness(10, 0, 10, 12);
            public Windows.UI.Xaml.Thickness SpeedDialHeaderMargin
            {
                get { return _speedDialHeaderMargin; }
                set
                {
                    _speedDialHeaderMargin = value;
                    OnPropertyChanged("SpeedDialHeaderMargin");
                }
            }

            private Windows.UI.Xaml.Thickness _speedDialListViewMargin = new Windows.UI.Xaml.Thickness(10, 0, 0, 0);
            public Windows.UI.Xaml.Thickness SpeedDialListViewMargin
            {
                get { return _speedDialListViewMargin; }
                set
                {
                    _speedDialListViewMargin = value;
                    OnPropertyChanged("SpeedDialListViewMargin");
                }
            }

            public Windows.UI.Xaml.Visibility AvatarImageVisibility
            {
                get { return !string.IsNullOrEmpty(AvatarUrl) ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed; }
            }

            public Windows.UI.Xaml.Visibility AvatarFallbackVisibility
            {
                get { return string.IsNullOrEmpty(AvatarUrl) ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed; }
            }

            private int _activePageIndex = 0;
            public int ActivePageIndex
            {
                get { return _activePageIndex; }
                set
                {
                    if (_activePageIndex != value)
                    {
                        _activePageIndex = value;
                        OnPropertyChanged("ActivePageIndex");
                        OnPropertyChanged("Page1DotBrush");
                        OnPropertyChanged("Page2DotBrush");
                    }
                }
            }

            private static readonly Windows.UI.Xaml.Media.SolidColorBrush _activeDotBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
            private static readonly Windows.UI.Xaml.Media.SolidColorBrush _inactiveDotBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 85, 85));

            public Windows.UI.Xaml.Media.Brush Page1DotBrush
            {
                get { return _activePageIndex == 0 ? _activeDotBrush : _inactiveDotBrush; }
            }

            public Windows.UI.Xaml.Media.Brush Page2DotBrush
            {
                get { return _activePageIndex == 1 ? _activeDotBrush : _inactiveDotBrush; }
            }

            public Windows.UI.Xaml.Visibility PaginationVisibility
            {
                get { return (SpeedDialPages != null && SpeedDialPages.Count > 1) ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed; }
            }

            public string CategoryTag { get; set; }
            public string FeaturedCoverUrl { get; set; }
            public string CardBgColor { get; set; }
            public HomeSectionLayout Layout { get; set; }
            public List<YouTubeTrack> Tracks { get; set; }
            public List<HomeTrackColumn> TrackColumns { get; set; }
            public List<HomeSpeedDialPage> SpeedDialPages { get; set; }

            public YouTubeTrack FirstTrack { get { return (Tracks != null && Tracks.Count > 0) ? Tracks[0] : null; } }
            public YouTubeTrack SecondTrack { get { return (Tracks != null && Tracks.Count > 1) ? Tracks[1] : null; } }
            public YouTubeTrack ThirdTrack { get { return (Tracks != null && Tracks.Count > 2) ? Tracks[2] : null; } }

            public HomeSection()
            {
                Tracks = new List<YouTubeTrack>();
                TrackColumns = new List<HomeTrackColumn>();
                SpeedDialPages = new List<HomeSpeedDialPage>();
                Layout = HomeSectionLayout.Normal;
                CardBgColor = "#1C1824";
                CategoryTag = "FEATURED";
                UserInitial = "Y";
            }

            public void PopulateColumns(int chunkSize = 4)
            {
                if (Tracks == null) return;
                TrackColumns = new List<HomeTrackColumn>();
                for (int i = 0; i < Tracks.Count; i += chunkSize)
                {
                    var col = new HomeTrackColumn();
                    for (int j = i; j < Math.Min(i + chunkSize, Tracks.Count); j++)
                    {
                        col.Tracks.Add(Tracks[j]);
                    }
                    TrackColumns.Add(col);
                }
            }

            public void PopulateSpeedDialPages(int pageSize = 9, double screenWidth = 0)
            {
                if (Tracks == null) return;
                SpeedDialPages = new List<HomeSpeedDialPage>();

                if (screenWidth < 300) screenWidth = 400;
                double gap = (screenWidth >= 400) ? 10 : 8;
                double cardSize = Math.Floor((screenWidth - (4 * gap)) / 3.0);
                if (cardSize < 90) cardSize = 112;
                double pWidth = 3 * (cardSize + gap);

                SpeedDialHeaderMargin = new Windows.UI.Xaml.Thickness(gap, 0, gap, 12);
                SpeedDialListViewMargin = new Windows.UI.Xaml.Thickness(gap, 0, 0, 0);

                var cardMargin = new Windows.UI.Xaml.Thickness(0, 0, gap, gap);
                var pageMargin = new Windows.UI.Xaml.Thickness(0, 0, gap * 2, 0);

                for (int i = 0; i < Tracks.Count; i += pageSize)
                {
                    var page = new HomeSpeedDialPage 
                    { 
                        PageWidth = pWidth,
                        PageMargin = pageMargin
                    };
                    for (int j = i; j < Math.Min(i + pageSize, Tracks.Count); j++)
                    {
                        var track = Tracks[j];
                        track.SpeedDialCardSize = cardSize;
                        track.SpeedDialCardMargin = cardMargin;
                        page.Items.Add(track);
                    }
                    SpeedDialPages.Add(page);
                }
                OnPropertyChanged("PaginationVisibility");
                OnPropertyChanged("SpeedDialPages");
            }
        }

        public static void ClearHomeCache()
        {
            _cachedDiscover = null;
            _cachedCharts = null;
            _cachedMoods = null;
            _cachedVisitorData = null;
            try { Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove("CachedVisitorData"); } catch { }
        }

        public class HomeChipItem
        {
            public string Title { get; set; }
            public string Params { get; set; }
            public bool IsSelected { get; set; }
        }

        public static readonly List<HomeChipItem> DefaultMoodChips = new List<HomeChipItem>
        {
            new HomeChipItem { Title = "Energize", Params = "ggM8SgQICRADSgQICBABSgQIBxABSgQIDhABSgQIBBABSgQIAxABSgQIDRABSgQIChABSgQIBhABSgQIBRAB" },
            new HomeChipItem { Title = "Feel good", Params = "ggM8SgQICRABSgQICBADSgQIBxABSgQIDhABSgQIBBABSgQIAxABSgQIDRABSgQIChABSgQIBhABSgQIBRAB" },
            new HomeChipItem { Title = "Relax", Params = "ggM8SgQICRABSgQICBABSgQIBxADSgQIDhABSgQIBBABSgQIAxABSgQIDRABSgQIChABSgQIBhABSgQIBRAB" },
            new HomeChipItem { Title = "Party", Params = "ggM8SgQICRABSgQICBABSgQIBxABSgQIDhADSgQIBBABSgQIAxABSgQIDRABSgQIChABSgQIBhABSgQIBRAB" },
            new HomeChipItem { Title = "Workout", Params = "ggM8SgQICRABSgQICBABSgQIBxABSgQIDhABSgQIBBADSgQIAxABSgQIDRABSgQIChABSgQIBhABSgQIBRAB" },
            new HomeChipItem { Title = "Commute", Params = "ggM8SgQICRABSgQICBABSgQIBxABSgQIDhABSgQIBBABSgQIAxADSgQIDRABSgQIChABSgQIBhABSgQIBRAB" },
            new HomeChipItem { Title = "Romance", Params = "ggM8SgQICRABSgQICBABSgQIBxABSgQIDhABSgQIBBABSgQIAxABSgQIDRADSgQIChABSgQIBhABSgQIBRAB" },
            new HomeChipItem { Title = "Sad", Params = "ggM8SgQICRABSgQICBABSgQIBxABSgQIDhABSgQIBBABSgQIAxABSgQIDRABSgQIChADSgQIBhABSgQIBRAB" },
            new HomeChipItem { Title = "Focus", Params = "ggM8SgQICRABSgQICBABSgQIBxABSgQIDhABSgQIBBABSgQIAxABSgQIDRABSgQIChABSgQIBhADSgQIBRAB" },
            new HomeChipItem { Title = "Sleep", Params = "ggM8SgQICRABSgQICBABSgQIBxABSgQIDhABSgQIBBABSgQIAxABSgQIDRABSgQIChABSgQIBhABSgQIBRAD" }
        };

        public class HomeBrowseResult
        {
            public List<HomeSection> Sections { get; set; }
            public string ContinuationToken { get; set; }
            public List<HomeChipItem> Chips { get; set; }
            public HomeBrowseResult()
            {
                Sections = new List<HomeSection>();
                Chips = new List<HomeChipItem>();
            }
        }

        public static string LocalizeHomeSectionTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;
            if (CurrentLanguage == "vi")
            {
                string t = title.Trim();
                if (t.Equals("Shorts Featured Section VN", StringComparison.OrdinalIgnoreCase)) return "Shorts nổi bật";
                if (t.Equals("Shorts Featured Section", StringComparison.OrdinalIgnoreCase)) return "Shorts nổi bật";
                if (t.Equals("Top Charts", StringComparison.OrdinalIgnoreCase)) return "Bảng xếp hạng hàng đầu";
                if (t.Equals("Made for you", StringComparison.OrdinalIgnoreCase)) return "Dành cho bạn";
                if (t.Equals("Mixed for you", StringComparison.OrdinalIgnoreCase)) return "Bản kết hợp dành cho bạn";
                if (t.Equals("Recently played artists", StringComparison.OrdinalIgnoreCase)) return "Nghệ sĩ nghe gần đây";
                if (t.Equals("Jump back in", StringComparison.OrdinalIgnoreCase)) return "Nghe lại";
                if (t.Equals("Trending", StringComparison.OrdinalIgnoreCase)) return "Thịnh hành";
                if (t.Equals("New releases", StringComparison.OrdinalIgnoreCase)) return "Bản phát hành mới";
                if (t.Equals("Forgotten favorites", StringComparison.OrdinalIgnoreCase)) return "Bài hát yêu thích";
                if (t.Equals("Quick picks", StringComparison.OrdinalIgnoreCase)) return "Lựa chọn nhanh";
                if (t.Equals("Recommended music videos", StringComparison.OrdinalIgnoreCase)) return "Video âm nhạc đề xuất";
                if (t.Equals("Similar to", StringComparison.OrdinalIgnoreCase)) return "Tương tự";
                if (t.Equals("Today's Hits", StringComparison.OrdinalIgnoreCase)) return "Bản hit hôm nay";
                if (t.Equals("Energize", StringComparison.OrdinalIgnoreCase)) return "Nạp năng lượng";
                if (t.Equals("Feel good", StringComparison.OrdinalIgnoreCase)) return "Cảm thấy vui vẻ";
                if (t.Equals("Relax", StringComparison.OrdinalIgnoreCase)) return "Thư giãn";
                if (t.Equals("Party", StringComparison.OrdinalIgnoreCase)) return "Tiệc tùng";
                if (t.Equals("Workout", StringComparison.OrdinalIgnoreCase)) return "Tập luyện";
                if (t.Equals("Commute", StringComparison.OrdinalIgnoreCase)) return "Đi lại";
                if (t.Equals("Romance", StringComparison.OrdinalIgnoreCase)) return "Lãng mạn";
                if (t.Equals("Sad", StringComparison.OrdinalIgnoreCase)) return "Buồn bã";
                if (t.Equals("Focus", StringComparison.OrdinalIgnoreCase)) return "Tập trung";
                if (t.Equals("Sleep", StringComparison.OrdinalIgnoreCase)) return "Đi ngủ";
            }
            return title;
        }

        private static void ParseHomeSectionList(JToken secs, List<HomeSection> targetList)
        {
            if (secs == null) return;

            foreach (var sec in secs)
            {
                // musicCarouselShelfRenderer = horizontal carousel (most common)
                var carousel = sec["musicCarouselShelfRenderer"];
                if (carousel != null)
                {
                    string sectionTitle = "";
                    string sectionSubtitle = "";
                    var hdr = carousel["header"]?["musicCarouselShelfBasicHeaderRenderer"];
                    if (hdr != null)
                    {
                        sectionTitle = hdr["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                        sectionSubtitle = hdr["strapline"]?["runs"]?[0]?["text"]?.ToString()
                            ?? hdr["subtitle"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                    }

                    if (string.IsNullOrEmpty(sectionTitle)) continue;
                    sectionTitle = LocalizeHomeSectionTitle(sectionTitle);

                    var homeSection = new HomeSection { Title = sectionTitle, Subtitle = sectionSubtitle };
                    
                    string numItemsPerColStr = carousel["numItemsPerColumn"]?.ToString();
                    int numItemsPerCol = 0;
                    if (!string.IsNullOrEmpty(numItemsPerColStr))
                    {
                        int.TryParse(numItemsPerColStr, out numItemsPerCol);
                    }

                    bool hasResponsiveListItems = false;
                    int wideThumbCount = 0;
                    int totalItemCount = 0;

                    var cItems = carousel["contents"];
                    if (cItems != null)
                    {
                        foreach (var cItem in cItems)
                        {
                            if (homeSection.Tracks.Count >= 20) break;
                            try
                            {
                                var twoRow = cItem["musicTwoRowItemRenderer"];
                                if (twoRow != null)
                                {
                                    totalItemCount++;
                                    string title = twoRow["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                    string subtitle = twoRow["subtitle"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                    var subRuns = twoRow["subtitle"]?["runs"];
                                    if (subRuns != null)
                                    {
                                        subtitle = ExtractArtistFromRuns(subRuns);
                                    }

                                    string thumbUrl = "";
                                    double coverWidth = 140; // Default 1:1
                                    var thumbs = twoRow["thumbnailRenderer"]?["musicThumbnailRenderer"]
                                        ?["thumbnail"]?["thumbnails"];
                                    if (thumbs != null && thumbs.HasValues)
                                    {
                                        var lastThumb = thumbs.Last;
                                        thumbUrl = lastThumb?["url"]?.ToString() ?? "";
                                        
                                        // Check aspect ratio to automatically display 16:9 thumbnails properly
                                        int w = 0, h = 0;
                                        int.TryParse(lastThumb?["width"]?.ToString(), out w);
                                        int.TryParse(lastThumb?["height"]?.ToString(), out h);
                                        if (w > 0 && h > 0)
                                        {
                                            double ratio = (double)w / h;
                                            if (ratio > 1.3) // 16:9 is 1.77, anything > 1.3 is widescreen
                                            {
                                                coverWidth = 260; // Wide width matching VideoItemTemplate
                                                wideThumbCount++;
                                            }
                                        }
                                    }

                                    string videoId = twoRow["navigationEndpoint"]?["watchEndpoint"]?["videoId"]?.ToString();
                                    string browseId = twoRow["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                                    string playlistId = twoRow["navigationEndpoint"]?["watchEndpoint"]?["playlistId"]?.ToString();

                                    string finalId = videoId;
                                    if (string.IsNullOrEmpty(finalId))
                                    {
                                        if (!string.IsNullOrEmpty(browseId))
                                        {
                                            if (browseId.StartsWith("VLPL") || browseId.StartsWith("VL"))
                                                finalId = "PLAYLIST:" + browseId.Substring(2);
                                            else if (browseId.StartsWith("MPRE") || browseId.StartsWith("FEmusic_library"))
                                                finalId = "PLAYLIST:" + browseId;
                                            else if (browseId.StartsWith("UC") || browseId.StartsWith("FEmusic_artist"))
                                                finalId = "CHANNEL:" + browseId;
                                            else
                                                finalId = "PLAYLIST:" + browseId;
                                        }
                                        else if (!string.IsNullOrEmpty(playlistId))
                                        {
                                            finalId = "PLAYLIST:" + playlistId;
                                        }
                                    }

                                    if (string.IsNullOrEmpty(finalId)) continue;

                                    homeSection.Tracks.Add(new YouTubeTrack
                                    {
                                        VideoId = finalId,
                                        Title = title,
                                        ChannelName = CleanChannelName(subtitle),
                                        ThumbnailUrl = thumbUrl,
                                        CoverWidth = coverWidth
                                    });
                                    continue;
                                }

                                // musicResponsiveListItemRenderer (individual songs)
                                if (cItem["musicResponsiveListItemRenderer"] != null)
                                {
                                    hasResponsiveListItems = true;
                                }
                                var track = ParseMusicListItem(cItem);
                                if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                    homeSection.Tracks.Add(track);
                            }
                            catch { continue; }
                        }
                    }

                    if (homeSection.Tracks.Count > 0)
                    {
                        bool hasPlaylistsOrAlbums = homeSection.Tracks.Any(t => t.VideoId != null && (t.VideoId.StartsWith("PLAYLIST:") || t.VideoId.StartsWith("CHANNEL:")));
                        if (!hasPlaylistsOrAlbums && (numItemsPerCol >= 2 || hasResponsiveListItems))
                        {
                            homeSection.Layout = HomeSectionLayout.MultiTrackColumn;
                            int colSize = (numItemsPerCol >= 2) ? numItemsPerCol : 4;
                            homeSection.PopulateColumns(colSize);
                        }
                        else if (totalItemCount > 0 && ((double)wideThumbCount / totalItemCount) > 0.5)
                        {
                            homeSection.Layout = HomeSectionLayout.LandscapeVideo;
                            foreach (var t in homeSection.Tracks) { t.CoverWidth = 260; }
                        }
                        else
                        {
                            homeSection.Layout = HomeSectionLayout.Normal;
                        }
                        targetList.Add(homeSection);
                    }
                    continue;
                }

                // musicCardShelfRenderer = Featured Card (e.g. "Dựa trên thư viện của bạn", "Tập thể dục", "Recap")
                var cardShelf = sec["musicCardShelfRenderer"];
                if (cardShelf != null)
                {
                    string cardTitle = cardShelf["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                    string cardSubtitle = cardShelf["subtitle"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                    string defaultStrapline = (CurrentLanguage == "vi") ? "DỰA TRÊN THƯ VIỆN CỦA BẠN" : "BASED ON YOUR LIBRARY";
                    string strapline = cardShelf["header"]?["musicCardShelfHeaderBasicRenderer"]?["strapline"]?["runs"]?[0]?["text"]?.ToString() ?? defaultStrapline;
                    if (string.IsNullOrEmpty(cardTitle) && cardShelf["header"] != null)
                    {
                        cardTitle = cardShelf["header"]?["musicCardShelfHeaderBasicRenderer"]?["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                    }

                    if (!string.IsNullOrEmpty(cardTitle))
                    {
                        cardTitle = LocalizeHomeSectionTitle(cardTitle);
                        var homeSectionCard = new HomeSection
                        {
                            Title = cardTitle,
                            Subtitle = cardSubtitle,
                            CategoryTag = strapline.ToUpperInvariant(),
                            Layout = HomeSectionLayout.FeaturedCard
                        };

                        var thumbs = cardShelf["thumbnail"]?["musicThumbnailRenderer"]?["thumbnail"]?["thumbnails"];
                        if (thumbs != null && thumbs.HasValues)
                        {
                            homeSectionCard.FeaturedCoverUrl = thumbs.Last?["url"]?.ToString() ?? "";
                        }

                        var cItems = cardShelf["contents"];
                        if (cItems != null)
                        {
                            foreach (var cItem in cItems)
                            {
                                if (homeSectionCard.Tracks.Count >= 10) break;
                                try
                                {
                                    var track = ParseMusicListItem(cItem);
                                    if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                        homeSectionCard.Tracks.Add(track);
                                }
                                catch { }
                            }
                        }

                        if (homeSectionCard.Tracks.Count > 0)
                        {
                            if (string.IsNullOrEmpty(homeSectionCard.FeaturedCoverUrl))
                                homeSectionCard.FeaturedCoverUrl = homeSectionCard.Tracks[0].ThumbnailUrl;
                            targetList.Add(homeSectionCard);
                        }
                    }
                    continue;
                }

                // musicShelfRenderer = vertical list of songs
                var shelf = sec["musicShelfRenderer"];
                if (shelf != null)
                {
                    string shelfTitle = shelf["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(shelfTitle)) continue;
                    shelfTitle = LocalizeHomeSectionTitle(shelfTitle);

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
                    {
                        homeSection2.Layout = HomeSectionLayout.MultiTrackColumn;
                        homeSection2.PopulateColumns(4);
                        targetList.Add(homeSection2);
                    }
                }
            }
        }

        public static async Task<HomeBrowseResult> BrowseHomeFirstPageAsync(string filterParams = null, string accessToken = null)
        {
            var result = new HomeBrowseResult();
            try
            {
                string vd = await GetVisitorDataAsync();
                JObject data = null;

                if (HasCookieAuth)
                {
                    var extraParams = new JObject
                    {
                        ["browseId"] = "FEmusic_home"
                    };
                    if (!string.IsNullOrEmpty(filterParams))
                    {
                        extraParams["params"] = filterParams;
                    }
                    data = await CookieInnerTubePostAsync("browse", extraParams);
                }
                else
                {
                    var body = new JObject
                    {
                        ["context"] = BuildMusicContext(vd),
                        ["browseId"] = "FEmusic_home"
                    };
                    if (!string.IsNullOrEmpty(filterParams))
                    {
                        body["params"] = filterParams;
                    }
                    string url = "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false";
                    data = await PostInnerTubeAsync(url, body, true);
                }

                if (data != null)
                {
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

                    var tabs = data["contents"]?["singleColumnBrowseResultsRenderer"]?["tabs"];
                    if (tabs != null && tabs.HasValues)
                    {
                        var sectionList = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"];
                        var secs = sectionList?["contents"];
                        var continuations = sectionList?["continuations"];

                        ParseHomeSectionList(secs, result.Sections);

                        if (continuations != null && continuations.HasValues)
                        {
                            result.ContinuationToken = continuations[0]?["nextContinuationData"]?["continuation"]?.ToString();
                        }

                        var chipCloud = sectionList?["header"]?["chipCloudRenderer"]?["chips"];
                        if (chipCloud != null && chipCloud.HasValues)
                        {
                            foreach (var c in chipCloud)
                            {
                                var r = c?["chipCloudChipRenderer"];
                                if (r == null) continue;
                                string title = r["text"]?["runs"]?[0]?["text"]?.ToString();
                                string p = r["navigationEndpoint"]?["browseEndpoint"]?["params"]?.ToString();
                                bool isSelected = r["isSelected"] != null && (bool)r["isSelected"];
                                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(p))
                                {
                                    result.Chips.Add(new HomeChipItem
                                    {
                                        Title = title,
                                        Params = p,
                                        IsSelected = isSelected
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        public static async Task<HomeBrowseResult> BrowseHomeContinuationAsync(string continuationToken, string accessToken = null)
        {
            var result = new HomeBrowseResult();
            if (string.IsNullOrEmpty(continuationToken)) return result;

            try
            {
                string vd = await GetVisitorDataAsync();
                JObject data = null;

                if (HasCookieAuth)
                {
                    var extraParams = new JObject
                    {
                        ["continuation"] = continuationToken
                    };
                    data = await CookieInnerTubePostAsync("browse", extraParams);
                }
                else
                {
                    var body = new JObject
                    {
                        ["context"] = BuildMusicContext(vd),
                        ["continuation"] = continuationToken
                    };
                    string url = "https://music.youtube.com/youtubei/v1/browse?prettyPrint=false";
                    data = await PostInnerTubeAsync(url, body, true);
                }

                if (data != null)
                {
                    var sectionList = data["continuationContents"]?["sectionListContinuation"];
                    var secs = sectionList?["contents"];
                    var continuations = sectionList?["continuations"];

                    ParseHomeSectionList(secs, result.Sections);

                    if (continuations != null && continuations.HasValues)
                    {
                        result.ContinuationToken = continuations[0]?["nextContinuationData"]?["continuation"]?.ToString();
                    }
                }
            }
            catch { }
            return result;
        }

        public static async Task<List<HomeSection>> BrowseHomeAsync(string accessToken = null, Action<List<HomeSection>> onPageLoaded = null)
        {
            var firstPage = await BrowseHomeFirstPageAsync(accessToken);
            onPageLoaded?.Invoke(new List<HomeSection>(firstPage.Sections));
            return firstPage.Sections;
        }
    }
}
