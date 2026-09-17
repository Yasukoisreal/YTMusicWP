using System;
using System.ComponentModel;
using Windows.Foundation;
using Windows.UI;
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
                    OnPropertyChanged("Title");
                    UpdateBackgroundBrush();
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
                    UpdateBackgroundBrush();
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
                }
            }
        }

        public bool HasThumbnail
        {
            get { return !string.IsNullOrEmpty(_thumbnailUrl); }
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
            Windows.UI.Color c1, c2;
            if (_stripeColor != 0)
            {
                byte a = (byte)((_stripeColor >> 24) & 0xFF);
                if (a == 0) a = 255;
                byte r = (byte)((_stripeColor >> 16) & 0xFF);
                byte g = (byte)((_stripeColor >> 8) & 0xFF);
                byte b = (byte)(_stripeColor & 0xFF);
                c1 = Windows.UI.Color.FromArgb(a, r, g, b);
                c2 = Windows.UI.Color.FromArgb(a, (byte)(r * 0.7), (byte)(g * 0.7), (byte)(b * 0.7));
            }
            else
            {
                int hash = string.IsNullOrEmpty(_title) ? 0 : _title.GetHashCode();
                float hue1 = ((hash & 0xFF) / 255.0f) * 360.0f;
                float hue2 = (((hash >> 8) & 0xFF) / 255.0f) * 360.0f;
                c1 = HsvToRgb(hue1, 0.75f, 0.85f);
                c2 = HsvToRgb(hue2, 0.75f, 0.65f);
            }

            var lgb = new LinearGradientBrush();
            lgb.StartPoint = new Point(0, 0);
            lgb.EndPoint = new Point(1, 1);
            lgb.GradientStops.Add(new GradientStop { Color = c1, Offset = 0.0 });
            lgb.GradientStops.Add(new GradientStop { Color = c2, Offset = 1.0 });
            BackgroundBrush = lgb;
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

            return Windows.UI.Color.FromArgb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
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
