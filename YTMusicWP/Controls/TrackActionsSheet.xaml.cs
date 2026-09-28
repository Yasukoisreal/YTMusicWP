using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Track options bottom sheet (Play, Add to Queue, Go to Radio, ...). The actions themselves live in
    /// MainPage and are wired through the *Click events; this control handles showing, sliding and closing.
    /// </summary>
    public sealed partial class TrackActionsSheet : UserControl
    {
        public event RoutedEventHandler PlayClick;
        public event RoutedEventHandler AddToQueueClick;
        public event RoutedEventHandler GoToRadioClick;
        public event RoutedEventHandler AddToPlaylistClick;
        public event RoutedEventHandler GoToArtistClick;
        public event RoutedEventHandler SleepTimerClick;
        public event RoutedEventHandler ShareClick;
        public event RoutedEventHandler DeleteClick;

        public TrackActionsSheet()
        {
            this.InitializeComponent();
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        public void Show(YouTubeTrack track)
        {
            BottomSheetTitle.Text = track.Title;
            BottomSheetArtist.Text = track.ChannelName;
            if (!string.IsNullOrEmpty(track.ThumbnailUrl))
            {
                try {
                    var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    bmp.DecodePixelWidth = 100;
                    bmp.UriSource = new Uri(track.ThumbnailUrl);
                    BottomSheetCover.ImageSource = bmp;
                } catch {}
            }

            // Show delete button only for downloaded (LOCAL:) tracks
            BottomSheetDeleteBtn.Visibility = (track.VideoId != null && track.VideoId.StartsWith("LOCAL:"))
                ? Visibility.Visible : Visibility.Collapsed;

            Services.MotionHelper.ShowSheet(this, Sheet);
        }

        public void Hide()
        {
            Services.MotionHelper.HideSheet(this, Sheet);
        }

        private void Content_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private static void Raise(RoutedEventHandler handler, object sender, RoutedEventArgs e)
        {
            if (handler != null) handler(sender, e);
        }

        private void Play_Click(object sender, RoutedEventArgs e) { Raise(PlayClick, sender, e); }
        private void AddToQueue_Click(object sender, RoutedEventArgs e) { Raise(AddToQueueClick, sender, e); }
        private void GoToRadio_Click(object sender, RoutedEventArgs e) { Raise(GoToRadioClick, sender, e); }
        private void AddToPlaylist_Click(object sender, RoutedEventArgs e) { Raise(AddToPlaylistClick, sender, e); }
        private void GoToArtist_Click(object sender, RoutedEventArgs e) { Raise(GoToArtistClick, sender, e); }
        private void SleepTimer_Click(object sender, RoutedEventArgs e) { Raise(SleepTimerClick, sender, e); }
        private void Share_Click(object sender, RoutedEventArgs e) { Raise(ShareClick, sender, e); }
        private void Delete_Click(object sender, RoutedEventArgs e) { Raise(DeleteClick, sender, e); }
    }
}
