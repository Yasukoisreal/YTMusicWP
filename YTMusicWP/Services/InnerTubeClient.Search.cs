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
        public static async Task<List<YouTubeTrack>> SearchAsync(string query, int maxResults = 20)
        {
            var results = new List<YouTubeTrack>();
            try
            {
                string vd = await GetVisitorDataAsync();
                var body = new JObject
                {
                    ["context"] = BuildMusicContext(vd),
                    ["query"] = query
                    // Không dùng params filter → trả cả songs, playlists, artists
                };

                var data = await PostInnerTubeAsync(
                    "https://music.youtube.com/youtubei/v1/search?prettyPrint=false", body, true);

                // Path: contents.tabbedSearchResultsRenderer.tabs[0].tabRenderer.content.sectionListRenderer.contents[]
                var tabs = data?["contents"]?["tabbedSearchResultsRenderer"]?["tabs"];
                if (tabs == null || !tabs.HasValues) return results;

                var sections = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];
                if (sections == null) return results;

                foreach (var sec in sections)
                {
                    // musicShelfRenderer — danh sách bài hát
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
                                        if (results.Count >= maxResults) return results;
                                    }
                                }
                                catch { continue; }
                            }
                        }
                        continue;
                    }

                    // itemSectionRenderer — playlist/artist items
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
                                        if (results.Count >= maxResults) return results;
                                    }
                                }
                                catch { continue; }
                            }
                        }
                        continue;
                    }

                    // musicCardShelfRenderer — top result card
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

                            // Subtitle for artist name — take first non-label/non-separator text
                            string cardSub = "";
                            var subRuns = card["subtitle"]?["runs"];
                            if (subRuns != null)
                            {
                                foreach (var r in subRuns)
                                {
                                    string t = r["text"]?.ToString();
                                    if (t != null && t != " • " && t != " · " && t != "Song" && t != "Video" && t != "Artist" && t != "Playlist" && t != "Album" && t != "EP" && t != "Single")
                                    {
                                        // Skip view counts and durations
                                        if (t.Contains(" views") || t.Contains(" view")) continue;
                                        if (t.Length <= 6 && t.Contains(":")) continue; // e.g. "3:57"
                                        cardSub = t;
                                        break; // Take FIRST valid text (= artist name)
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
            return results;
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
