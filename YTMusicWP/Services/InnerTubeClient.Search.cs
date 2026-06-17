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

        /// <summary>
        /// Parse musicResponsiveListItemRenderer → YouTubeTrack
        /// Dùng cho search results và artist songs
        /// </summary>
        private static YouTubeTrack ParseMusicListItem(JToken item)
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
                if (runs != null && runs.HasValues)
                {
                    // Strategy: find first run with browseEndpoint (=artist link), else first non-label text
                    foreach (var r in runs)
                    {
                        string t = r["text"]?.ToString();
                        if (string.IsNullOrEmpty(t) || t == " • " || t == " · " || t == " & ") continue;
                        
                        // Check if this run has a browseEndpoint pointing to a channel
                        string browseTarget = r["navigationEndpoint"]?["browseEndpoint"]?["browseId"]?.ToString();
                        if (!string.IsNullOrEmpty(browseTarget) && browseTarget.StartsWith("UC"))
                        {
                            artist = t;
                            channelId = browseTarget;
                            break;
                        }
                    }
                    
                    // Fallback: first non-label, non-separator text
                    if (string.IsNullOrEmpty(artist))
                    {
                        foreach (var r in runs)
                        {
                            string t = r["text"]?.ToString();
                            if (string.IsNullOrEmpty(t) || t == " • " || t == " · " || t == " & ") continue;
                            if (t == "Song" || t == "Video" || t == "Artist" || t == "Playlist" || t == "Album" || t == "EP" || t == "Single") continue;
                            if (t.Contains(" views") || t.Contains(" view")) continue;
                            if (t.Length <= 6 && t.Contains(":")) continue; // duration like "3:57"
                            artist = t;
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

            // Thumbnail
            string thumbUrl = "";
            var thumbs = mr["thumbnail"]?["musicThumbnailRenderer"]
                ?["thumbnail"]?["thumbnails"];
            if (thumbs != null && thumbs.HasValues)
            {
                thumbUrl = thumbs.Last?["url"]?.ToString() ?? "";
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

            return new YouTubeTrack
            {
                VideoId = videoId,
                Title = title,
                ChannelName = CleanChannelName(artist),
                ChannelId = channelId,
                ThumbnailUrl = thumbUrl
            };
        }

        // ==========================================
        // BROWSE PLAYLIST
        // ==========================================
    }
}
