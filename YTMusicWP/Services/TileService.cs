using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.UI.Notifications;

namespace YTMusicWP.Services
{
    public static class TileService
    {
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

        // ── Badge: "playing" glyph on Lock Screen & Tile ──

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

        // ── Now Playing Tile (called from AudioTask background) ──

        public static void UpdateNowPlaying(string title, string artist, string thumbUrl)
        {
            try
            {
                if (!IsLiveTileEnabled || LiveTileMode == 2) return;
                if (string.IsNullOrEmpty(thumbUrl)) return;

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(true);

                string safeThumb = WebUtility.HtmlEncode(thumbUrl);
                string safeTitle = WebUtility.HtmlEncode(title ?? "");
                string safeArtist = WebUtility.HtmlEncode(artist ?? "");

                // Medium tile: peek with "♪ Now Playing" prefix
                // Wide tile: peek with full title + artist
                string xml = string.Format(
                    "<tile><visual version=\"2\">" +
                    "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                    "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">♪ {1}</text></binding>" +
                    "<binding template=\"TileWide310x150SmallImageAndText01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text></binding>" +
                    "</visual></tile>", safeThumb, safeTitle, safeArtist);

                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var notif = new TileNotification(doc)
                {
                    Tag = "nowplaying",
                    ExpirationTime = DateTimeOffset.UtcNow.AddHours(12)
                };
                updater.Update(notif);

                SetPlayingBadge();
            }
            catch { }
        }

        // ── Recommendations: mix Home + Favorites + History ──

        public static void UpdateRecommendations(
            IEnumerable<YouTubeTrack> homeTracks,
            IEnumerable<YouTubeTrack> favoriteTracks = null,
            IEnumerable<YouTubeTrack> historyTracks = null,
            int maxCount = 5)
        {
            try
            {
                if (!IsLiveTileEnabled || LiveTileMode != 0) return;

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(true);
                updater.Clear();

                // Build a mixed list: 2-3 home recs, 1 favorite, 1 recently played
                var tiles = new List<TileItem>();

                if (homeTracks != null)
                {
                    foreach (var t in homeTracks)
                    {
                        if (IsValidTrack(t))
                            tiles.Add(new TileItem { Track = t, Label = "Trending" });
                        if (tiles.Count >= 3) break;
                    }
                }

                if (favoriteTracks != null)
                {
                    // Pick a random favorite to keep it fresh each time
                    var favList = favoriteTracks.Where(t => IsValidTrack(t)).ToList();
                    if (favList.Count > 0)
                    {
                        var pick = favList[new Random().Next(favList.Count)];
                        // Avoid duplicate
                        if (!tiles.Any(x => x.Track.VideoId == pick.VideoId))
                            tiles.Add(new TileItem { Track = pick, Label = "Your Favorite" });
                    }
                }

                if (historyTracks != null)
                {
                    // Pick most recent history track that isn't already in tiles
                    foreach (var t in historyTracks)
                    {
                        if (IsValidTrack(t) && !tiles.Any(x => x.Track.VideoId == t.VideoId))
                        {
                            tiles.Add(new TileItem { Track = t, Label = "Recently Played" });
                            break;
                        }
                    }
                }

                int count = 0;
                foreach (var item in tiles)
                {
                    if (count >= maxCount) break;
                    PushRecommendationTile(updater, item, count);
                    count++;
                }
            }
            catch { }
        }

        // Overload for backward compatibility (home tracks only)
        public static void UpdateRecommendations(IEnumerable<YouTubeTrack> homeTracks, int maxCount)
        {
            UpdateRecommendations(homeTracks, null, null, maxCount);
        }

        private static void PushRecommendationTile(TileUpdater updater, TileItem item, int index)
        {
            string safeThumb = WebUtility.HtmlEncode(item.Track.ThumbnailUrl);
            string safeTitle = WebUtility.HtmlEncode(item.Track.Title);
            string safeArtist = WebUtility.HtmlEncode(item.Track.ChannelName ?? "YouTube Music");
            string safeLabel = WebUtility.HtmlEncode(item.Label);

            // Wide tile uses SmallImageAndText for a sleek layout with label
            // Medium tile uses PeekImage for album art + title
            string xml = string.Format(
                "<tile><visual version=\"2\">" +
                "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text></binding>" +
                "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2} · {3}</text></binding>" +
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
