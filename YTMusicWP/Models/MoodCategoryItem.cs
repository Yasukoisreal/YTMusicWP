using System;
using System.ComponentModel;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace YTMusicWP
{
    public class MoodCategoryItem : INotifyPropertyChanged
    {
        private string _title;
        private string _params;
        private string _browseId = "FEmusic_moods_and_genres_category";
        private string _color = "#333333";
        private uint _stripeColor;
        private string _thumbnailUrl;
        private string _sectionTitle;
        private Brush _backgroundBrush;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title
        {
            get { return _title; }
            set
            {
                if (_title != value)
                {
                    _title = value;
                    _backgroundBrush = null;
                    OnPropertyChanged("Title");
                    OnPropertyChanged("BackgroundBrush");
                }
            }
        }

        public string BrowseId
        {
            get { return _browseId; }
            set
            {
                if (_browseId != value)
                {
                    _browseId = value;
                    OnPropertyChanged("BrowseId");
                }
            }
        }

        public string Params
        {
            get { return _params; }
            set
            {
                if (_params != value)
                {
                    _params = value;
                    OnPropertyChanged("Params");
                }
            }
        }

        public string Color
        {
            get { return _color; }
            set
            {
                if (_color != value)
                {
                    _color = value;
                    OnPropertyChanged("Color");
                }
            }
        }

        public uint StripeColor
        {
            get { return _stripeColor; }
            set
            {
                if (_stripeColor != value)
                {
                    _stripeColor = value;
                    if (_stripeColor != 0)
                    {
                        Color = "#" + (_stripeColor & 0x00FFFFFF).ToString("X6");
                    }
                    OnPropertyChanged("StripeColor");
                }
            }
        }

        public string ThumbnailUrl
        {
            get { return _thumbnailUrl; }
            set
            {
                if (_thumbnailUrl != value)
                {
                    _thumbnailUrl = value;
                    OnPropertyChanged("ThumbnailUrl");
                    OnPropertyChanged("HasThumbnail");
                    OnPropertyChanged("ThumbnailVisibility");
                }
            }
        }

        public bool HasThumbnail
        {
            get { return !string.IsNullOrEmpty(_thumbnailUrl); }
        }

        public Visibility ThumbnailVisibility
        {
            get { return HasThumbnail ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string SectionTitle
        {
            get { return _sectionTitle; }
            set
            {
                if (_sectionTitle != value)
                {
                    _sectionTitle = value;
                    OnPropertyChanged("SectionTitle");
                }
            }
        }

        public Brush BackgroundBrush
        {
            get
            {
                if (_backgroundBrush == null)
                {
                    UpdateBackgroundBrush();
                }
                return _backgroundBrush;
            }
            set
            {
                if (_backgroundBrush != value)
                {
                    _backgroundBrush = value;
                    OnPropertyChanged("BackgroundBrush");
                }
            }
        }

        private void UpdateBackgroundBrush()
        {
            try
            {
                int hash = JavaStringHashCode(_title);
                float hue1 = ((hash & 0xFF) / 255.0f) * 360.0f;
                float hue2 = (((hash >> 8) & 0xFF) / 255.0f) * 360.0f;

                // Ensure distinct 2-color gradient
                float diff = Math.Abs(hue1 - hue2);
                if (diff < 35.0f || diff > 325.0f)
                {
                    hue2 = (hue2 + 45.0f) % 360.0f;
                }

                // Rich vibrant colors matching SimpMusic playlistTitleGradient
                var c1 = HsvToRgb(hue1, 0.72f, 0.90f);
                var c2 = HsvToRgb(hue2, 0.72f, 0.76f);

                var lgb = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                lgb.GradientStops.Add(new GradientStop { Color = c1, Offset = 0.0 });
                lgb.GradientStops.Add(new GradientStop { Color = c2, Offset = 1.0 });
                BackgroundBrush = lgb;
            }
            catch
            {
                try { BackgroundBrush = new SolidColorBrush(Windows.UI.Colors.DarkSlateGray); } catch { }
            }
        }

        private static int JavaStringHashCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int h = 0;
            for (int i = 0; i < s.Length; i++)
            {
                h = 31 * h + s[i];
            }
            return h;
        }

        private static Windows.UI.Color HsvToRgb(float hue, float saturation, float value)
        {
            float h = hue / 60.0f;
            float c = value * saturation;
            float x = c * (1.0f - Math.Abs((h % 2.0f) - 1.0f));
            float m = value - c;

            float r = 0, g = 0, b = 0;
            int hi = (int)h % 6;
            switch (hi)
            {
                case 0: r = c; g = x; b = 0; break;
                case 1: r = x; g = c; b = 0; break;
                case 2: r = 0; g = c; b = x; break;
                case 3: r = 0; g = x; b = c; break;
                case 4: r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }

            return Windows.UI.Color.FromArgb(255, 
                (byte)Math.Round((r + m) * 255), 
                (byte)Math.Round((g + m) * 255), 
                (byte)Math.Round((b + m) * 255));
        }

        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
