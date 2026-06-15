using Windows.UI.Xaml;

namespace YTMusicWP
{
    public class YouTubeTrack
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string ChannelName { get; set; }
        public string ChannelId { get; set; }
        public string ThumbnailUrl { get; set; }

        public Visibility DeleteVisibility
        {
            get { return (VideoId != null && VideoId.StartsWith("LOCAL:")) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility DownloadVisibility
        {
            get { return (VideoId != null && VideoId.StartsWith("LOCAL:")) ? Visibility.Collapsed : Visibility.Visible; }
        }
    }
}
