using System;
using System.Collections.Generic;
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
            }
            catch { }
        }

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

                string xml = string.Format(
                    "<tile><visual version=\"2\">" +
                    "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                    "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text></binding>" +
                    "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2}</text></binding>" +
                    "</visual></tile>", safeThumb, safeTitle, safeArtist);

                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var notif = new TileNotification(doc)
                {
                    Tag = "nowplaying",
                    ExpirationTime = DateTimeOffset.UtcNow.AddHours(12)
                };
                updater.Update(notif);
            }
            catch { }
        }

        public static void UpdateRecommendations(IEnumerable<YouTubeTrack> tracks, int maxCount = 5)
        {
            try
            {
                if (!IsLiveTileEnabled || LiveTileMode != 0) return;
                if (tracks == null) return;

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(true);
                updater.Clear();

                int count = 0;
                foreach (var t in tracks)
                {
                    if (t == null || string.IsNullOrEmpty(t.ThumbnailUrl) || string.IsNullOrEmpty(t.Title)) continue;

                    string safeThumb = WebUtility.HtmlEncode(t.ThumbnailUrl);
                    string safeTitle = WebUtility.HtmlEncode(t.Title);
                    string safeArtist = WebUtility.HtmlEncode(t.ChannelName ?? "YouTube Music");

                    string xml = string.Format(
                        "<tile><visual version=\"2\">" +
                        "<binding template=\"TileSquare71x71Image\"><image id=\"1\" src=\"{0}\"/></binding>" +
                        "<binding template=\"TileSquare150x150PeekImageAndText04\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text></binding>" +
                        "<binding template=\"TileWide310x150PeekImage01\"><image id=\"1\" src=\"{0}\"/><text id=\"1\">{1}</text><text id=\"2\">{2}</text></binding>" +
                        "</visual></tile>", safeThumb, safeTitle, safeArtist);

                    var doc = new XmlDocument();
                    doc.LoadXml(xml);
                    var notif = new TileNotification(doc)
                    {
                        Tag = "rec_" + count,
                        ExpirationTime = DateTimeOffset.UtcNow.AddDays(1)
                    };
                    updater.Update(notif);

                    count++;
                    if (count >= maxCount) break;
                }
            }
            catch { }
        }
    }
}
