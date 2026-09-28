using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Samples tab UI. Feed loading, video playback and swiping live in MainPage.Shorts.cs;
    /// this control only forwards its input events (original sender kept).
    /// </summary>
    public sealed partial class ShortsPanel : UserControl
    {
        public event RoutedEventHandler LikeClick;
        public event RoutedEventHandler SaveClick;
        public event RoutedEventHandler ShareClick;
        public event RoutedEventHandler PlayClick;
        public event RoutedEventHandler MoreClick;
        public event TappedEventHandler SongBarTapped;
        public event TappedEventHandler StageTapped;
        public event ManipulationDeltaEventHandler SwipeDelta;
        public event ManipulationCompletedEventHandler SwipeCompleted;

        public ShortsPanel()
        {
            this.InitializeComponent();
        }

        private static void Raise(RoutedEventHandler handler, object sender, RoutedEventArgs e)
        {
            if (handler != null) handler(sender, e);
        }

        private void Like_Click(object sender, RoutedEventArgs e) { Raise(LikeClick, sender, e); }
        private void Save_Click(object sender, RoutedEventArgs e) { Raise(SaveClick, sender, e); }
        private void Share_Click(object sender, RoutedEventArgs e) { Raise(ShareClick, sender, e); }
        private void Play_Click(object sender, RoutedEventArgs e) { Raise(PlayClick, sender, e); }
        private void More_Click(object sender, RoutedEventArgs e) { Raise(MoreClick, sender, e); }
        private void SongBar_Tapped(object sender, TappedRoutedEventArgs e) { if (SongBarTapped != null) SongBarTapped(sender, e); }
        private void SampleStage_Tapped(object sender, TappedRoutedEventArgs e) { if (StageTapped != null) StageTapped(sender, e); }
        private void Shorts_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) { if (SwipeDelta != null) SwipeDelta(sender, e); }
        private void Shorts_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e) { if (SwipeCompleted != null) SwipeCompleted(sender, e); }
    }
}
