using System;
using System.Threading.Tasks;
using Windows.Security.Authentication.Web;
using Windows.UI.Xaml;
using YTMusicWP.Services;

namespace YTMusicWP
{
    /// <summary>Last.fm: sign-in from Settings, "now playing" on each new song, scrobbles from the listening log.</summary>
    public sealed partial class MainPage
    {
        private void LastFm_Click(object sender, RoutedEventArgs e)
        {
            if (!LastFmClient.IsAvailable) return;
            if (LastFmClient.IsSignedIn)
            {
                LastFmClient.SignOut();
                RefreshLastFmSettings();
                ShowToast("Disconnected from Last.fm");
                return;
            }

            try
            {
                // The app is suspended while the sign-in page is up; App.OnActivated hands the result back to
                // HandleLastFmSignInContinuation
                WebAuthenticationBroker.AuthenticateAndContinue(LastFmClient.AuthUri, new Uri(LastFmClient.CallbackUrl));
            }
            catch (Exception ex)
            {
                ShowToast("Couldn't open Last.fm sign-in: " + ex.Message);
            }
        }

        public async void HandleLastFmSignInContinuation(WebAuthenticationResult result)
        {
            if (result == null || result.ResponseStatus != WebAuthenticationStatus.Success)
            {
                ShowToast("Last.fm sign-in cancelled");
                return;
            }

            bool ok = await LastFmClient.CompleteSignInAsync(result.ResponseData);
            RefreshLastFmSettings();
            ShowToast(ok ? "Connected to Last.fm as " + LastFmClient.UserName : "Last.fm sign-in failed, please try again");
        }

        /// <summary>Shows the Last.fm card (only in builds with API credentials) with the current account.</summary>
        private void RefreshLastFmSettings()
        {
            SettingsPanel.LastFmSection.Visibility = LastFmClient.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
            if (!LastFmClient.IsAvailable) return;

            if (LastFmClient.IsSignedIn)
            {
                SettingsPanel.LastFmStatusText.Text = "Scrobbling as " + LastFmClient.UserName + ". Songs count once played for half their length or 4 minutes.";
                SettingsPanel.LastFmButtonText.Text = "Disconnect";
            }
            else
            {
                SettingsPanel.LastFmStatusText.Text = "Scrobble the songs you listen to (played for half their length or 4 minutes) to your Last.fm profile.";
                SettingsPanel.LastFmButtonText.Text = "Connect Last.fm";
            }
        }

        /// <summary>
        /// A new song started: show it as "now playing", and scrobble the one before once the audio task has logged it.
        /// </summary>
        private async void OnLastFmTrackStarted(string title, string artist)
        {
            if (!LastFmClient.IsSignedIn) return;
            try
            {
                await LastFmClient.UpdateNowPlayingAsync(title, artist, 0);
                await Task.Delay(5000);
                await LastFmClient.ScrobblePendingAsync();
            }
            catch { }
        }

        /// <summary>Sends plays that were logged while the app was closed or suspended.</summary>
        private void ScrobblePendingLastFm()
        {
            if (!LastFmClient.IsSignedIn) return;
            var ignored = LastFmClient.ScrobblePendingAsync();
        }
    }
}
