using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace YTMusicWP
{
    /// <summary>
    /// InnerTube API client — gọi trực tiếp YouTube Music/YouTube API từ phone.
    /// Không cần proxy, API key, hay backend.
    /// </summary>
    public static class InnerTubeClient
    {
        private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        private static string _cachedVisitorData = null;
        private static DateTime _vdCacheTime = DateTime.MinValue;

        // ==========================================
        // VISITOR DATA
        // ==========================================
        public static async Task<string> GetVisitorDataAsync()
        {
            // Cache 30 phút
            if (_cachedVisitorData != null && (DateTime.Now - _vdCacheTime).TotalMinutes < 30)
                return _cachedVisitorData;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
                var resp = await _client.SendAsync(request);
                if (resp.IsSuccessStatusCode)
                {
                    string html = await resp.Content.ReadAsStringAsync();
                    string marker = "\"visitorData\":\"";
                    int idx = html.IndexOf(marker);
                    if (idx >= 0)
                    {
                        int start = idx + marker.Length;
                        int end = html.IndexOf("\"", start);
                        if (end > start && end - start >= 20 && end - start < 600)
                        {
                            string vd = html.Substring(start, end - start);
                            if (vd.StartsWith("Cg"))
                            {
                                _cachedVisitorData = vd;
                                _vdCacheTime = DateTime.Now;
                                return vd;
                            }
                        }
                    }
                }
            }
            catch { }
            return _cachedVisitorData; // Return old cache if fetch fails
        }

        // ==========================================
        // CONTEXT BUILDERS
        // ==========================================
        private static JObject BuildMusicContext(string visitorData)
        {
            var client = new JObject
            {
                ["clientName"] = "WEB_REMIX",
                ["clientVersion"] = "1.20241016.01.00",
                ["hl"] = "en",
                ["gl"] = "US"
            };
            if (!string.IsNullOrEmpty(visitorData))
                client["visitorData"] = visitorData;

            return new JObject { ["client"] = client };
        }

        private static JObject BuildWebContext(string visitorData)
        {
            var client = new JObject
            {
                ["clientName"] = "WEB",
                ["clientVersion"] = "2.20241016.00.00",
                ["hl"] = "en",
                ["gl"] = "US"
            };
            if (!string.IsNullOrEmpty(visitorData))
                client["visitorData"] = visitorData;

            return new JObject { ["client"] = client };
        }

        private static async Task<JObject> PostInnerTubeAsync(string url, JObject body, bool isMusic = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");
            if (isMusic)
            {
                request.Headers.Add("Origin", "https://music.youtube.com");
                request.Headers.Add("Referer", "https://music.youtube.com/");
            }

            var resp = await _client.SendAsync(request);
            string json = await resp.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }

        // ==========================================
        // SEARCH
        // ==========================================
        /// <summary>
        /// Tìm kiếm bài hát qua InnerTube (YouTube Music).
        /// Trả về danh sách YouTubeTrack.
        /// </summary>
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
        // RESOLVE STREAM URL (for playback)
        // ==========================================
        /// <summary>
        /// Resolve audio stream URL qua InnerTube API.
        /// Thử nhiều client: ANDROID_VR → ANDROID_MUSIC → ANDROID.
        /// Gọi từ foreground vì HttpClient mạnh hơn AudioTask.
        /// </summary>
        public static string LastResolveDebug = "";

        public static async Task<string> ResolveStreamUrlAsync(string videoId)
        {
            LastResolveDebug = "";
            if (string.IsNullOrEmpty(videoId) || videoId.StartsWith("LOCAL:") || videoId.StartsWith("CHANNEL:") || videoId.StartsWith("PLAYLIST:"))
                return null;

            // Lấy visitorData từ watch page (chính xác nhất cho video cụ thể)
            string vd = null;
            try
            {
                var watchReq = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/watch?v=" + videoId);
                watchReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
                watchReq.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                var watchResp = await _client.SendAsync(watchReq);
                if (watchResp.IsSuccessStatusCode)
                {
                    string html = await watchResp.Content.ReadAsStringAsync();
                    LastResolveDebug = "wp:" + html.Length;
                    string marker = "\"visitorData\":\"";
                    int idx = html.IndexOf(marker);
                    if (idx >= 0)
                    {
                        int start = idx + marker.Length;
                        int end = html.IndexOf("\"", start);
                        if (end > start && end - start >= 20 && end - start < 600)
                        {
                            string candidate = html.Substring(start, end - start);
                            if (candidate.StartsWith("Cg"))
                            {
                                vd = candidate;
                                LastResolveDebug += " vd:OK";
                            }
                        }
                    }
                    if (vd == null) LastResolveDebug += " vd:NONE";
                }
                else
                {
                    LastResolveDebug = "wp:HTTP" + (int)watchResp.StatusCode;
                }
            }
            catch (Exception ex) { LastResolveDebug = "wp:EX:" + ex.Message.Substring(0, Math.Min(30, ex.Message.Length)); }

            // Fallback: dùng cached visitorData
            if (string.IsNullOrEmpty(vd))
            {
                vd = await GetVisitorDataAsync();
                if (!string.IsNullOrEmpty(vd))
                    LastResolveDebug += " vd2:OK";
            }

            // Thử nhiều client type
            var clients = new[]
            {
                new { Name = "ANDROID_VR", Version = "1.60.19", Id = "28", Make = "Oculus", Model = "Quest 3", Os = "12L",
                      Ua = "com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip" },
                new { Name = "ANDROID_MUSIC", Version = "7.27.52", Id = "21", Make = "Google", Model = "Pixel 7", Os = "14",
                      Ua = "com.google.android.apps.youtube.music/7.27.52 (Linux; U; Android 14; Pixel 7 Build/AP2A.240805.005) gzip" },
                new { Name = "ANDROID", Version = "19.29.37", Id = "3", Make = "Google", Model = "Pixel 7", Os = "14",
                      Ua = "com.google.android.youtube/19.29.37 (Linux; U; Android 14; Pixel 7 Build/AP2A.240805.005) gzip" },
            };

            foreach (var c in clients)
            {
                try
                {
                    string vdField = !string.IsNullOrEmpty(vd) ? ",\"visitorData\":\"" + vd + "\"" : "";
                    string requestBody = "{" +
                        "\"contentCheckOk\":true," +
                        "\"context\":{\"client\":{" +
                            "\"clientName\":\"" + c.Name + "\"," +
                            "\"clientVersion\":\"" + c.Version + "\"," +
                            "\"deviceMake\":\"" + c.Make + "\"," +
                            "\"deviceModel\":\"" + c.Model + "\"," +
                            "\"osName\":\"ANDROID\"," +
                            "\"osVersion\":\"" + c.Os + "\"," +
                            "\"platform\":\"MOBILE\"," +
                            "\"hl\":\"en\",\"gl\":\"US\"" +
                            vdField +
                        "}}," +
                        "\"videoId\":\"" + videoId + "\"" +
                    "}";

                    var req = new HttpRequestMessage(HttpMethod.Post,
                        "https://www.youtube.com/youtubei/v1/player?key=AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI&prettyPrint=false");
                    req.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                    req.Headers.Add("User-Agent", c.Ua);
                    req.Headers.Add("X-YouTube-Client-Name", c.Id);
                    req.Headers.Add("X-YouTube-Client-Version", c.Version);

                    var resp = await _client.SendAsync(req);
                    if (!resp.IsSuccessStatusCode)
                    {
                        LastResolveDebug += " " + c.Name.Substring(c.Name.Length > 8 ? c.Name.Length - 4 : 0) + ":H" + (int)resp.StatusCode;
                        continue;
                    }

                    string json = await resp.Content.ReadAsStringAsync();
                    var data = JObject.Parse(json);

                    string status = data["playabilityStatus"]?["status"]?.ToString() ?? "?";
                    string reason = data["playabilityStatus"]?["reason"]?.ToString() ?? "";
                    
                    if (status != "OK")
                    {
                        string shortReason = reason.Length > 15 ? reason.Substring(0, 15) : reason;
                        LastResolveDebug += " " + c.Name.Substring(c.Name.Length > 8 ? c.Name.Length - 4 : 0) + ":" + status.Substring(0, Math.Min(5, status.Length));
                        continue;
                    }

                    // Tìm audio URL: itag 140 (m4a 128kbps) → 139 → 18 (combo)
                    var formats = data["streamingData"]?["adaptiveFormats"];
                    if (formats != null)
                    {
                        foreach (var fmt in formats)
                        {
                            int itag = fmt["itag"]?.Value<int>() ?? 0;
                            if (itag == 140 || itag == 139)
                            {
                                string url = fmt["url"]?.ToString();
                                if (!string.IsNullOrEmpty(url))
                                {
                                    LastResolveDebug += " " + c.Name + ":OK";
                                    return PrepareStreamUrl(url);
                                }
                            }
                        }
                    }

                    // Fallback: formats (itag 18 = video+audio 360p)
                    var fmts2 = data["streamingData"]?["formats"];
                    if (fmts2 != null)
                    {
                        foreach (var fmt in fmts2)
                        {
                            int itag = fmt["itag"]?.Value<int>() ?? 0;
                            if (itag == 18)
                            {
                                string url = fmt["url"]?.ToString();
                                if (!string.IsNullOrEmpty(url))
                                {
                                    LastResolveDebug += " i18:OK";
                                    return PrepareStreamUrl(url);
                                }
                            }
                        }
                    }

                    // Status OK nhưng không có URL
                    LastResolveDebug += " " + c.Name.Substring(c.Name.Length > 8 ? c.Name.Length - 4 : 0) + ":NOURL";
                }
                catch (Exception ex)
                {
                    LastResolveDebug += " " + c.Name.Substring(c.Name.Length > 8 ? c.Name.Length - 4 : 0) + ":EX";
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Chuẩn bị URL stream: thêm ratebypass=yes và range=0- để tránh throttle/cut
        /// </summary>
        private static string PrepareStreamUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (!url.Contains("ratebypass="))
                url += "&ratebypass=yes";
            if (!url.Contains("range="))
                url += "&range=0-";
            return url;
        }

        // ==========================================
        // HELPERS
        // ==========================================
        private static string CleanChannelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.EndsWith(" - Topic")) return name.Substring(0, name.Length - 8);
            if (name.EndsWith(" - Chủ đề")) return name.Substring(0, name.Length - 9);
            return name;
        }
    }

    // ==========================================
    // RESULT MODELS
    // ==========================================
    public class PlaylistResult
    {
        public string Title { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
        public List<YouTubeTrack> Tracks { get; set; } = new List<YouTubeTrack>();
    }

    public class ArtistResult
    {
        public string Name { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public List<YouTubeTrack> Tracks { get; set; } = new List<YouTubeTrack>();
        public List<ArtistAlbum> Albums { get; set; } = new List<ArtistAlbum>();
    }

    public class ArtistAlbum
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
        public string BrowseId { get; set; } = "";
        public string SectionTitle { get; set; } = "";
    }
}
