using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Now Playing "more" menu. The actions live in MainPage (wired through the *Click events); MainPage also
    /// updates the header and status texts directly (they are x:FieldModifier="internal").
    /// </summary>
    public sealed partial class NowPlayingMenu : UserControl
    {
        /// <summary>Raised by the backdrop and the Close button; MainPage decides how to close.</summary>
        public event RoutedEventHandler CloseClick;
        public event RoutedEventHandler AddToPlaylistClick;
        public event RoutedEventHandler GoToArtistClick;
        public event RoutedEventHandler GoToRadioClick;
        public event RoutedEventHandler SongCreditsClick;
        public event RoutedEventHandler PlaybackSpeedClick;
        public event RoutedEventHandler SleepTimerClick;
        public event RoutedEventHandler ShareClick;
        public event RoutedEventHandler DownloadClick;
        public event RoutedEventHandler LikeClick;
        public event RoutedEventHandler WatchLaterClick;
        public event RoutedEventHandler LiveDebugClick;

        /// <summary>Raised when the slide-down animation of <see cref="Hide"/> has finished.</summary>
        public event EventHandler Closed;

        public NowPlayingMenu()
        {
            this.InitializeComponent();
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        /// <summary>Switches the Download item between "Download" and a green "Downloaded" state.</summary>
        public void SetDownloaded(bool downloaded)
        {
            DownloadIcon.Visibility = downloaded ? Visibility.Collapsed : Visibility.Visible;
            DownloadedIcon.Visibility = downloaded ? Visibility.Visible : Visibility.Collapsed;
            DownloadText.Text = downloaded ? "Downloaded" : "Download";
            DownloadText.Foreground = downloaded ? DownloadedIcon.Fill : DownloadIcon.Fill;
        }

        public void Open()
        {
            Visibility = Visibility.Visible;
            SlideUpStoryboard.Begin();
        }

        /// <summary>Slides the menu down, then collapses it and raises <see cref="Closed"/>.</summary>
        public void Hide()
        {
            SlideDownStoryboard.Begin();
        }

        private void SlideDownStoryboard_Completed(object sender, object e)
        {
            Visibility = Visibility.Collapsed;
            if (Closed != null) Closed(this, EventArgs.Empty);
        }

        private static void Raise(RoutedEventHandler handler, object sender, RoutedEventArgs e)
        {
            if (handler != null) handler(sender, e);
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Raise(CloseClick, sender, e); }
        private void AddToPlaylist_Click(object sender, RoutedEventArgs e) { Raise(AddToPlaylistClick, sender, e); }
        private void GoToArtist_Click(object sender, RoutedEventArgs e) { Raise(GoToArtistClick, sender, e); }
        private void GoToRadio_Click(object sender, RoutedEventArgs e) { Raise(GoToRadioClick, sender, e); }
        private void SongCredits_Click(object sender, RoutedEventArgs e) { Raise(SongCreditsClick, sender, e); }
        private void PlaybackSpeed_Click(object sender, RoutedEventArgs e) { Raise(PlaybackSpeedClick, sender, e); }
        private void SleepTimer_Click(object sender, RoutedEventArgs e) { Raise(SleepTimerClick, sender, e); }
        private void Share_Click(object sender, RoutedEventArgs e) { Raise(ShareClick, sender, e); }
        private void Download_Click(object sender, RoutedEventArgs e) { Raise(DownloadClick, sender, e); }
        private void Like_Click(object sender, RoutedEventArgs e) { Raise(LikeClick, sender, e); }
        private void WatchLater_Click(object sender, RoutedEventArgs e) { Raise(WatchLaterClick, sender, e); }
        private void LiveDebug_Click(object sender, RoutedEventArgs e) { Raise(LiveDebugClick, sender, e); }
    }
}
