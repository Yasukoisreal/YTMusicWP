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
        public string AlbumName { get; set; }
        public string CreditsBrowseId { get; set; }
        
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

        public string Subtitle { get; set; }
        public string ItemType { get; set; }
        public bool IsLive { get; set; }

        public Visibility LiveBadgeVisibility
        {
            get { return IsLive ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ArtistThumbVisibility
        {
            get { return (ItemType == "artist" || (VideoId != null && VideoId.StartsWith("CHANNEL:"))) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility SquareThumbVisibility
        {
            get { return (ItemType != "artist" && (VideoId == null || !VideoId.StartsWith("CHANNEL:"))) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string DisplaySubtitle
        {
            get { return !string.IsNullOrEmpty(Subtitle) ? Subtitle : ChannelName; }
        }

        public string Duration { get; set; }
        public Visibility DurationVisibility
        {
            get { return !string.IsNullOrEmpty(Duration) ? Visibility.Visible : Visibility.Collapsed; }
        }

        private double _playProgressPercent = 0.0;
        public double PlayProgressPercent
        {
            get { return _playProgressPercent; }
            set
            {
                if (_playProgressPercent != value)
                {
                    _playProgressPercent = value;
                    OnPropertyChanged("PlayProgressPercent");
                    OnPropertyChanged("ProgressWidth");
                    OnPropertyChanged("ProgressVisibility");
                    OnPropertyChanged("SpeedDialProgressWidth");
                }
            }
        }

        public double ProgressWidth
        {
            get { return System.Math.Max(8, System.Math.Min(100, 100 * (_playProgressPercent > 0 ? _playProgressPercent : 0.45))); }
        }

        public bool HasChevron
        {
            get
            {
                return (ItemType == "liked" || ItemType == "playlist" || ItemType == "album" || ItemType == "mix" || ItemType == "radio"
                    || (VideoId != null && (VideoId.StartsWith("PLAYLIST:") || VideoId.StartsWith("ALBUM:") || VideoId.StartsWith("CHANNEL:") || VideoId == "LIKED_MUSIC")));
            }
        }

        public string SpeedDialTitle
        {
            get
            {
                if (HasChevron && !string.IsNullOrEmpty(Title) && !Title.EndsWith("›"))
                    return Title + " ›";
                return Title;
            }
        }

        public bool IsLikedMusic
        {
            get
            {
                return ItemType == "liked" || VideoId == "PLAYLIST:LM" || VideoId == "LIKED_MUSIC"
                    || (!string.IsNullOrEmpty(Title) && (Title == "Liked Music" || Title == "Nhạc đã thích"));
            }
        }

        public Visibility LikedCardVisibility
        {
            get { return IsLikedMusic ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility NormalThumbVisibility
        {
            get { return IsLikedMusic ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility ProgressVisibility
        {
            get { return (!IsLikedMusic && PlayProgressPercent > 0) ? Visibility.Visible : Visibility.Collapsed; }
        }

        private double _speedDialCardSize = 120;
        public double SpeedDialCardSize
        {
            get { return _speedDialCardSize > 0 ? _speedDialCardSize : 120; }
            set
            {
                if (_speedDialCardSize != value)
                {
                    _speedDialCardSize = value;
                    OnPropertyChanged("SpeedDialCardSize");
                    OnPropertyChanged("SpeedDialProgressWidth");
                }
            }
        }

        private Thickness _speedDialCardMargin = new Thickness(0, 0, 10, 10);
        public Thickness SpeedDialCardMargin
        {
            get { return _speedDialCardMargin; }
            set
            {
                _speedDialCardMargin = value;
                OnPropertyChanged("SpeedDialCardMargin");
            }
        }

        public double SpeedDialProgressWidth
        {
            get
            {
                double p = _playProgressPercent > 0 ? _playProgressPercent : 0.45;
                double maxW = (_speedDialCardSize > 20) ? (_speedDialCardSize - 14) : 106;
                return System.Math.Max(8, System.Math.Min(maxW, maxW * p));
            }
        }

        private string _commentCount;
        public string CommentCount
        {
            get { return _commentCount; }
            set
            {
                if (_commentCount != value)
                {
                    _commentCount = value;
                    OnPropertyChanged("CommentCount");
                }
            }
        }

        private string _topCommentAuthor;
        public string TopCommentAuthor
        {
            get { return _topCommentAuthor; }
            set
            {
                if (_topCommentAuthor != value)
                {
                    _topCommentAuthor = value;
                    OnPropertyChanged("TopCommentAuthor");
                }
            }
        }

        private string _topCommentText;
        public string TopCommentText
        {
            get { return _topCommentText; }
            set
            {
                if (_topCommentText != value)
                {
                    _topCommentText = value;
                    OnPropertyChanged("TopCommentText");
                    OnPropertyChanged("CommentVisibility");
                }
            }
        }
        public Visibility CommentVisibility
        {
            get { return !string.IsNullOrEmpty(_topCommentText) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}

