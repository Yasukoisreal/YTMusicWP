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
        // Search result with continuation token for pagination, top card, and filter chips
        public class SearchResult
        {
            public List<YouTubeTrack> Tracks { get; set; }
            public string ContinuationToken { get; set; }
            public SearchCardItem Card { get; set; }
            public List<SearchChipItem> Chips { get; set; }
        }

        public static async Task<List<YouTubeTrack>> SearchAsync(string query, int maxResults = 20, string searchParams = null)
        {
            var result = await SearchWithContinuationAsync(query, maxResults, searchParams);
            if (result == null) return new List<YouTubeTrack>();
            var tracks = result.Tracks ?? new List<YouTubeTrack>();

            // Ensure top card (e.g. artist or album) is included at index 0 if it has a valid browseId/videoId
            if (result.Card != null && !string.IsNullOrEmpty(result.Card.Title))
            {
                string bId = result.Card.BrowseId;
                string vId = result.Card.VideoId;
                if (string.IsNullOrEmpty(vId) && !string.IsNullOrEmpty(bId))
                {
                    if (result.Card.ItemType == "artist" || bId.StartsWith("UC"))
                        vId = "CHANNEL:" + bId;
                    else if (result.Card.ItemType == "playlist" || result.Card.ItemType == "album")
                        vId = "PLAYLIST:" + bId.Replace("VL", "");
                }

                if (!string.IsNullOrEmpty(vId) && !tracks.Any(t => t.VideoId == vId))
                {
                    tracks.Insert(0, new YouTubeTrack
                    {
                        VideoId = vId,
                        Title = result.Card.Title,
                        ChannelName = result.Card.Title,
                        ChannelId = bId,
                        ThumbnailUrl = result.Card.ThumbnailUrl,
                        Subtitle = result.Card.Subtitle,
                        ItemType = result.Card.ItemType
                    });
                }
            }

            return tracks;
        }

        public static async Task<SearchResult> SearchWithContinuationAsync(string query, int maxResults = 20, string searchParams = null)
        {
            var results = new List<YouTubeTrack>();
            string continuationToken = null;
            SearchCardItem topCard = null;
            var chipsList = new List<SearchChipItem>();

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

                JObject data;
                if (HasCookieAuth)
                {
                    var searchExtra = new JObject { ["query"] = query };
                    if (!string.IsNullOrEmpty(searchParams))
                        searchExtra["params"] = searchParams;
                    data = await CookieInnerTubePostAsync("search", searchExtra);
                }
                else
                {
                    data = await PostInnerTubeAsync("https://music.youtube.com/youtubei/v1/search?prettyPrint=false", body, true);
                }

                var tabs = data?["contents"]?["tabbedSearchResultsRenderer"]?["tabs"];
                if (tabs == null || !tabs.HasValues) return new SearchResult { Tracks = results };

                var sectionList = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"];
                if (sectionList == null) return new SearchResult { Tracks = results };

                // Extract Search Filter Chips
                var chipNodes = sectionList["header"]?["chipCloudRenderer"]?["chips"];
                if (chipNodes != null && chipNodes.HasValues)
                {
                    foreach (var cn in chipNodes)
                    {
                        var ccr = cn["chipCloudChipRenderer"];
                        if (ccr == null) continue;
                        string chipText = "";
                        var textRuns = ccr["text"]?["runs"];
                        if (textRuns != null && textRuns.HasValues)
                            chipText = string.Join("", textRuns.Select(r => r["text"]?.ToString() ?? ""));
                        if (string.IsNullOrWhiteSpace(chipText)) continue;

                        string chipParams = ccr["navigationEndpoint"]?["searchEndpoint"]?["params"]?.ToString();
                        bool isSel = ccr["isSelected"] != null && (bool)ccr["isSelected"];

                        chipsList.Add(new SearchChipItem
                        {
                            Title = NormalizeSearchChipTitle(chipText),
                            FilterParams = chipParams,
                            IsSelected = isSel
                        });
                    }
                }

                var sections = sectionList["contents"];
                if (sections == null) return new SearchResult { Tracks = results, Chips = chipsList };

                foreach (var sec in sections)
                {
                    var card = sec["musicCardShelfRenderer"];
                    if (card != null && topCard == null)
                    {
                        try
                        {
                            string cardTitle = card["title"]?["runs"]?[0]?["text"]?.ToString();
                            string cardVid = card["title"]?["runs"]?[0]?["navigationEndpoint"]?["watchEndpoint"]?["videoId"]?.ToString();
                            string cardBrowseId = card["title"]?["runs"]?[0]?["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                            if (string.IsNullOrEmpty(cardBrowseId))
                                cardBrowseId = card["onTap"]?["browseEndpoint"]?["browseId"]?.ToString();
                            if (string.IsNullOrEmpty(cardVid))
                                cardVid = card["onTap"]?["watchEndpoint"]?["videoId"]?.ToString();

                            var cardThumbs = card["thumbnail"]?["musicThumbnailRenderer"]?["thumbnail"]?["thumbnails"];
                            string cardThumb = (cardThumbs != null && cardThumbs.HasValues) ? cardThumbs.Last?["url"]?.ToString() : "";
                            string cardCrop = card["thumbnail"]?["musicThumbnailRenderer"]?["thumbnailCrop"]?.ToString();

                            string fullSub = "";
                            var subRuns = card["subtitle"]?["runs"];
                            if (subRuns != null && subRuns.HasValues)
                            {
                                fullSub = string.Join("", subRuns.Select(r => r["text"]?.ToString() ?? ""));
                            }

                            string itemType = "song";
                            if (cardCrop == "MUSIC_THUMBNAIL_CROP_CIRCLE" || (!string.IsNullOrEmpty(cardBrowseId) && cardBrowseId.StartsWith("UC")))
                                itemType = "artist";
                            else if (!string.IsNullOrEmpty(cardBrowseId) && (cardBrowseId.StartsWith("VL") || cardBrowseId.StartsWith("PL")))
                                itemType = "playlist";
                            else if (!string.IsNullOrEmpty(cardBrowseId) && cardBrowseId.StartsWith("MPREb_"))
                                itemType = "album";

                            string shufflePlaylistId = null;
                            string radioPlaylistId = null;
                            var cardButtons = card["buttons"];
                            if (cardButtons != null && cardButtons.HasValues)
                            {
                                foreach (var btn in cardButtons)
                                {
                                    var br = btn["buttonRenderer"];
                                    if (br == null) continue;
                                    string iconType = br["icon"]?["iconType"]?.ToString();
                                    string cmdPlaylistId = br["command"]?["watchPlaylistEndpoint"]?["playlistId"]?.ToString();
                                    string cmdVideoId = br["command"]?["watchEndpoint"]?["videoId"]?.ToString();

                                    if (iconType == "MUSIC_SHUFFLE")
                                        shufflePlaylistId = cmdPlaylistId;
                                    else if (iconType == "MIX")
                                        radioPlaylistId = cmdPlaylistId;
                                    else if (iconType == "PLAY_ARROW" && string.IsNullOrEmpty(cardVid))
                                        cardVid = cmdVideoId;
                                }
                            }

                            var topSongs = new List<YouTubeTrack>();
                            var cardContents = card["contents"];
                            if (cardContents != null && cardContents.HasValues)
                            {
                                foreach (var cItem in cardContents)
                                {
                                    try
                                    {
                                        var t = ParseMusicListItem(cItem);
                                        if (t != null && !string.IsNullOrEmpty(t.VideoId))
                                            topSongs.Add(t);
                                    }
                                    catch { }
                                }
                            }

                            topCard = new SearchCardItem
                            {
                                Title = cardTitle,
                                Subtitle = fullSub,
                                ThumbnailUrl = cardThumb,
                                ItemType = itemType,
                                BrowseId = cardBrowseId,
                                VideoId = cardVid,
                                ShufflePlaylistId = shufflePlaylistId,
                                RadioPlaylistId = radioPlaylistId,
                                TopSongs = topSongs
                            };
                        }
                        catch { }
                        continue;
                    }

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
                }
            }
            catch { }
            return new SearchResult { Tracks = results, ContinuationToken = continuationToken, Card = topCard, Chips = chipsList };
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

        /// <summary>
        /// Check if a candidate channel/artist title matches the target artist name closely.
        /// Prevents unrelated TV shows, labels, or collaboration titles from hijacking artist profiles.
        /// </summary>
        public static bool IsArtistNameMatch(string candidate, string target)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(target))
                return false;

            candidate = candidate.Trim();
            target = target.Trim();

            // Exact case-insensitive match (e.g. "Dương Domic" == "dương domic", "Captain Boy" == "CAPTAIN BOY")
            if (candidate.Equals(target, StringComparison.OrdinalIgnoreCase))
                return true;

            // Candidate contains Topic suffix or official channel marker (e.g. "Dương Domic - Topic", "Lou Hoàng Official")
            if (candidate.StartsWith(target + " -", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(target + " Official", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(target + " Channel", StringComparison.OrdinalIgnoreCase))
                return true;

            // Target contains artist qualifier
            if (target.StartsWith(candidate + " -", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(candidate + " Official", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// High-precision artist channel resolver.
        /// Uses the YouTube Music Artists filter (EgWKAQIgAWoQEAUQAxAEEAkQChAQEBUQEQ%3D%3D) and Top Card.
        /// Strictly verifies candidate name matching to prevent false positives.
        /// </summary>
        public static async Task<YouTubeTrack> FindArtistAsync(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName)) return null;
            artistName = artistName.Trim();

            try
            {
                // 1. Primary: Search YouTube Music with the "Artists" filter
                var artistFilterResult = await SearchWithContinuationAsync(artistName, 5, "EgWKAQIgAWoQEAUQAxAEEAkQChAQEBUQEQ%3D%3D");
                if (artistFilterResult != null)
                {
                    // Check Top Card first
                    if (artistFilterResult.Card != null && !string.IsNullOrEmpty(artistFilterResult.Card.BrowseId))
                    {
                        if (IsArtistNameMatch(artistFilterResult.Card.Title, artistName))
                        {
                            return new YouTubeTrack
                            {
                                VideoId = "CHANNEL:" + artistFilterResult.Card.BrowseId,
                                ChannelId = artistFilterResult.Card.BrowseId,
                                Title = artistFilterResult.Card.Title,
                                ChannelName = artistFilterResult.Card.Title,
                                ThumbnailUrl = artistFilterResult.Card.ThumbnailUrl,
                                ItemType = "artist"
                            };
                        }
                    }

                    // Check Tracks from the Artists filter
                    if (artistFilterResult.Tracks != null && artistFilterResult.Tracks.Count > 0)
                    {
                        var match = artistFilterResult.Tracks.FirstOrDefault(t =>
                            t.VideoId != null && t.VideoId.StartsWith("CHANNEL:") &&
                            IsArtistNameMatch(t.Title, artistName));

                        if (match != null)
                        {
                            string chId = match.ChannelId ?? match.VideoId.Replace("CHANNEL:", "");
                            return new YouTubeTrack
                            {
                                VideoId = "CHANNEL:" + chId,
                                ChannelId = chId,
                                Title = match.Title,
                                ChannelName = match.Title,
                                ThumbnailUrl = match.ThumbnailUrl,
                                ItemType = "artist"
                            };
                        }
                    }
                }

                // 2. Secondary: Normal search with Top Card inspection
                var normalResult = await SearchWithContinuationAsync(artistName, 5, null);
                if (normalResult != null)
                {
                    if (normalResult.Card != null && !string.IsNullOrEmpty(normalResult.Card.BrowseId))
                    {
                        if ((normalResult.Card.ItemType == "artist" || normalResult.Card.BrowseId.StartsWith("UC")) &&
                            IsArtistNameMatch(normalResult.Card.Title, artistName))
                        {
                            return new YouTubeTrack
                            {
                                VideoId = "CHANNEL:" + normalResult.Card.BrowseId,
                                ChannelId = normalResult.Card.BrowseId,
                                Title = normalResult.Card.Title,
                                ChannelName = normalResult.Card.Title,
                                ThumbnailUrl = normalResult.Card.ThumbnailUrl,
                                ItemType = "artist"
                            };
                        }
                    }

                    if (normalResult.Tracks != null && normalResult.Tracks.Count > 0)
                    {
                        var match = normalResult.Tracks.FirstOrDefault(t =>
                            t.VideoId != null && t.VideoId.StartsWith("CHANNEL:") &&
                            IsArtistNameMatch(t.Title, artistName));

                        if (match != null)
                        {
                            string chId = match.ChannelId ?? match.VideoId.Replace("CHANNEL:", "");
                            return new YouTubeTrack
                            {
                                VideoId = "CHANNEL:" + chId,
                                ChannelId = chId,
                                Title = match.Title,
                                ChannelName = match.Title,
                                ThumbnailUrl = match.ThumbnailUrl,
                                ItemType = "artist"
                            };
                        }
                    }
                }
            }
            catch { }

            return null;
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
                if (!string.IsNullOrEmpty(browseTarget) && (browseTarget.StartsWith("UC") || browseTarget.StartsWith("MPSP") || browseTarget.StartsWith("FEmusic_podcast")))
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
                if (string.IsNullOrEmpty(t) || t == " • " || t == " · " || t == " & " || t == ", ") continue;
                
                string lower = t.ToLowerInvariant().Trim();
                if (lower == "song" || lower == "video" || lower == "artist" || lower == "playlist" || lower == "album" || lower == "ep" || lower == "single" ||
                    lower == "episode" || lower == "podcast" || lower == "show" || lower == "profile" || lower == "station" ||
                    lower == "bài hát" || lower == "nghệ sĩ" || lower == "danh sách phát" || lower == "tập" || lower == "tập podcast" || lower == "chương trình" || lower == "hồ sơ")
                    continue;

                if (lower.Contains(" views") || lower.Contains(" view") || lower.Contains(" lượt phát") || lower.Contains(" lượt xem") ||
                    lower.Contains(" subscriber") || lower.Contains(" người đăng ký") || lower.Contains(" plays") || lower.Contains(" play") ||
                    lower.Contains(" song") || lower.Contains(" bài hát") || lower.Contains(" track") || lower.Contains(" người nghe") || lower.Contains(" monthly"))
                    continue;

                if (t.Length <= 6 && t.Contains(":")) continue; // duration like "3:57"
                int parsedYear;
                if (t.Length == 4 && int.TryParse(t, out parsedYear)) continue; // ignore year like "2026"
                if (IsDateOrRelativeTime(lower)) continue; // ignore dates like "Aug 19", "Oct 17, 2024", "7h ago"
                
                return t;
            }
            
            return "";
        }

        private static bool IsDateOrRelativeTime(string lower)
        {
            if (string.IsNullOrEmpty(lower)) return false;
            if (lower.Contains(" ago") || lower.Contains(" trước")) return true;
            if (lower.Contains("thg ")) return true;

            // Dates require at least one digit (day, time, or year)
            bool hasDigit = false;
            for (int i = 0; i < lower.Length; i++)
            {
                if (char.IsDigit(lower[i])) { hasDigit = true; break; }
            }
            if (!hasDigit) return false;

            string[] months = new string[] { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
            foreach (var m in months)
            {
                if (lower.StartsWith(m + " ") || lower.Contains(" " + m + " ") || lower.EndsWith(" " + m))
                    return true;
            }
            if (lower.Contains(", 20") || lower.Contains(", 19")) return true;
            return false;
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
                        string rText = r["text"]?.ToString();
                        string browseTarget = r["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                        if (!string.IsNullOrEmpty(browseTarget) && (browseTarget.StartsWith("UC") || browseTarget.StartsWith("MPSP") || browseTarget.StartsWith("FEmusic_podcast")))
                        {
                            channelId = browseTarget;
                            if (rText == artist) break; // Exact single artist match
                            // Otherwise keep first artist channelId found
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
            if (string.IsNullOrEmpty(videoId) && cols != null && cols.Count() > 0)
            {
                videoId = cols[0]?["musicResponsiveListItemFlexColumnRenderer"]
                    ?["text"]?["runs"]?[0]?["navigationEndpoint"]?["watchEndpoint"]?["videoId"]?.ToString();
            }
            if (string.IsNullOrEmpty(videoId))
            {
                videoId = mr["menu"]?.SelectToken("$..likeButtonRenderer.target.videoId")?.ToString();
            }
            if (string.IsNullOrEmpty(videoId))
            {
                videoId = mr.SelectToken("$..watchEndpoint.videoId")?.ToString();
            }

            // BrowseId (for artist/playlist items)
            string browseId = mr["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();

            // SetVideoId (for playlist items)
            string setVideoId = mr["playlistItemData"]?["playlistSetVideoId"]?.ToString() ?? mr["playlistItemData"]?["setVideoId"]?.ToString();

            // Extract full subtitle string across columns from col 1 onwards
            List<string> subParts = new List<string>();
            if (cols != null)
            {
                for (int cIdx = 1; cIdx < cols.Count(); cIdx++)
                {
                    var colRuns = cols[cIdx]?["musicResponsiveListItemFlexColumnRenderer"]?["text"]?["runs"];
                    if (colRuns != null && colRuns.HasValues)
                    {
                        string colText = string.Join("", colRuns.Select(r => r["text"]?.ToString() ?? "")).Trim(' ', '•', '·');
                        if (!string.IsNullOrWhiteSpace(colText))
                            subParts.Add(colText);
                    }
                }
            }
            string fullSubtitle = string.Join(" • ", subParts);

            // Thumbnail
            string thumbUrl = "";
            double coverWidth = 140; // Default 1:1
            string thumbCrop = mr["thumbnail"]?["musicThumbnailRenderer"]?["thumbnailCrop"]?.ToString();
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

            // Badges (e.g. LIVE)
            bool isLive = false;
            var badges = mr["badges"];
            if (badges != null && badges.HasValues)
            {
                foreach (var b in badges)
                {
                    // Live badge renderer (standard on YouTube Music live streams)
                    var liveR = b["liveBadgeRenderer"];
                    if (liveR != null)
                    {
                        isLive = true;
                        break;
                    }

                    var badgeR = b["musicInlineBadgeRenderer"];
                    if (badgeR != null)
                    {
                        string iconType = badgeR["icon"]?["iconType"]?.ToString();
                        string label = badgeR["accessibilityData"]?["accessibilityData"]?["label"]?.ToString();
                        if (iconType == "LIVE" || (label != null && (label.IndexOf("LIVE", StringComparison.OrdinalIgnoreCase) >= 0 || label.IndexOf("Trực tiếp", StringComparison.OrdinalIgnoreCase) >= 0)))
                        {
                            isLive = true;
                            break;
                        }
                    }
                }
            }
            if (!isLive && fullSubtitle != null && (fullSubtitle.StartsWith("LIVE", StringComparison.OrdinalIgnoreCase) || fullSubtitle.StartsWith("Trực tiếp", StringComparison.OrdinalIgnoreCase)))
            {
                isLive = true;
            }

            // Determine type
            string type = "song";
            if (thumbCrop == "MUSIC_THUMBNAIL_CROP_CIRCLE" || (!string.IsNullOrEmpty(browseId) && browseId.StartsWith("UC")))
            {
                type = "artist";
            }
            else if (!string.IsNullOrEmpty(browseId) && (browseId.StartsWith("VL") || browseId.StartsWith("PL")))
            {
                type = "playlist";
            }
            else if (!string.IsNullOrEmpty(browseId) && browseId.StartsWith("MPREb_"))
            {
                type = "album";
            }
            else if (fullSubtitle.StartsWith("Video", StringComparison.OrdinalIgnoreCase))
            {
                type = "video";
            }

            // Use browseId as videoId marker if needed
            if (type == "artist" && !string.IsNullOrEmpty(browseId) && string.IsNullOrEmpty(videoId))
            {
                videoId = "CHANNEL:" + browseId;
            }
            else if ((type == "playlist" || type == "album") && !string.IsNullOrEmpty(browseId) && string.IsNullOrEmpty(videoId))
            {
                videoId = "PLAYLIST:" + browseId.Replace("VL", "");
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
                    var nav = mi["menuNavigationItemRenderer"];
                    if (nav != null)
                    {
                        string bId = nav["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                        if (!string.IsNullOrEmpty(bId))
                        {
                            if (bId.StartsWith("MPTC"))
                            {
                                creditsBrowseId = bId;
                            }
                            else if (string.IsNullOrEmpty(channelId) && (bId.StartsWith("UC") || bId.StartsWith("FEmusic_artist")))
                            {
                                string iconType = nav["icon"]?["iconType"]?.ToString();
                                if (iconType == "ARTIST" || bId.StartsWith("UC"))
                                {
                                    channelId = bId;
                                }
                            }
                        }
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
                CoverWidth = coverWidth,
                Subtitle = fullSubtitle,
                ItemType = type,
                IsLive = isLive
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

        public static string NormalizeSearchChipTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            string t = text.Trim();
            if (CurrentLanguage == "vi")
            {
                string lower = t.ToLowerInvariant();
                if (lower == "nghệ sĩ" || lower == "nghe si" || lower == "artists" || lower == "artist") return "Nghệ sĩ";
                if (lower == "bài hát" || lower == "bai hat" || lower == "songs" || lower == "song") return "Bài hát";
                if (lower == "đĩa nhạc" || lower == "dia nhac" || lower == "album" || lower == "albums") return "Album";
                if (lower == "video" || lower == "videos") return "Video";
                if (lower.Contains("danh sách phát") || lower.Contains("community playlists") || lower.Contains("playlists") || lower.Contains("playlist")) return "Danh sách phát cộng đồng";
                if (lower.Contains("tập podcast") || lower.Contains("tập") || lower.Contains("episodes") || lower.Contains("episode")) return "Tập";
                if (lower == "hồ sơ" || lower == "ho so" || lower == "profiles" || lower == "profile") return "Hồ sơ";
                if (lower.Contains("podcast")) return "Podcast";
                return t;
            }
            if (CurrentLanguage == "en" || string.IsNullOrEmpty(CurrentLanguage))
            {
                string lower = t.ToLowerInvariant();
                if (lower == "nghệ sĩ" || lower == "nghe si" || lower == "artists" || lower == "artist") return "Artists";
                if (lower == "bài hát" || lower == "bai hat" || lower == "songs" || lower == "song") return "Songs";
                if (lower == "đĩa nhạc" || lower == "dia nhac" || lower == "album" || lower == "albums") return "Albums";
                if (lower == "video" || lower == "videos") return "Videos";
                if (lower.Contains("danh sách phát") || lower.Contains("community playlists") || lower.Contains("playlists") || lower.Contains("playlist")) return "Community playlists";
                if (lower.Contains("tập podcast") || lower.Contains("tập") || lower.Contains("episodes") || lower.Contains("episode")) return "Episodes";
                if (lower == "hồ sơ" || lower == "ho so" || lower == "profiles" || lower == "profile") return "Profiles";
                if (lower.Contains("podcast")) return "Podcasts";
                return t;
            }
            return t;
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

    public class SearchCardItem
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ThumbnailUrl { get; set; }
        public string ItemType { get; set; }
        public string BrowseId { get; set; }
        public string VideoId { get; set; }
        public string RadioPlaylistId { get; set; }
        public string ShufflePlaylistId { get; set; }
        public List<YouTubeTrack> TopSongs { get; set; }

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

        public Visibility ArtistThumbVisibility
        {
            get { return (ItemType == "artist" || (!string.IsNullOrEmpty(BrowseId) && BrowseId.StartsWith("UC"))) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility SquareThumbVisibility
        {
            get { return (ItemType != "artist" && (string.IsNullOrEmpty(BrowseId) || !BrowseId.StartsWith("UC"))) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ShuffleButtonVisibility
        {
            get { return (!string.IsNullOrEmpty(ShufflePlaylistId) || ItemType == "artist") ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility MixButtonVisibility
        {
            get { return (!string.IsNullOrEmpty(RadioPlaylistId) || !string.IsNullOrEmpty(VideoId) || ItemType == "artist") ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility TopSongsVisibility
        {
            get { return (TopSongs != null && TopSongs.Count > 0) ? Visibility.Visible : Visibility.Collapsed; }
        }
    }

    public class SearchChipItem
    {
        public string Title { get; set; }
        public string FilterKey { get; set; }
        public string FilterParams { get; set; }
        public bool IsSelected { get; set; }
    }
}

