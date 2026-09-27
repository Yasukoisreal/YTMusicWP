using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Bottom sheet showing song credits. MainPage fetches the data; this control only renders it.
    /// </summary>
    public sealed partial class SongCreditsDialog : UserControl
    {
        public SongCreditsDialog()
        {
            this.InitializeComponent();
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        /// <summary>Opens the sheet in the loading state with placeholder values.</summary>
        public void ShowLoading(string trackTitle, string albumName)
        {
            Visibility = Visibility.Visible;
            SongCreditsLoading.Visibility = Visibility.Visible;
            SongCreditsContent.Visibility = Visibility.Collapsed;

            SongCreditsTrackTitle.Text = trackTitle ?? "";
            CreditsPerformedBySection.Visibility = Visibility.Collapsed;
            CreditsWrittenBySection.Visibility = Visibility.Collapsed;
            CreditsProducedBySection.Visibility = Visibility.Collapsed;
            CreditsProvidedBySection.Visibility = Visibility.Collapsed;
            CreditsViewsText.Text = "--";
            CreditsDateText.Text = "--";
            CreditsAlbumText.Text = !string.IsNullOrEmpty(albumName) ? albumName : "Single / Album";
            CreditsAudioText.Text = "Opus / AAC";
        }

        /// <summary>Fills in whatever the credits contain; empty fields keep their placeholders.</summary>
        public void ShowCredits(SongCredits credits, string channelName)
        {
            if (credits == null) return;

            if (!string.IsNullOrEmpty(credits.PerformedBy))
            {
                CreditsPerformedByText.Text = credits.PerformedBy;
                CreditsPerformedBySection.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrEmpty(channelName))
            {
                CreditsPerformedByText.Text = channelName;
                CreditsPerformedBySection.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrEmpty(credits.WrittenBy))
            {
                CreditsWrittenByText.Text = credits.WrittenBy;
                CreditsWrittenBySection.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrEmpty(credits.ProducedBy))
            {
                CreditsProducedByText.Text = credits.ProducedBy;
                CreditsProducedBySection.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrEmpty(credits.ProvidedBy))
            {
                CreditsProvidedByText.Text = credits.ProvidedBy;
                CreditsProvidedBySection.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrEmpty(credits.ViewCount))
            {
                CreditsViewsText.Text = credits.ViewCount;
            }

            if (!string.IsNullOrEmpty(credits.PublishDate))
            {
                CreditsDateText.Text = credits.PublishDate;
            }

            if (!string.IsNullOrEmpty(credits.Album))
            {
                CreditsAlbumText.Text = credits.Album;
            }

            if (!string.IsNullOrEmpty(credits.AudioFormat))
            {
                CreditsAudioText.Text = credits.AudioFormat;
            }
        }

        /// <summary>Hides the loading indicator and shows the content area.</summary>
        public void EndLoading()
        {
            SongCreditsLoading.Visibility = Visibility.Collapsed;
            SongCreditsContent.Visibility = Visibility.Visible;
        }

        public void Close()
        {
            Visibility = Visibility.Collapsed;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
