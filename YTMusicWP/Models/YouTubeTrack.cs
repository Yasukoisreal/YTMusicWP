using System.ComponentModel;
using Windows.UI.Xaml;

namespace YTMusicWP
{
    public class YouTubeTrack : INotifyPropertyChanged
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string ChannelName { get; set; }
        public string ChannelId { get; set; }

        private string _thumbnailUrl;
        public string ThumbnailUrl
        {
            get { return _thumbnailUrl; }
            set
            {
                if (_thumbnailUrl != value)
                {
                    _thumbnailUrl = value;
                    OnPropertyChanged("ThumbnailUrl");
                }
            }
        }

        public Visibility DeleteVisibility
        {
            get { return (VideoId != null && VideoId.StartsWith("LOCAL:")) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility DownloadVisibility
        {
            get { return (VideoId != null && VideoId.StartsWith("LOCAL:")) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
