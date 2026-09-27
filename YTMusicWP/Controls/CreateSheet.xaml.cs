using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Floating "Create" menu above the bottom nav bar. MainPage animates it together with the
    /// nav "+" button, so this control only exposes the slide transform and raises one event per option.
    /// </summary>
    public sealed partial class CreateSheet : UserControl
    {
        public event RoutedEventHandler PlaylistClick;
        public event RoutedEventHandler CollabClick;
        public event RoutedEventHandler BlendClick;
        public event RoutedEventHandler ListenTogetherClick;

        public CreateSheet()
        {
            this.InitializeComponent();
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        /// <summary>Vertical slide of the menu card (0 = shown, 200 = hidden below).</summary>
        public TranslateTransform SheetTransform
        {
            get { return CreateSheetTransform; }
        }

        private void Playlist_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistClick != null) PlaylistClick(sender, e);
        }

        private void Collab_Click(object sender, RoutedEventArgs e)
        {
            if (CollabClick != null) CollabClick(sender, e);
        }

        private void Blend_Click(object sender, RoutedEventArgs e)
        {
            if (BlendClick != null) BlendClick(sender, e);
        }

        private void ListenTogether_Click(object sender, RoutedEventArgs e)
        {
            if (ListenTogetherClick != null) ListenTogetherClick(sender, e);
        }
    }
}
