using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.StartScreen;

namespace YTMusicWP.Services
{
    public static class TileService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        public static bool IsLiveTileEnabled
        {
            get
            {
                try
                {
                    var settings = ApplicationData.Current.LocalSettings.Values;
                    return !settings.ContainsKey("EnableLiveTile") || (bool)settings["EnableLiveTile"];
                }
                catch { return true; }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values["EnableLiveTile"] = value;
                    if (!value) ClearLiveTile();
                }
                catch { }
            }
        }

        // 0: Full Dynamic (Now Playing + Recommendations Flip)
        // 1: Now Playing Only (Transparent when idle)
        // 2: Transparent Only (Static Transparent Tile)
        public static int LiveTileMode
        {
            get
            {
                try
                {
                    var settings = ApplicationData.Current.LocalSettings.Values;
                    return settings.ContainsKey("LiveTileMode") ? Convert.ToInt32(settings["LiveTileMode"]) : 0;
                }
                catch { return 0; }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values["LiveTileMode"] = value;
                    if (value == 1 || value == 2) ClearLiveTile();
                }
                catch { }
            }
        }

        // 0: Calm / Slow (2 items - Recommended for relaxed & elegant flipping)
        // 1: Moderate (3 items)
        // 2: Dynamic (5 items - Rapid rotation)
        public static int LiveTileSpeed
        {
            get
            {
                try
                {
                    var settings = ApplicationData.Current.LocalSettings.Values;
                    return settings.ContainsKey("LiveTileSpeed") ? Convert.ToInt32(settings["LiveTileSpeed"]) : 0;
                }
                catch { return 0; }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values["LiveTileSpeed"] = value;
                }
                catch { }
            }
        }

        private static DateTime _lastRecommendationUpdate = DateTime.MinValue;
        private static readonly TimeSpan MinUpdateInterval = TimeSpan.FromSeconds(2);

        public static void ClearLiveTile()
        {
            try
            {
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Clear();
                ClearBadge();
            }
            catch { }
        }

        // ── Badge & Lock Screen Integration ──

        public static void SetPlayingBadge()
        {
            try
            {
                var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
                var badgeEl = badgeXml.SelectSingleNode("/badge") as XmlElement;
                badgeEl.SetAttribute("value", "playing");
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(new BadgeNotification(badgeXml));
            }
            catch { }
        }

        public static void ClearBadge()
        {
            try
            {
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
            }
            catch { }
        }

        // ── Now Playing & Up Next Queue Tiles ──

        public static void UpdateNowPlaying(string title, string artist, string thumbUrl)
        {
            UpdateNowPlayingWithQueue(title, artist, thumbUrl, null);
        }

        public static void UpdateNowPlayingWithQueue(
            string title,
            string artist,
            string thumbUrl,
            IEnumerable<YouTubeTrack> upNextTracks)
        {
            try
            {
                if (!IsLiveTileEnabled || LiveTileMode == 2) return;
                if (string.IsNullOrEmpty(thumbUrl)) return;

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                bool isDynamic = (LiveTileMode == 0);
                updater.EnableNotificationQueue(isDynamic);
                
                string squareThumb = FormatSquareThumbnail(thumbUrl);
                string safeThumb = WebUtility.HtmlEncode(squareThumb);
                string safeTitle = WebUtility.HtmlEncode(title ?? "");
                string safeArtist = WebUtility.HtmlEncode(artist ?? "");

                // Medium tile: peek with "♪ Now Playing" prefix
                // Wide tile: Photo-style vertical slide/peek with song & artist
                // Large tile: Photo-style vertical slide/peek with song & artist
                string xml = string.Format(
                    "<tile><visual version=\"2\">" +
                    "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                    "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text></binding>" +
                    "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text><text id=\"2\">{2}</text></binding>" +
                    "<binding template=\"TileSquare310x310PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text><text id=\"2\">{2}</text></binding>" +
                    "</visual></tile>", safeThumb, safeTitle, safeArtist);

                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var notif = new TileNotification(doc)
                {
                    Tag = "nowplaying",
                    ExpirationTime = DateTimeOffset.UtcNow.AddHours(12)
                };
                updater.Update(notif);

                // If Up Next has tracks, push an Up Next tile to rotate with Now Playing (Only in Dynamic Mode)
                if (isDynamic && upNextTracks != null)
                {
                    var nextList = upNextTracks.Where(IsValidTrack).Take(2).ToList();
                    if (nextList.Count > 0)
                    {
                        var next1 = nextList[0];
                        string nextThumb = WebUtility.HtmlEncode(FormatSquareThumbnail(next1.ThumbnailUrl));
                        string next1Title = WebUtility.HtmlEncode(next1.Title ?? "");
                        string next1Artist = WebUtility.HtmlEncode(next1.ChannelName ?? "YouTube Music");

                        string xmlUpNext = string.Format(
                            "<tile><visual version=\"2\">" +
                            "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                            "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">Up Next: {1}</text></binding>" +
                            "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">UP NEXT: {1}</text><text id=\"2\">{2}</text></binding>" +
                            "<binding template=\"TileSquare310x310PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">UP NEXT: {1}</text><text id=\"2\">{2}</text></binding>" +
                            "</visual></tile>", nextThumb, next1Title, next1Artist);

                        var docNext = new XmlDocument();
                        docNext.LoadXml(xmlUpNext);
                        var notifNext = new TileNotification(docNext)
                        {
                            Tag = "upnext",
                            ExpirationTime = DateTimeOffset.UtcNow.AddHours(12)
                        };
                        updater.Update(notifNext);
                    }
                }

                SetPlayingBadge();
            }
            catch { }
        }

        // ── Recommendations & Favorites Live Tile ──

        public static void UpdateRecommendations(
            IEnumerable<YouTubeTrack> homeTracks,
            IEnumerable<YouTubeTrack> favoriteTracks = null,
            IEnumerable<YouTubeTrack> historyTracks = null,
            int maxCount = 5,
            bool force = false)
        {
            Task.Run(async () =>
            {
                await UpdateRecommendationsAsync(homeTracks, favoriteTracks, historyTracks, maxCount, force);
            });
        }

        public static void UpdateRecommendations(IEnumerable<YouTubeTrack> homeTracks, int maxCount)
        {
            UpdateRecommendations(homeTracks, null, null, maxCount, false);
        }

        public static async Task UpdateRecommendationsAsync(
            IEnumerable<YouTubeTrack> homeTracks,
            IEnumerable<YouTubeTrack> favoriteTracks = null,
            IEnumerable<YouTubeTrack> historyTracks = null,
            int maxCount = 5,
            bool force = false)
        {
            try
            {
                if (!IsLiveTileEnabled || LiveTileMode != 0) return;

                // Throttling: Prevent updating too fast unless forced by user setting change
                if (!force && (DateTime.UtcNow - _lastRecommendationUpdate) < MinUpdateInterval)
                {
                    return;
                }
                _lastRecommendationUpdate = DateTime.UtcNow;

                // 1. Build a diverse list of valid unique tracks
                var pool = new List<TileItem>();
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (favoriteTracks != null)
                {
                    foreach (var t in favoriteTracks)
                    {
                        if (IsValidTrack(t) && seenIds.Add(t.VideoId))
                            pool.Add(new TileItem { Track = t, Label = "Your Favorite" });
                    }
                }

                if (homeTracks != null)
                {
                    foreach (var t in homeTracks)
                    {
                        if (IsValidTrack(t) && seenIds.Add(t.VideoId))
                            pool.Add(new TileItem { Track = t, Label = "Trending" });
                    }
                }

                if (historyTracks != null)
                {
                    foreach (var t in historyTracks)
                    {
                        if (IsValidTrack(t) && seenIds.Add(t.VideoId))
                            pool.Add(new TileItem { Track = t, Label = "Recently Played" });
                    }
                }

                if (pool.Count == 0) return;

                // Extract formatted thumbnail URLs for mosaic creation & wide collections
                var thumbUrls = pool.Select(p => FormatSquareThumbnail(p.Track.ThumbnailUrl)).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();

                // 2. Generate People Hub Style Mosaic Tiles (3x3 = 9 boxes, 2x2 = 4 boxes) & Cache Local Images
                string mosaic3x3_Path = null;
                string mosaic2x2_Path = null;
                string feat0_Path = null;
                string feat1_Path = null;

                try
                {
                    var tasks = new List<Task>();
                    Task<string> task9 = null;
                    Task<string> task4 = null;
                    Task<string> taskFeat0 = null;
                    Task<string> taskFeat1 = null;

                    if (thumbUrls.Count >= 4)
                    {
                        task9 = GenerateMosaicTileAsync(thumbUrls.Take(9).ToList(), 3, "tile_mosaic_3x3.png");
                        task4 = GenerateMosaicTileAsync(thumbUrls.Take(4).ToList(), 2, "tile_mosaic_2x2.png");
                        tasks.Add(task9);
                        tasks.Add(task4);
                    }

                    if (pool.Count > 0)
                    {
                        taskFeat0 = CacheLocalTileImageAsync(FormatSquareThumbnail(pool[0].Track.ThumbnailUrl), "tile_feat_0.png");
                        tasks.Add(taskFeat0);
                    }

                    if (pool.Count > 1)
                    {
                        taskFeat1 = CacheLocalTileImageAsync(FormatSquareThumbnail(pool[1].Track.ThumbnailUrl), "tile_feat_1.png");
                        tasks.Add(taskFeat1);
                    }

                    if (tasks.Count > 0)
                    {
                        await Task.WhenAll(tasks);
                        if (task9 != null) mosaic3x3_Path = task9.Result;
                        if (task4 != null) mosaic2x2_Path = task4.Result;
                        if (taskFeat0 != null) feat0_Path = taskFeat0.Result;
                        if (taskFeat1 != null) feat1_Path = taskFeat1.Result;
                    }
                }
                catch { }

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(true);
                updater.Clear();

                int speed = LiveTileSpeed;
                // speed 0 (Calm / Slower): 2 items in queue -> Windows Phone cycles slowly & stays static longer
                // speed 1 (Moderate): 3 items in queue
                // speed 2 (Dynamic): 5 items in queue
                int maxQueueItems = (speed == 0) ? 2 : (speed == 1 ? 3 : 5);

                // 3. Build Notification Queue items
                // --- Item 0: People 9-Box Mosaic (Medium & Large) + 5-Cover Collection (Wide) ---
                if (!string.IsNullOrEmpty(mosaic3x3_Path))
                {
                    PushPeopleMosaicTile(updater, mosaic3x3_Path, thumbUrls.Take(5).ToList(), "mosaic_9_tile", 0);
                }
                else if (pool.Count > 0)
                {
                    PushPhotosSlideTile(updater, pool[0], feat0_Path, "rec_0", 0);
                }

                // --- Item 1: Photos-Style Vertical Slide (Medium, Wide, Large) ---
                if (maxQueueItems >= 2 && pool.Count > 0)
                {
                    PushPhotosSlideTile(updater, pool[0], feat0_Path, "slide_0", 1);
                }

                // --- Item 2: Typography Billboard Tile (#1 Charts / Billboard Style) ---
                if (maxQueueItems >= 3 && pool.Count > 0)
                {
                    var topTrending = pool.FirstOrDefault(p => p.Label == "Trending") ?? pool[0];
                    PushBlockNumberTile(updater, topTrending, "#1", "Top Charts", 2);
                }

                // --- Item 3: People 4-Box Mosaic (Medium & Large) ---
                if (maxQueueItems >= 4 && !string.IsNullOrEmpty(mosaic2x2_Path))
                {
                    PushPeopleMosaicTile(updater, mosaic2x2_Path, thumbUrls.Skip(4).Take(5).ToList(), "mosaic_4_tile", 3);
                }

                // --- Item 4: Featured Track 2 with Photos-Style Vertical Slide ---
                if (maxQueueItems >= 5 && pool.Count > 1)
                {
                    PushPhotosSlideTile(updater, pool[1], feat1_Path, "slide_1", 4);
                }

                // 4. Schedule Daypart Notifications (Morning / Afternoon / Evening)
                ScheduleDaypartNotifications(pool.Select(p => p.Track));
            }
            catch { }
        }

        // ── Secondary Tiles (Pin to Start Screen) ──

        public static bool IsSecondaryTilePinned(string rawTileId)
        {
            try
            {
                return SecondaryTile.Exists(SanitizeTileId(rawTileId));
            }
            catch { return false; }
        }

        public static async Task<bool> PinSecondaryTileAsync(
            string rawTileId,
            string displayName,
            string arguments,
            IEnumerable<YouTubeTrack> tracks = null)
        {
            try
            {
                string tileId = SanitizeTileId(rawTileId);
                if (SecondaryTile.Exists(tileId)) return true;

                var squareLogo = new Uri("ms-appx:///Assets/Logo.png");
                var wideLogo = new Uri("ms-appx:///Assets/WideLogo.png");
                var smallLogo = new Uri("ms-appx:///Assets/Square71x71Logo.png");

                var tile = new SecondaryTile(
                    tileId,
                    displayName,
                    arguments,
                    squareLogo,
                    TileSize.Square150x150);

                tile.VisualElements.Wide310x150Logo = wideLogo;
                tile.VisualElements.Square71x71Logo = smallLogo;
                tile.VisualElements.ShowNameOnSquare150x150Logo = true;
                tile.VisualElements.ShowNameOnWide310x150Logo = true;
                tile.VisualElements.ForegroundText = ForegroundText.Light;

                bool created = await tile.RequestCreateAsync();
                if (created && tracks != null)
                {
                    var trackList = tracks.Where(IsValidTrack).ToList();
                    if (trackList.Count > 0)
                    {
                        var _ = Task.Run(async () =>
                        {
                            await UpdateSecondaryTileLiveContentAsync(tileId, displayName, trackList);
                        });
                    }
                }
                return created;
            }
            catch { return false; }
        }

        public static async Task<bool> UnpinSecondaryTileAsync(string rawTileId)
        {
            try
            {
                string tileId = SanitizeTileId(rawTileId);
                if (SecondaryTile.Exists(tileId))
                {
                    var tile = new SecondaryTile(tileId);
                    return await tile.RequestDeleteAsync();
                }
                return true;
            }
            catch { return false; }
        }

        public static async Task UpdateSecondaryTileLiveContentAsync(
            string tileId,
            string displayName,
            List<YouTubeTrack> tracks)
        {
            try
            {
                if (!SecondaryTile.Exists(tileId) || tracks == null || tracks.Count == 0) return;

                var updater = TileUpdateManager.CreateTileUpdaterForSecondaryTile(tileId);
                updater.EnableNotificationQueue(true);
                updater.Clear();

                var thumbUrls = tracks.Select(t => FormatSquareThumbnail(t.ThumbnailUrl)).Where(u => !string.IsNullOrEmpty(u)).Distinct().Take(9).ToList();
                string mosaicPath = null;
                if (thumbUrls.Count >= 4)
                {
                    string fileName = "tile_sec_" + tileId.Replace(".", "_") + ".png";
                    mosaicPath = await GenerateMosaicTileAsync(thumbUrls, thumbUrls.Count >= 9 ? 3 : 2, fileName);
                }

                if (!string.IsNullOrEmpty(mosaicPath))
                {
                    PushPeopleMosaicTile(updater, mosaicPath, thumbUrls.Take(5).ToList(), "sec_mosaic", 0);
                }
                else
                {
                    PushPhotosSlideTile(updater, new TileItem { Track = tracks[0], Label = displayName }, null, "sec_0", 0);
                }

                if (tracks.Count > 1)
                {
                    PushPhotosSlideTile(updater, new TileItem { Track = tracks[1], Label = displayName }, null, "sec_1", 1);
                }
            }
            catch { }
        }

        private static string SanitizeTileId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "tile_default";
            var sb = new System.Text.StringBuilder();
            foreach (char c in id)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        // ── Scheduled Daypart Notifications (Morning, Afternoon, Evening) ──

        public static void ScheduleDaypartNotifications(IEnumerable<YouTubeTrack> tracks)
        {
            try
            {
                if (!IsLiveTileEnabled || LiveTileMode != 0) return;
                var validTracks = tracks != null ? tracks.Where(IsValidTrack).ToList() : new List<YouTubeTrack>();
                if (validTracks.Count == 0) return;

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();

                // Clear outdated scheduled notifications
                try
                {
                    var scheduled = updater.GetScheduledTileNotifications();
                    foreach (var s in scheduled) updater.RemoveFromSchedule(s);
                }
                catch { }

                DateTime now = DateTime.Now;
                var r = new Random();

                // Morning (06:00): Good Morning Mix
                DateTime morningTime = now.Date.AddDays(now.Hour >= 6 ? 1 : 0).AddHours(6);
                var morningTrack = validTracks[r.Next(validTracks.Count)];
                ScheduleSingleDaypart(updater, morningTrack, "☀️ Good Morning Mix", "daypart_morning", morningTime);

                // Afternoon (13:00): Energy Boost
                DateTime afternoonTime = now.Date.AddDays(now.Hour >= 13 ? 1 : 0).AddHours(13);
                var afternoonTrack = validTracks[r.Next(validTracks.Count)];
                ScheduleSingleDaypart(updater, afternoonTrack, "⚡ Energy Boost", "daypart_afternoon", afternoonTime);

                // Evening (20:00): Chill & Relax
                DateTime eveningTime = now.Date.AddDays(now.Hour >= 20 ? 1 : 0).AddHours(20);
                var eveningTrack = validTracks[r.Next(validTracks.Count)];
                ScheduleSingleDaypart(updater, eveningTrack, "🌙 Chill & Relax", "daypart_evening", eveningTime);
            }
            catch { }
        }

        private static void ScheduleSingleDaypart(
            TileUpdater updater,
            YouTubeTrack track,
            string greeting,
            string tag,
            DateTime deliveryTime)
        {
            try
            {
                string thumb = WebUtility.HtmlEncode(FormatSquareThumbnail(track.ThumbnailUrl));
                string title = WebUtility.HtmlEncode(track.Title ?? "");
                string artist = WebUtility.HtmlEncode(track.ChannelName ?? "YouTube Music");
                string safeGreeting = WebUtility.HtmlEncode(greeting);

                string xml = string.Format(
                    "<tile><visual version=\"2\">" +
                    "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                    "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}: {2}</text></binding>" +
                    "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2} · {3}</text></binding>" +
                    "<binding template=\"TileSquare310x310PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2} · {3}</text></binding>" +
                    "</visual></tile>", thumb, safeGreeting, title, artist);

                var doc = new XmlDocument();
                doc.LoadXml(xml);

                var notif = new ScheduledTileNotification(doc, new DateTimeOffset(deliveryTime))
                {
                    Tag = tag,
                    ExpirationTime = new DateTimeOffset(deliveryTime.AddHours(5))
                };
                updater.AddToSchedule(notif);
            }
            catch { }
        }

        // ── Tile Push Helpers ──

        private static void PushBlockNumberTile(TileUpdater updater, TileItem item, string blockText, string blockLabel, int index)
        {
            string safeTitle = WebUtility.HtmlEncode(item.Track.Title ?? "");
            string safeArtist = WebUtility.HtmlEncode(item.Track.ChannelName ?? "YouTube Music");
            string safeBlockText = WebUtility.HtmlEncode(blockText ?? "#1");
            string safeBlockLabel = WebUtility.HtmlEncode(blockLabel ?? "Top Charts");

            // Wide: TileWide310x150BlockAndText01 (Big #1 block on left, title & artist on right)
            // Medium: TileSquare150x150Block (Big #1 block with label)
            // Large: TileSquare310x310BlockAndText01
            string xml = string.Format(
                "<tile><visual version=\"2\">" +
                "<binding template=\"TileSquare150x150Block\"><text id=\"1\">{0}</text><text id=\"2\">{1}</text></binding>" +
                "<binding template=\"TileWide310x150BlockAndText01\"><text id=\"1\">{0}</text><text id=\"2\">{1}</text><text id=\"3\">{2}</text><text id=\"4\">{3}</text></binding>" +
                "<binding template=\"TileSquare310x310BlockAndText01\"><text id=\"1\">{0}</text><text id=\"2\">{1}</text><text id=\"3\">{2}</text><text id=\"4\">{3}</text></binding>" +
                "</visual></tile>", safeBlockText, safeBlockLabel, safeTitle, safeArtist);

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var notif = new TileNotification(doc)
            {
                Tag = "block_" + index,
                ExpirationTime = DateTimeOffset.UtcNow.AddDays(1)
            };
            updater.Update(notif);
        }

        private static void PushPhotosSlideTile(
            TileUpdater updater,
            TileItem item,
            string localImageUri,
            string tag,
            int index)
        {
            string squareThumb = string.IsNullOrEmpty(localImageUri) ? FormatSquareThumbnail(item.Track.ThumbnailUrl) : localImageUri;
            string safeThumb = WebUtility.HtmlEncode(squareThumb ?? "");
            string safeTitle = WebUtility.HtmlEncode(item.Track.Title ?? "");
            string safeArtist = WebUtility.HtmlEncode(item.Track.ChannelName ?? "YouTube Music");
            string safeLabel = WebUtility.HtmlEncode(item.Label ?? "YouTube Music");

            // Small: Single Cover
            // Medium: TileSquare150x150PeekImageAndText04 (Photos-style peek text over cover)
            // Wide: TileWide310x150PeekImage01 (Photos-style vertical slide)
            // Large: TileSquare310x310PeekImage01 (Photos-style vertical slide)
            string xml = string.Format(
                "<tile><visual version=\"2\">" +
                "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text></binding>" +
                "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2} · {3}</text></binding>" +
                "<binding template=\"TileSquare310x310PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2}</text></binding>" +
                "</visual></tile>", safeThumb, safeTitle, safeArtist, safeLabel);

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var notif = new TileNotification(doc)
            {
                Tag = tag ?? ("slide_" + index),
                ExpirationTime = DateTimeOffset.UtcNow.AddDays(1)
            };
            updater.Update(notif);
        }

        private static void PushPeopleMosaicTile(
            TileUpdater updater,
            string mosaicUri,
            List<string> fiveThumbs,
            string tag,
            int index)
        {
            string safeMosaic = WebUtility.HtmlEncode(mosaicUri ?? "");
            var thumbs = new List<string>(fiveThumbs ?? new List<string>());
            while (thumbs.Count < 5) thumbs.Add(thumbs.FirstOrDefault() ?? "");

            string t1 = WebUtility.HtmlEncode(thumbs[0] ?? "");
            string t2 = WebUtility.HtmlEncode(thumbs[1] ?? "");
            string t3 = WebUtility.HtmlEncode(thumbs[2] ?? "");
            string t4 = WebUtility.HtmlEncode(thumbs[3] ?? "");
            string t5 = WebUtility.HtmlEncode(thumbs[4] ?? "");

            // Small: First track cover
            // Medium: 9-Box / 4-Box People Mosaic
            // Wide: TileWide310x150ImageCollection (5 rotating album arts)
            // Large: TileSquare310x310Image (Full-resolution 9-Box / 4-Box People Mosaic)
            string xml = string.Format(
                "<tile><visual version=\"2\">" +
                "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                "<binding template=\"TileSquare150x150Image\"><image id=\"1\" src=\"{1}\"/></binding>" +
                "<binding template=\"TileWide310x150ImageCollection\">" +
                "<image id=\"1\" src=\"{0}\"/><image id=\"2\" src=\"{2}\"/><image id=\"3\" src=\"{3}\"/><image id=\"4\" src=\"{4}\"/><image id=\"5\" src=\"{5}\"/>" +
                "</binding>" +
                "<binding template=\"TileSquare310x310Image\"><image id=\"1\" src=\"{1}\"/></binding>" +
                "</visual></tile>", t1, safeMosaic, t2, t3, t4, t5);

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var notif = new TileNotification(doc)
            {
                Tag = tag ?? ("mosaic_" + index),
                ExpirationTime = DateTimeOffset.UtcNow.AddDays(1)
            };
            updater.Update(notif);
        }

        // ── Local Image Caching & People Hub Mosaic Generator ──

        public static async Task<string> CacheLocalTileImageAsync(string url, string fileName)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                byte[] raw = await _httpClient.GetByteArrayAsync(url);
                if (raw == null || raw.Length == 0) return null;

                byte[] squarePngBytes = null;
                try
                {
                    using (var inStream = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(inStream.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(raw);
                            await writer.StoreAsync();
                        }

                        var decoder = await BitmapDecoder.CreateAsync(inStream);
                        uint srcW = decoder.PixelWidth;
                        uint srcH = decoder.PixelHeight;
                        uint cropSize = Math.Min(srcW, srcH);
                        uint cropX = (srcW - cropSize) / 2;
                        uint cropY = (srcH - cropSize) / 2;
                        uint targetSize = Math.Min(cropSize, 480);
                        if (targetSize < 120) targetSize = cropSize;

                        var transform = new BitmapTransform
                        {
                            Bounds = new BitmapBounds
                            {
                                X = cropX,
                                Y = cropY,
                                Width = cropSize,
                                Height = cropSize
                            },
                            ScaledWidth = targetSize,
                            ScaledHeight = targetSize,
                            InterpolationMode = BitmapInterpolationMode.Fant
                        };

                        var pixelData = await decoder.GetPixelDataAsync(
                            BitmapPixelFormat.Bgra8,
                            BitmapAlphaMode.Premultiplied,
                            transform,
                            ExifOrientationMode.RespectExifOrientation,
                            ColorManagementMode.ColorManageToSRgb);

                        byte[] pixels = pixelData.DetachPixelData();

                        using (var outStream = new InMemoryRandomAccessStream())
                        {
                            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
                            encoder.SetPixelData(
                                BitmapPixelFormat.Bgra8,
                                BitmapAlphaMode.Premultiplied,
                                targetSize,
                                targetSize,
                                96,
                                96,
                                pixels);
                            await encoder.FlushAsync();

                            squarePngBytes = new byte[outStream.Size];
                            using (var reader = new DataReader(outStream.GetInputStreamAt(0)))
                            {
                                await reader.LoadAsync((uint)outStream.Size);
                                reader.ReadBytes(squarePngBytes);
                            }
                        }
                    }
                }
                catch { }

                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    using (var writer = new DataWriter(stream))
                    {
                        writer.WriteBytes(squarePngBytes ?? raw);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                    }
                }
                return "ms-appdata:///local/" + fileName;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string> GenerateMosaicTileAsync(List<string> thumbUrls, int gridSize, string fileName)
        {
            if (thumbUrls == null || thumbUrls.Count == 0) return null;

            int targetCount = gridSize * gridSize; // 4 for 2x2, 9 for 3x3
            int canvasSize = 336; // Native WP8.1 medium tile rendering canvas
            // Perfect pixel symmetry:
            // 3x3: pad = 3 -> 336 - 12 = 324 / 3 = 108px per tile (margins = 3px, gaps = 3px)
            // 2x2: pad = 6 -> 336 - 18 = 318 / 2 = 159px per tile (margins = 6px, gaps = 6px)
            int pad = gridSize == 3 ? 3 : 6;
            int tileSize = (canvasSize - pad * (gridSize + 1)) / gridSize;

            byte[] canvasPixels = new byte[canvasSize * canvasSize * 4];

            // Initialize dark background (#101010)
            for (int i = 0; i < canvasPixels.Length; i += 4)
            {
                canvasPixels[i] = 16;     // B
                canvasPixels[i + 1] = 16; // G
                canvasPixels[i + 2] = 16; // R
                canvasPixels[i + 3] = 255;// A
            }

            // Prepare URLs
            var urlsToFetch = new List<string>();
            for (int idx = 0; idx < targetCount; idx++)
            {
                string url = idx < thumbUrls.Count ? thumbUrls[idx] : thumbUrls[idx % thumbUrls.Count];
                urlsToFetch.Add(url);
            }

            // Download all in parallel
            var downloadTasks = urlsToFetch.Select(async u =>
            {
                try
                {
                    if (string.IsNullOrEmpty(u)) return null;
                    return await _httpClient.GetByteArrayAsync(u);
                }
                catch { return null; }
            }).ToArray();

            byte[][] downloadedBytes = await Task.WhenAll(downloadTasks);

            // Decode, center-crop to 1:1, and blit each thumbnail slot
            for (int idx = 0; idx < targetCount; idx++)
            {
                int r = idx / gridSize;
                int c = idx % gridSize;
                int destX = pad + c * (tileSize + pad);
                int destY = pad + r * (tileSize + pad);

                byte[] raw = downloadedBytes[idx];
                byte[] tileBytes = null;

                if (raw != null && raw.Length > 0)
                {
                    try
                    {
                        using (var ms = new InMemoryRandomAccessStream())
                        {
                            using (var writer = new DataWriter(ms.GetOutputStreamAt(0)))
                            {
                                writer.WriteBytes(raw);
                                await writer.StoreAsync();
                            }

                            var decoder = await BitmapDecoder.CreateAsync(ms);
                            uint srcW = decoder.PixelWidth;
                            uint srcH = decoder.PixelHeight;
                            uint cropSize = Math.Min(srcW, srcH);
                            uint cropX = (srcW - cropSize) / 2;
                            uint cropY = (srcH - cropSize) / 2;

                            var transform = new BitmapTransform
                            {
                                Bounds = new BitmapBounds
                                {
                                    X = cropX,
                                    Y = cropY,
                                    Width = cropSize,
                                    Height = cropSize
                                },
                                ScaledWidth = (uint)tileSize,
                                ScaledHeight = (uint)tileSize,
                                InterpolationMode = BitmapInterpolationMode.Fant
                            };
                            var pixelData = await decoder.GetPixelDataAsync(
                                BitmapPixelFormat.Bgra8,
                                BitmapAlphaMode.Premultiplied,
                                transform,
                                ExifOrientationMode.RespectExifOrientation,
                                ColorManagementMode.ColorManageToSRgb);
                            tileBytes = pixelData.DetachPixelData();
                        }
                    }
                    catch { }
                }

                // Blit pixel data into canvas
                if (tileBytes != null && tileBytes.Length >= tileSize * tileSize * 4)
                {
                    for (int y = 0; y < tileSize; y++)
                    {
                        int srcOffset = y * tileSize * 4;
                        int dstOffset = ((destY + y) * canvasSize + destX) * 4;
                        Array.Copy(tileBytes, srcOffset, canvasPixels, dstOffset, tileSize * 4);
                    }
                }
                else
                {
                    // Fallback solid color block
                    for (int y = 0; y < tileSize; y++)
                    {
                        int dstOffset = ((destY + y) * canvasSize + destX) * 4;
                        for (int x = 0; x < tileSize; x++)
                        {
                            int p = dstOffset + x * 4;
                            canvasPixels[p] = 32;     // B
                            canvasPixels[p + 1] = 32; // G
                            canvasPixels[p + 2] = 32; // R
                            canvasPixels[p + 3] = 255;// A
                        }
                    }
                }
            }

            // Save PNG to ApplicationData.Current.LocalFolder
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    (uint)canvasSize,
                    (uint)canvasSize,
                    96,
                    96,
                    canvasPixels);
                await encoder.FlushAsync();
            }

            return "ms-appdata:///local/" + fileName;
        }

        public static string FormatSquareThumbnail(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            // Google CDN / YouTube Music thumbnails (*.googleusercontent.com, *.ggpht.com)
            // Replace any size parameter (e.g. =w120-h120, =s192, =w60-h60-l90-rj) with 1:1 square crop =w480-h480-l90-rj
            if (url.Contains("googleusercontent.com") || url.Contains("ggpht.com"))
            {
                int eqIdx = url.LastIndexOf("=");
                if (eqIdx > 0)
                    return url.Substring(0, eqIdx) + "=w480-h480-l90-rj";
                return url + "=w480-h480-l90-rj";
            }

            // YouTube video thumbnails — avoid 4:3 letterboxed hqdefault
            if (url.Contains("hqdefault.jpg"))
                return url.Replace("hqdefault.jpg", "mqdefault.jpg");
            if (url.Contains("sddefault.jpg"))
                return url.Replace("sddefault.jpg", "mqdefault.jpg");

            return url;
        }

        private static bool IsValidTrack(YouTubeTrack t)
        {
            return t != null && !string.IsNullOrEmpty(t.ThumbnailUrl) && !string.IsNullOrEmpty(t.Title);
        }

        private class TileItem
        {
            public YouTubeTrack Track;
            public string Label;
        }
    }
}
