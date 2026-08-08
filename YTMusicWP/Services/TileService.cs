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
                    if (value == 2) ClearLiveTile();
                }
                catch { }
            }
        }

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
                updater.EnableNotificationQueue(true);

                string squareThumb = FormatSquareThumbnail(thumbUrl);
                string safeThumb = WebUtility.HtmlEncode(squareThumb);
                string safeTitle = WebUtility.HtmlEncode(title ?? "");
                string safeArtist = WebUtility.HtmlEncode(artist ?? "");

                // Medium tile: peek with "♪ Now Playing" prefix
                // Wide tile: Photo-style vertical slide/peek with song & artist
                // Large tile (Windows 10 / 8.1): full image and text
                string xml = string.Format(
                    "<tile><visual version=\"2\">" +
                    "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                    "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text></binding>" +
                    "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text><text id=\"2\">{2}</text></binding>" +
                    "<binding template=\"TileSquare310x310ImageAndText01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text><text id=\"2\">{2}</text></binding>" +
                    "</visual></tile>", safeThumb, safeTitle, safeArtist);

                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var notif = new TileNotification(doc)
                {
                    Tag = "nowplaying",
                    ExpirationTime = DateTimeOffset.UtcNow.AddHours(12)
                };
                updater.Update(notif);

                // If Up Next has tracks, push an Up Next tile to rotate with Now Playing
                if (upNextTracks != null)
                {
                    var nextList = upNextTracks.Where(IsValidTrack).Take(3).ToList();
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
                            "<binding template=\"TileWide310x150SmallImageAndText01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">UP NEXT</text><text id=\"2\">{1} · {2}</text></binding>" +
                            "<binding template=\"TileSquare310x310ImageAndText01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">Up Next: {1}</text><text id=\"2\">{2}</text></binding>" +
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
            int maxCount = 5)
        {
            Task.Run(async () =>
            {
                await UpdateRecommendationsAsync(homeTracks, favoriteTracks, historyTracks, maxCount);
            });
        }

        public static void UpdateRecommendations(IEnumerable<YouTubeTrack> homeTracks, int maxCount)
        {
            UpdateRecommendations(homeTracks, null, null, maxCount);
        }

        public static async Task UpdateRecommendationsAsync(
            IEnumerable<YouTubeTrack> homeTracks,
            IEnumerable<YouTubeTrack> favoriteTracks = null,
            IEnumerable<YouTubeTrack> historyTracks = null,
            int maxCount = 5)
        {
            try
            {
                if (!IsLiveTileEnabled || LiveTileMode != 0) return;

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

                // 2. Generate People Hub Style Mosaic Tiles (3x3 = 9 boxes, 2x2 = 4 boxes)
                string mosaic3x3_Path = null;
                string mosaic2x2_Path = null;

                try
                {
                    if (thumbUrls.Count >= 4)
                    {
                        var task9 = GenerateMosaicTileAsync(thumbUrls.Take(9).ToList(), 3, "tile_mosaic_3x3.png");
                        var task4 = GenerateMosaicTileAsync(thumbUrls.Take(4).ToList(), 2, "tile_mosaic_2x2.png");
                        await Task.WhenAll(task9, task4);
                        mosaic3x3_Path = task9.Result;
                        mosaic2x2_Path = task4.Result;
                    }
                }
                catch { }

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(true);
                updater.Clear();

                // 3. Build Notification Queue items
                // --- Item 0: People 9-Box Mosaic (Medium) + Wide 5-Image Flipping Collection (Wide) ---
                if (!string.IsNullOrEmpty(mosaic3x3_Path) && thumbUrls.Count >= 5)
                {
                    PushImageCollectionTile(updater, mosaic3x3_Path, thumbUrls.Take(5).ToList(), "mosaic_9_tile", 0);
                }
                else if (pool.Count > 0)
                {
                    PushSingleItemTile(updater, pool[0], 0);
                }

                // --- Item 1: Typography Block Tile (#1 Charts / Billboard Style) ---
                if (pool.Count > 0)
                {
                    var topTrending = pool.FirstOrDefault(p => p.Label == "Trending") ?? pool[0];
                    PushBlockNumberTile(updater, topTrending, "#1", "Top Charts", 1);
                }

                // --- Item 2: People 4-Box Mosaic (Medium) + Photos-Style Vertical Slide (Wide) ---
                if (!string.IsNullOrEmpty(mosaic2x2_Path) && pool.Count > 1)
                {
                    PushMosaicAndSlideTile(updater, mosaic2x2_Path, pool[1], "mosaic_4_tile", 2);
                }
                else if (pool.Count > 1)
                {
                    PushSingleItemTile(updater, pool[1], 2);
                }

                // --- Item 3: Featured Track with Wide 5-Image Flipping Collection ---
                if (thumbUrls.Count >= 5 && pool.Count > 2)
                {
                    var altThumbs = thumbUrls.Skip(1).Take(5).ToList();
                    if (altThumbs.Count < 5) altThumbs = thumbUrls.Take(5).ToList();
                    PushImageCollectionTile(updater, FormatSquareThumbnail(pool[2].Track.ThumbnailUrl), altThumbs, "alt_collection", 3);
                }
                else if (pool.Count > 2)
                {
                    PushSingleItemTile(updater, pool[2], 3);
                }

                // --- Item 4: Featured Favorite Track with Photos-Style Vertical Slide ---
                if (pool.Count > 3)
                {
                    PushSingleItemTile(updater, pool[3], 4);
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

                if (!string.IsNullOrEmpty(mosaicPath) && thumbUrls.Count >= 5)
                {
                    PushImageCollectionTile(updater, mosaicPath, thumbUrls.Take(5).ToList(), "sec_mosaic", 0);
                }
                else
                {
                    PushSingleItemTile(updater, new TileItem { Track = tracks[0], Label = displayName }, 0);
                }

                if (tracks.Count > 1)
                {
                    PushSingleItemTile(updater, new TileItem { Track = tracks[1], Label = displayName }, 1);
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

                // Afternoon (12:00): Afternoon Energy
                DateTime afternoonTime = now.Date.AddDays(now.Hour >= 12 ? 1 : 0).AddHours(12);
                var afternoonTrack = validTracks[r.Next(validTracks.Count)];
                ScheduleSingleDaypart(updater, afternoonTrack, "⚡ Afternoon Energy", "daypart_afternoon", afternoonTime);

                // Evening (18:00): Night Chill & Relax
                DateTime eveningTime = now.Date.AddDays(now.Hour >= 18 ? 1 : 0).AddHours(18);
                var eveningTrack = validTracks[r.Next(validTracks.Count)];
                ScheduleSingleDaypart(updater, eveningTrack, "🌙 Night Chill & Relax", "daypart_evening", eveningTime);
            }
            catch { }
        }

        private static void ScheduleSingleDaypart(TileUpdater updater, YouTubeTrack track, string greeting, string tag, DateTime deliveryTime)
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
                    "<binding template=\"TileSquare310x310ImageAndText01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2} · {3}</text></binding>" +
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
            string xml = string.Format(
                "<tile><visual version=\"2\">" +
                "<binding template=\"TileSquare150x150Block\"><text id=\"1\">{0}</text><text id=\"2\">{1}</text></binding>" +
                "<binding template=\"TileWide310x150BlockAndText01\"><text id=\"1\">{0}</text><text id=\"2\">{1}</text><text id=\"3\">{2}</text><text id=\"4\">{3}</text></binding>" +
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

        private static void PushSingleItemTile(TileUpdater updater, TileItem item, int index)
        {
            string squareThumb = FormatSquareThumbnail(item.Track.ThumbnailUrl);
            string safeThumb = WebUtility.HtmlEncode(squareThumb ?? "");
            string safeTitle = WebUtility.HtmlEncode(item.Track.Title ?? "");
            string safeArtist = WebUtility.HtmlEncode(item.Track.ChannelName ?? "YouTube Music");
            string safeLabel = WebUtility.HtmlEncode(item.Label ?? "YouTube Music");

            // Medium: TileSquare150x150PeekImageAndText04 (Peeks title over cover)
            // Wide: TileWide310x150PeekImage01 (Photos app style: vertical sliding cover art)
            // Large: TileSquare310x310ImageAndText01
            string xml = string.Format(
                "<tile><visual version=\"2\">" +
                "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text></binding>" +
                "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2} · {3}</text></binding>" +
                "<binding template=\"TileSquare310x310ImageAndText01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2}</text></binding>" +
                "</visual></tile>", safeThumb, safeTitle, safeArtist, safeLabel);

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var notif = new TileNotification(doc)
            {
                Tag = "rec_" + index,
                ExpirationTime = DateTimeOffset.UtcNow.AddDays(1)
            };
            updater.Update(notif);
        }

        private static void PushMosaicAndSlideTile(TileUpdater updater, string mediumMosaicUri, TileItem wideItem, string tag, int index)
        {
            string safeMediumThumb = WebUtility.HtmlEncode(mediumMosaicUri ?? "");
            string wideThumb = FormatSquareThumbnail(wideItem.Track.ThumbnailUrl);
            string safeWideThumb = WebUtility.HtmlEncode(wideThumb ?? "");
            string safeTitle = WebUtility.HtmlEncode(wideItem.Track.Title ?? "");
            string safeArtist = WebUtility.HtmlEncode(wideItem.Track.ChannelName ?? "YouTube Music");
            string safeLabel = WebUtility.HtmlEncode(wideItem.Label ?? "YouTube Music");

            // Medium tile: 4-Box Mosaic (People style)
            // Wide tile: Photos app style vertical slide
            string xml = string.Format(
                "<tile><visual version=\"2\">" +
                "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                "<binding template=\"TileSquare150x150Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{1}\"/><text id=\"1\">{2}</text><text id=\"2\">{3} · {4}</text></binding>" +
                "<binding template=\"TileSquare310x310ImageAndText01\"><image id=\"1\" src=\"{1}\"/><text id=\"1\">{2}</text><text id=\"2\">{3}</text></binding>" +
                "</visual></tile>", safeMediumThumb, safeWideThumb, safeTitle, safeArtist, safeLabel);

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var notif = new TileNotification(doc)
            {
                Tag = tag ?? ("rec_" + index),
                ExpirationTime = DateTimeOffset.UtcNow.AddDays(1)
            };
            updater.Update(notif);
        }

        private static void PushImageCollectionTile(TileUpdater updater, string mediumThumbUri, List<string> wideThumbs5, string tag, int index)
        {
            string safeMediumThumb = WebUtility.HtmlEncode(mediumThumbUri ?? "");
            while (wideThumbs5.Count < 5) wideThumbs5.Add(wideThumbs5.FirstOrDefault() ?? "");

            string t1 = WebUtility.HtmlEncode(wideThumbs5[0] ?? "");
            string t2 = WebUtility.HtmlEncode(wideThumbs5[1] ?? "");
            string t3 = WebUtility.HtmlEncode(wideThumbs5[2] ?? "");
            string t4 = WebUtility.HtmlEncode(wideThumbs5[3] ?? "");
            string t5 = WebUtility.HtmlEncode(wideThumbs5[4] ?? "");

            // Medium tile: People-style mosaic or primary artwork
            // Wide tile: TileWide310x150ImageCollection (5 album arts with native flipping & sliding transitions)
            // Large tile: TileSquare310x310ImageCollection
            string xml = string.Format(
                "<tile><visual version=\"2\">" +
                "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                "<binding template=\"TileSquare150x150Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                "<binding template=\"TileWide310x150ImageCollection\" fallback=\"TileWideImageCollection\">" +
                "<image id=\"1\" src=\"{1}\"/><image id=\"2\" src=\"{2}\"/><image id=\"3\" src=\"{3}\"/><image id=\"4\" src=\"{4}\"/><image id=\"5\" src=\"{5}\"/>" +
                "</binding>" +
                "<binding template=\"TileSquare310x310ImageCollection\">" +
                "<image id=\"1\" src=\"{1}\"/><image id=\"2\" src=\"{2}\"/><image id=\"3\" src=\"{3}\"/><image id=\"4\" src=\"{4}\"/><image id=\"5\" src=\"{5}\"/>" +
                "</binding>" +
                "</visual></tile>", safeMediumThumb, t1, t2, t3, t4, t5);

            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var notif = new TileNotification(doc)
            {
                Tag = tag ?? ("rec_" + index),
                ExpirationTime = DateTimeOffset.UtcNow.AddDays(1)
            };
            updater.Update(notif);
        }

        // ── People Hub Mosaic Generator (WinRT Bitmap Composition) ──

        public static async Task<string> GenerateMosaicTileAsync(List<string> thumbUrls, int gridSize, string fileName)
        {
            if (thumbUrls == null || thumbUrls.Count == 0) return null;

            int targetCount = gridSize * gridSize; // 4 for 2x2, 9 for 3x3
            int canvasSize = 336; // Native WP8.1 medium tile rendering canvas
            int pad = gridSize == 3 ? 4 : 6;
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

            // Download and decode each thumbnail slot
            for (int idx = 0; idx < targetCount; idx++)
            {
                int r = idx / gridSize;
                int c = idx % gridSize;
                int destX = pad + c * (tileSize + pad);
                int destY = pad + r * (tileSize + pad);

                string url = idx < thumbUrls.Count ? thumbUrls[idx] : thumbUrls[idx % thumbUrls.Count];
                byte[] tileBytes = null;

                try
                {
                    if (!string.IsNullOrEmpty(url))
                    {
                        byte[] raw = await _httpClient.GetByteArrayAsync(url);
                        using (var ms = new InMemoryRandomAccessStream())
                        {
                            using (var writer = new DataWriter(ms.GetOutputStreamAt(0)))
                            {
                                writer.WriteBytes(raw);
                                await writer.StoreAsync();
                            }

                            var decoder = await BitmapDecoder.CreateAsync(ms);
                            var transform = new BitmapTransform
                            {
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
                }
                catch { }

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
