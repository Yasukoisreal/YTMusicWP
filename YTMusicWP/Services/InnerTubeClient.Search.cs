using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace YTMusicWP
{
    public static partial class InnerTubeClient
    {
        // Search result with continuation token for pagination
        public class SearchResult
        {
            public List<YouTubeTrack> Tracks { get; set; }
            public string ContinuationToken { get; set; }
        }

        public static async Task<List<YouTubeTrack>> SearchAsync(string query, int maxResults = 20, string searchParams = null)
        {
            var result = await SearchWithContinuationAsync(query, maxResults, searchParams);
            return result?.Tracks ?? new List<YouTubeTrack>();
        }

        public static async Task<SearchResult> SearchWithContinuationAsync(string query, int maxResults = 20, string searchParams = null)
        {
            var results = new List<YouTubeTrack>();
            string continuationToken = null;
            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["query"] = query
                };

                // Add params for filtered search (songs, videos, playlists, artists)
                if (!string.IsNullOrEmpty(searchParams))
                    body["params"] = searchParams;

                var data = await PostInnerTubeAsync(
                    "https://music.youtube.com/youtubei/v1/search?prettyPrint=false", body, true);

                var tabs = data?["contents"]?["tabbedSearchResultsRenderer"]?["tabs"];
                if (tabs == null || !tabs.HasValues) return new SearchResult { Tracks = results };

                var sections = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];
                if (sections == null) return new SearchResult { Tracks = results };

                foreach (var sec in sections)
                {
                    var shelf = sec["musicShelfRenderer"];
                    if (shelf != null)
                    {
                        var items = shelf["contents"];
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                try
                                {
                                    var track = ParseMusicListItem(item);
                                    if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                    {
                                        results.Add(track);
                                        if (results.Count >= maxResults) break;
                                    }
                                }
                                catch { continue; }
                            }
                        }
                        // Extract continuation token
                        var conts = shelf["continuations"];
                        if (conts != null && conts.HasValues)
                        {
                            continuationToken = conts[0]?["nextContinuationData"]?["continuation"]?.ToString();
                            if (string.IsNullOrEmpty(continuationToken))
                                continuationToken = conts[0]?["reloadContinuationData"]?["continuation"]?.ToString();
                            System.Diagnostics.Debug.WriteLine("[InnerTube] Continuation token found: " + (continuationToken != null ? continuationToken.Substring(0, Math.Min(40, continuationToken.Length)) + "..." : "null"));
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[InnerTube] No continuations array in shelf");
                        }
                        continue;
                    }

                    var isr = sec["itemSectionRenderer"];
                    if (isr != null)
                    {
                        var isrItems = isr["contents"];
                        if (isrItems != null)
                        {
                            foreach (var item in isrItems)
                            {
                                try
                                {
                                    var track = ParseMusicListItem(item);
                                    if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                    {
                                        results.Add(track);
                                        if (results.Count >= maxResults) break;
                                    }
                                }
                                catch { continue; }
                            }
                        }
                        continue;
                    }

                    var card = sec["musicCardShelfRenderer"];
                    if (card != null)
                    {
                        try
                        {
                            string cardTitle = card["title"]?["runs"]?[0]?["text"]?.ToString();
                            string cardVid = card["title"]?["runs"]?[0]?["navigationEndpoint"]?["watchEndpoint"]?["videoId"]?.ToString();
                            string cardBrowseId = card["title"]?["runs"]?[0]?["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                            var cardThumbs = card["thumbnail"]?["musicThumbnailRenderer"]?["thumbnail"]?["thumbnails"];
                            string cardThumb = cardThumbs != null && cardThumbs.HasValues ? cardThumbs.Last?["url"]?.ToString() : "";

                            string cardSub = "";
                            var subRuns = card["subtitle"]?["runs"];
                            if (subRuns != null)
                            {
                                foreach (var r in subRuns)
                                {
                                    string t = r["text"]?.ToString();
                                    if (t != null && t != " • " && t != " · " && t != "Song" && t != "Video" && t != "Artist" && t != "Playlist" && t != "Album" && t != "EP" && t != "Single")
                                    {
                                        if (t.Contains(" views") || t.Contains(" view")) continue;
                                        if (t.Length <= 6 && t.Contains(":")) continue;
                                        cardSub = t;
                                        break;
                                    }
                                }
                            }

                            string vid = cardVid;
                            if (string.IsNullOrEmpty(vid) && !string.IsNullOrEmpty(cardBrowseId))
                            {
                                if (cardBrowseId.StartsWith("UC")) vid = "CHANNEL:" + cardBrowseId;
                                else if (cardBrowseId.StartsWith("VL") || cardBrowseId.StartsWith("PL"))
                                    vid = "PLAYLIST:" + cardBrowseId.Replace("VL", "");
                            }

                            if (!string.IsNullOrEmpty(vid) && !string.IsNullOrEmpty(cardTitle))
                            {
                                results.Add(new YouTubeTrack
                                {
                                    VideoId = vid,
                                    Title = cardTitle,
                                    ChannelName = CleanChannelName(cardSub),
                                    ThumbnailUrl = cardThumb ?? ""
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return new SearchResult { Tracks = results, ContinuationToken = continuationToken };
        }

        /// <summary>
        /// Load more search results using continuation token
        /// </summary>
        public static async Task<SearchResult> SearchContinueAsync(string continuationToken)
        {
            var results = new List<YouTubeTrack>();
            string nextToken = null;
            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd)
                };

                var data = await PostInnerTubeAsync(
                    "https://music.youtube.com/youtubei/v1/search?ctoken=" + Uri.EscapeDataString(continuationToken) + "&continuation=" + Uri.EscapeDataString(continuationToken) + "&prettyPrint=false", body, true);

                System.Diagnostics.Debug.WriteLine("[InnerTube Continue] Response keys: " + (data != null ? string.Join(",", ((JObject)data).Properties().Select(p => p.Name)) : "null"));

                // Continuation response: continuationContents.musicShelfContinuation
                var shelf = data?["continuationContents"]?["musicShelfContinuation"];
                if (shelf == null)
                {
                    // Fallback: try sectionListContinuation
                    shelf = data?["continuationContents"]?["sectionListContinuation"];
                    if (shelf != null)
                    {
                        // sectionListContinuation has contents[] with musicShelfRenderer
                        var innerSections = shelf["contents"];
                        if (innerSections != null)
                        {
                            foreach (var sec in innerSections)
                            {
                                var innerShelf = sec["musicShelfRenderer"];
                                if (innerShelf != null)
                                {
                                    var innerItems = innerShelf["contents"];
                                    if (innerItems != null)
                                    {
                                        foreach (var item in innerItems)
                                        {
                                            try
                                            {
                                                var track = ParseMusicListItem(item);
                                                if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                                    results.Add(track);
                                            }
                                            catch { continue; }
                                        }
                                    }
                                }
                            }
                        }
                        System.Diagnostics.Debug.WriteLine("[InnerTube Continue] sectionListContinuation: " + results.Count + " tracks");
                        return new SearchResult { Tracks = results, ContinuationToken = null };
                    }
                    System.Diagnostics.Debug.WriteLine("[InnerTube Continue] No shelf found in response");
                    return new SearchResult { Tracks = results, ContinuationToken = null };
                }

                var items2 = shelf["contents"];
                if (items2 != null)
                {
                    foreach (var item in items2)
                    {
                        try
                        {
                            var track = ParseMusicListItem(item);
                            if (track != null && !string.IsNullOrEmpty(track.VideoId))
                                results.Add(track);
                        }
                        catch { continue; }
                    }
                }
                var conts = shelf["continuations"];
                if (conts != null && conts.HasValues)
                {
                    nextToken = conts[0]?["nextContinuationData"]?["continuation"]?.ToString();
                    if (string.IsNullOrEmpty(nextToken))
                        nextToken = conts[0]?["reloadContinuationData"]?["continuation"]?.ToString();
                }
                System.Diagnostics.Debug.WriteLine("[InnerTube Continue] Got " + results.Count + " tracks, next token: " + (nextToken != null ? "yes" : "no"));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[InnerTube Continue] Error: " + ex.Message); }
            return new SearchResult { Tracks = results, ContinuationToken = nextToken };
        }

        internal static string ExtractArtistFromRuns(JToken runs)
        {
            if (runs == null || !runs.HasValues) return "";

            List<string> artists = new List<string>();
            foreach (var r in runs)
            {
                string t = r["text"]?.ToString();
                if (string.IsNullOrEmpty(t)) continue;
                
                string browseTarget = r["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                if (!string.IsNullOrEmpty(browseTarget) && browseTarget.StartsWith("UC"))
                {
                    artists.Add(t);
                }
            }
            
            if (artists.Count > 0)
            {
                return string.Join(", ", artists);
            }

            // Fallback: first non-label, non-separator text
            foreach (var r in runs)
            {
                string t = r["text"]?.ToString();
                if (string.IsNullOrEmpty(t) || t == " • " || t == " · " || t == " & ") continue;
                
                string lower = t.ToLowerInvariant();
                if (lower == "song" || lower == "video" || lower == "artist" || lower == "playlist" || lower == "album" || lower == "ep" || lower == "single") continue;
                if (lower.Contains(" views") || lower.Contains(" view") || lower.Contains(" lượt phát") || lower.Contains(" lượt xem") || lower.Contains(" views") || lower.Contains(" subscriber") || lower.Contains(" người đăng ký") || lower.Contains(" plays") || lower.Contains(" play") || lower.Contains(" song") || lower.Contains(" bài hát") || lower.Contains(" track")) continue;
                if (t.Length <= 6 && t.Contains(":")) continue; // duration like "3:57"
                int parsedYear;
                if (t.Length == 4 && int.TryParse(t, out parsedYear)) continue; // ignore year like "2026"
                
                return t;
            }
            
            return "";
        }

        /// <summary>
        /// Parse musicResponsiveListItemRenderer → YouTubeTrack
        /// Dùng cho search results và artist songs
        /// </summary>
        public static YouTubeTrack ParseMusicListItem(JToken item)
        {
            var mr = item["musicResponsiveListItemRenderer"];
            if (mr == null) return null;

            var cols = mr["flexColumns"];
            if (cols == null || !cols.HasValues) return null;

            // Title
            string title = cols[0]?["musicResponsiveListItemFlexColumnRenderer"]
                ?["text"]?["runs"]?[0]?["text"]?.ToString();
            if (string.IsNullOrEmpty(title)) return null;

            // Artist — column 1 contains runs like: ["Song", " • ", "ArtistName", " • ", "AlbumName", ...]
            string artist = "";
            string channelId = null;
            if (cols.Count() > 1)
            {
                var runs = cols[1]?["musicResponsiveListItemFlexColumnRenderer"]?["text"]?["runs"];
                artist = ExtractArtistFromRuns(runs);
                // Try to get channelId if we extracted the artist from a link
                if (runs != null && runs.HasValues && !string.IsNullOrEmpty(artist))
                {
                    foreach (var r in runs)
                    {
                        if (r["text"]?.ToString() == artist)
                        {
                            string browseTarget = r["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                            if (!string.IsNullOrEmpty(browseTarget) && browseTarget.StartsWith("UC"))
                            {
                                channelId = browseTarget;
                            }
                            break;
                        }
                    }
                }
            }

            // VideoId — try multiple paths
            string videoId = mr["playlistItemData"]?["videoId"]?.ToString();
            if (string.IsNullOrEmpty(videoId))
            {
                videoId = mr["overlay"]?["musicItemThumbnailOverlayRenderer"]
                    ?["content"]?["musicPlayButtonRenderer"]
                    ?["playNavigationEndpoint"]?["watchEndpoint"]?["videoId"]?.ToString();
            }
            if (string.IsNullOrEmpty(videoId))
            {
                videoId = mr["navigationEndpoint"]?["watchEndpoint"]?["videoId"]?.ToString();
            }

            // BrowseId (for artist/playlist items)
            string browseId = mr["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();

            // SetVideoId (for playlist items)
            string setVideoId = mr["playlistItemData"]?["playlistSetVideoId"]?.ToString() ?? mr["playlistItemData"]?["setVideoId"]?.ToString();

            // Thumbnail
            string thumbUrl = "";
            double coverWidth = 140; // Default 1:1
            var thumbs = mr["thumbnail"]?["musicThumbnailRenderer"]
                ?["thumbnail"]?["thumbnails"];
            if (thumbs != null && thumbs.HasValues)
            {
                var lastThumb = thumbs.Last;
                thumbUrl = lastThumb?["url"]?.ToString() ?? "";
                
                int w = 0, h = 0;
                int.TryParse(lastThumb?["width"]?.ToString(), out w);
                int.TryParse(lastThumb?["height"]?.ToString(), out h);
                if (w > 0 && h > 0)
                {
                    double ratio = (double)w / h;
                    if (ratio > 1.3) coverWidth = 260;
                }
            }

            // Determine type
            string type = "song";
            if (!string.IsNullOrEmpty(browseId) && string.IsNullOrEmpty(videoId))
            {
                if (browseId.StartsWith("UC")) type = "artist";
                else if (browseId.StartsWith("VL") || browseId.StartsWith("PL")) type = "playlist";

                // Use browseId as videoId marker
                if (type == "artist") videoId = "CHANNEL:" + browseId;
                else if (type == "playlist") videoId = "PLAYLIST:" + browseId.Replace("VL", "");
            }

            // Extract AlbumName if present
            string albumName = null;
            if (cols.Count() > 2)
            {
                var albumRuns = cols[2]?["musicResponsiveListItemFlexColumnRenderer"]?["text"]?["runs"];
                if (albumRuns != null && albumRuns.HasValues)
                {
                    albumName = albumRuns[0]?["text"]?.ToString();
                }
            }
            if (string.IsNullOrEmpty(albumName) && cols.Count() > 1)
            {
                var runs = cols[1]?["musicResponsiveListItemFlexColumnRenderer"]?["text"]?["runs"];
                if (runs != null && runs.HasValues)
                {
                    foreach (var r in runs)
                    {
                        string bId = r["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                        if (!string.IsNullOrEmpty(bId) && bId.StartsWith("MPREb_"))
                        {
                            albumName = r["text"]?.ToString();
                            break;
                        }
                    }
                }
            }

            // Extract Song Credits BrowseId (starts with MPTC)
            string creditsBrowseId = null;
            var menuItems = mr["menu"]?["menuRenderer"]?["items"];
            if (menuItems != null && menuItems.HasValues)
            {
                foreach (var mi in menuItems)
                {
                    string bId = mi["menuNavigationItemRenderer"]?["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                    if (!string.IsNullOrEmpty(bId) && bId.StartsWith("MPTC"))
                    {
                        creditsBrowseId = bId;
                        break;
                    }
                }
            }

            return new YouTubeTrack
            {
                VideoId = videoId,
                Title = title,
                ChannelName = CleanChannelName(artist),
                ChannelId = channelId,
                AlbumName = albumName,
                CreditsBrowseId = creditsBrowseId,
                ThumbnailUrl = thumbUrl,
                SetVideoId = setVideoId,
                CoverWidth = coverWidth
            };
        }

        // ==========================================
        // SEARCH SUGGESTIONS (YouTube Music)
        // ==========================================
        public static async Task<List<SearchSuggestionItem>> GetSearchSuggestionsAsync(string query)
        {
            var list = new List<SearchSuggestionItem>();
            if (string.IsNullOrWhiteSpace(query)) return list;

            try
            {
                JObject data = null;
                if (HasCookieAuth)
                {
                    var extra = new JObject { ["input"] = query };
                    data = await CookieInnerTubePostAsync("music/get_search_suggestions", extra, "WEB_REMIX", "1.20260304.03.00");
                }
                else
                {
                    string vd = await GetVisitorDataAsync();
                    var body = new JObject
                    {
                        ["context"] = BuildMusicContext(vd),
                        ["input"] = query
                    };
                    data = await PostInnerTubeAsync(
                        "https://music.youtube.com/youtubei/v1/music/get_search_suggestions?prettyPrint=false", body, true);
                }

                if (data == null) return list;

                var contents = data["contents"];
                if (contents == null || !contents.HasValues) return list;

                int queryCount = 0;
                int entityCount = 0;

                foreach (var section in contents)
                {
                    var items = section["searchSuggestionsSectionRenderer"]?["contents"];
                    if (items == null || !items.HasValues) continue;

                    foreach (var item in items)
                    {
                        // 1. Query suggestion
                        var queryRenderer = item["searchSuggestionRenderer"] ?? item["historySuggestionRenderer"];
                        if (queryRenderer != null)
                        {
                            if (queryCount >= 7) continue;
                            var runs = queryRenderer["suggestion"]?["runs"];
                            if (runs != null && runs.HasValues)
                            {
                                string qText = "";
                                foreach (var r in runs)
                                {
                                    var t = r["text"]?.ToString();
                                    if (!string.IsNullOrEmpty(t)) qText += t;
                                }
                                if (!string.IsNullOrWhiteSpace(qText))
                                {
                                    list.Add(new SearchSuggestionItem
                                    {
                                        Type = SearchSuggestionType.Query,
                                        Query = qText.Trim(),
                                        Title = qText.Trim()
                                    });
                                    queryCount++;
                                }
                            }
                            continue;
                        }

                        // 2. Rich Entity suggestion (Artist, Song, Playlist, Album)
                        var entityRenderer = item["musicResponsiveListItemRenderer"];
                        if (entityRenderer != null)
                        {
                            if (entityCount >= 6) continue;

                            var flexCols = entityRenderer["flexColumns"];
                            if (flexCols == null || !flexCols.HasValues) continue;

                            string title = "";
                            var titleRuns = flexCols[0]?["musicResponsiveListItemFlexColumnRenderer"]?["text"]?["runs"];
                            if (titleRuns != null && titleRuns.HasValues)
                            {
                                foreach (var r in titleRuns)
                                {
                                    var t = r["text"]?.ToString();
                                    if (!string.IsNullOrEmpty(t)) title += t;
                                }
                            }
                            if (string.IsNullOrWhiteSpace(title)) continue;

                            string subtitle = "";
                            if (flexCols.Count() > 1)
                            {
                                var subRuns = flexCols[1]?["musicResponsiveListItemFlexColumnRenderer"]?["text"]?["runs"];
                                if (subRuns != null && subRuns.HasValues)
                                {
                                    foreach (var r in subRuns)
                                    {
                                        var t = r["text"]?.ToString();
                                        if (!string.IsNullOrEmpty(t)) subtitle += t;
                                    }
                                }
                            }

                            string thumbUrl = null;
                            var thumbs = entityRenderer["thumbnail"]?["musicThumbnailRenderer"]?["thumbnail"]?["thumbnails"];
                            if (thumbs != null && thumbs.HasValues)
                            {
                                thumbUrl = thumbs.Last?["url"]?.ToString();
                            }

                            var nav = entityRenderer["navigationEndpoint"];
                            string videoId = nav?["watchEndpoint"]?["videoId"]?.ToString();
                            string browseId = nav?["browseEndpoint"]?["browseId"]?.ToString();
                            string pageType = nav?["browseEndpoint"]?["browseEndpointContextSupportedConfigs"]?["browseEndpointContextMusicConfig"]?["pageType"]?.ToString();

                            var itemType = SearchSuggestionType.Song;
                            if (!string.IsNullOrEmpty(pageType))
                            {
                                if (pageType == "MUSIC_PAGE_TYPE_ARTIST") itemType = SearchSuggestionType.Artist;
                                else if (pageType == "MUSIC_PAGE_TYPE_PLAYLIST") itemType = SearchSuggestionType.Playlist;
                                else if (pageType == "MUSIC_PAGE_TYPE_ALBUM") itemType = SearchSuggestionType.Album;
                            }
                            else if (!string.IsNullOrEmpty(browseId))
                            {
                                if (browseId.StartsWith("UC")) itemType = SearchSuggestionType.Artist;
                                else if (browseId.StartsWith("VL") || browseId.StartsWith("PL")) itemType = SearchSuggestionType.Playlist;
                                else if (browseId.StartsWith("MPREb")) itemType = SearchSuggestionType.Album;
                            }

                            list.Add(new SearchSuggestionItem
                            {
                                Type = itemType,
                                Title = title.Trim(),
                                Subtitle = subtitle.Trim(),
                                ThumbnailUrl = thumbUrl,
                                VideoId = videoId,
                                BrowseId = browseId,
                                PageType = pageType,
                                Query = title.Trim()
                            });
                            entityCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[InnerTube] GetSearchSuggestionsAsync error: " + ex.Message);
            }

            return list;
        }
    }

    public enum SearchSuggestionType
    {
        Query,
        Artist,
        Playlist,
        Album,
        Song
    }

    public class SearchSuggestionItem
    {
        public SearchSuggestionType Type { get; set; }
        public string Query { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ThumbnailUrl { get; set; }
        public string BrowseId { get; set; }
        public string VideoId { get; set; }
        public string PageType { get; set; }

        public Uri ThumbnailBitmapUri
        {
            get
            {
                if (string.IsNullOrEmpty(ThumbnailUrl)) return null;
                Uri uri;
                if (Uri.TryCreate(ThumbnailUrl, UriKind.Absolute, out uri))
                    return uri;
                return null;
            }
        }

        public Visibility QueryVisibility
        {
            get { return Type == SearchSuggestionType.Query ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility EntityVisibility
        {
            get { return Type != SearchSuggestionType.Query ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ArtistThumbVisibility
        {
            get { return Type == SearchSuggestionType.Artist ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility SquareThumbVisibility
        {
            get { return (Type != SearchSuggestionType.Query && Type != SearchSuggestionType.Artist) ? Visibility.Visible : Visibility.Collapsed; }
        }
    }
}

