using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using YTMusicWP.Services;

namespace YTMusicWP
{
    /// <summary>"New from your artists" on Home: recent albums and singles of the artists the user follows.</summary>
    public sealed partial class MainPage
    {
        /// <summary>Shows what was found before, then checks a few followed artists for new releases (after startup).</summary>
        private async void CheckNewReleasesAsync()
        {
            try
            {
                ShowNewReleases(await NewReleasesTracker.GetRecentAsync());

                var artists = _youtubeSubscriptions
                    .Where(s => !string.IsNullOrEmpty(s.ChannelId))
                    .Select(s => new KeyValuePair<string, string>(s.ChannelId, s.Title))
                    .ToList();
                if (artists.Count == 0) return;

                var found = await NewReleasesTracker.CheckAsync(artists);
                if (found.Count == 0) return;

                ShowNewReleases(await NewReleasesTracker.GetRecentAsync());
                ShowToast(found.Count == 1
                    ? "New from " + found[0].Artist + ": " + found[0].Title
                    : found.Count + " new releases from artists you follow");
            }
            catch { }
        }

        private void ShowNewReleases(List<ReleaseInfo> releases)
        {
            if (releases == null || releases.Count == 0)
            {
                HomeNewReleasesSection.Visibility = Visibility.Collapsed;
                HomeNewReleasesCarousel.ItemsSource = null;
                return;
            }

            HomeNewReleasesTitleText.Text = InnerTubeClient.CurrentLanguage == "vi" ? "Mới từ nghệ sĩ bạn theo dõi" : "New from your artists";
            // "ALBUM:" items open the album page (PlayTrack routes them) instead of playing
            HomeNewReleasesCarousel.ItemsSource = releases.Select(r => new YouTubeTrack
            {
                VideoId = "ALBUM:" + r.BrowseId,
                ItemType = "album",
                Title = r.Title,
                ChannelName = r.Artist,
                ThumbnailUrl = r.ThumbnailUrl,
                Subtitle = r.Subtitle
            }).ToList();
            HomeNewReleasesSection.Visibility = Visibility.Visible;
        }
    }
}
