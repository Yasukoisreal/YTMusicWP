using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Music Shorts screen. Swipe handling and playback stay in MainPage.Shorts.cs; this control forwards its events (original sender kept).
    /// </summary>
    public sealed partial class ShortsPanel : UserControl
    {
        public event RoutedEventHandler ShortsBackClick;
        public event RoutedEventHandler ShortsHeartClick;
        public event RoutedEventHandler ShortsPlayCurrentClick;
        public event TappedEventHandler ShortsSongBarTapped;
        public event ManipulationCompletedEventHandler ShortsManipulationCompleted;
        public event ManipulationDeltaEventHandler ShortsManipulationDelta;

        public ShortsPanel()
        {
            this.InitializeComponent();
        }

        private void ShortsBack_Click(object sender, RoutedEventArgs e) { if (ShortsBackClick != null) ShortsBackClick(sender, e); }
        private void ShortsHeart_Click(object sender, RoutedEventArgs e) { if (ShortsHeartClick != null) ShortsHeartClick(sender, e); }
        private void ShortsPlayCurrent_Click(object sender, RoutedEventArgs e) { if (ShortsPlayCurrentClick != null) ShortsPlayCurrentClick(sender, e); }
        private void ShortsSongBar_Tapped(object sender, TappedRoutedEventArgs e) { if (ShortsSongBarTapped != null) ShortsSongBarTapped(sender, e); }
        private void Shorts_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e) { if (ShortsManipulationCompleted != null) ShortsManipulationCompleted(sender, e); }
        private void Shorts_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) { if (ShortsManipulationDelta != null) ShortsManipulationDelta(sender, e); }
    }
}
