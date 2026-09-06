using System.ComponentModel;
using Windows.UI.Xaml;

namespace YTMusicWP
{
    public class YouTubeTrack : INotifyPropertyChanged
    {
        public string VideoId { get; set; }
        public string SetVideoId { get; set; }
        public string Title { get; set; }
        public string ChannelName { get; set; }
        public string ChannelId { get; set; }
        
        private double _coverWidth = 140;
        public double CoverWidth
        {
            get { return _coverWidth; }
            set
            {
                if (_coverWidth != value)
                {
                    _coverWidth = value;
                    OnPropertyChanged("CoverWidth");
                }
            }
        }

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

        private bool _isPlaying;
        public bool IsPlaying
        {
            get { return _isPlaying; }
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged("IsPlaying");
                    OnPropertyChanged("PlayingBadgeVisibility");
                    OnPropertyChanged("TitleColor");
                }
            }
        }

        public Visibility PlayingBadgeVisibility
        {
            get { return _isPlaying ? Visibility.Visible : Visibility.Collapsed; }
        }

        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _activeGreenBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 185, 84));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _defaultWhiteBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);

        public Windows.UI.Xaml.Media.Brush TitleColor
        {
            get { return _isPlaying ? _activeGreenBrush : _defaultWhiteBrush; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}

