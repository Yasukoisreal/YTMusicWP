using System;
using System.ComponentModel;
using Windows.UI.Xaml.Media;

namespace YTMusicWP
{
    public class LyricLine : INotifyPropertyChanged
    {
        public TimeSpan Time { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsInterlude { get; set; }
        public bool IsBackground { get; set; }

        /// <summary>
        /// Sung by a second singer (Apple Music TTML ttm:agent): drawn right-aligned, like Apple Music duets.
        /// Background vocals split out of a line keep the side of the line they belong to.
        /// </summary>
        public bool IsOppositeSide { get; set; }
        public Windows.UI.Xaml.TextAlignment TextAlign
        {
            get { return IsOppositeSide ? Windows.UI.Xaml.TextAlignment.Right : Windows.UI.Xaml.TextAlignment.Left; }
        }
        public Windows.UI.Xaml.HorizontalAlignment HAlign
        {
            get { return IsOppositeSide ? Windows.UI.Xaml.HorizontalAlignment.Right : Windows.UI.Xaml.HorizontalAlignment.Left; }
        }

        /// <summary>When the line stops being sung, when known (TTML end / last word); TimeSpan.Zero for plain LRC lines.</summary>
        public TimeSpan SungEnd
        {
            get
            {
                TimeSpan end = EndTime > Time ? EndTime : TimeSpan.Zero;
                if (Words != null)
                {
                    for (int i = 0; i < Words.Count; i++)
                    {
                        if (Words[i].EndTime > end) end = Words[i].EndTime;
                    }
                }
                return end > Time ? end : TimeSpan.Zero;
            }
        }

        public System.Collections.Generic.List<LyricWord> Words { get; set; }
        public bool HasWords { get { return Words != null && Words.Count > 0; } }

        private string _text;
        public string Text { get { return _text; } set { if (_text != value) { _text = value; OnPropertyChanged("Text"); } } }

        // [OPT] Static shared brush — avoids creating 50-100 brush objects per song
        private static readonly SolidColorBrush _defaultLyricBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 179, 179, 179));

        private SolidColorBrush _colorBrush = _defaultLyricBrush;
        public SolidColorBrush ColorBrush { get { return _colorBrush; } set { if (!object.ReferenceEquals(_colorBrush, value)) { _colorBrush = value; OnPropertyChanged("ColorBrush"); } } }

        private double _fontSize = 22;
        public double FontSize { get { return _fontSize; } set { if (Math.Abs(_fontSize - value) > 0.001) { _fontSize = value; OnPropertyChanged("FontSize"); } } }

        private double _opacity = 0.5;
        public double Opacity { get { return _opacity; } set { if (Math.Abs(_opacity - value) > 0.001) { _opacity = value; OnPropertyChanged("Opacity"); } } }

        private double _blurOpacity = 0.0;
        public double BlurOpacity { get { return _blurOpacity; } set { if (Math.Abs(_blurOpacity - value) > 0.001) { _blurOpacity = value; OnPropertyChanged("BlurOpacity"); } } }

        private double _farBlurOpacity = 0.0;
        public double FarBlurOpacity { get { return _farBlurOpacity; } set { if (Math.Abs(_farBlurOpacity - value) > 0.001) { _farBlurOpacity = value; OnPropertyChanged("FarBlurOpacity"); } } }

        private Windows.UI.Text.FontWeight _fontWeight = Windows.UI.Text.FontWeights.Normal;
        public Windows.UI.Text.FontWeight FontWeight { get { return _fontWeight; } set { if (_fontWeight.Weight != value.Weight) { _fontWeight = value; OnPropertyChanged("FontWeight"); } } }

        private Windows.UI.Text.FontStyle _fontStyle = Windows.UI.Text.FontStyle.Normal;
        public Windows.UI.Text.FontStyle FontStyle { get { return _fontStyle; } set { if (_fontStyle != value) { _fontStyle = value; OnPropertyChanged("FontStyle"); } } }

        // Dòng dấu chấm dạo nhạc chỉ hiện khi đang tới lượt nó; ngoài ra thu gọn hẳn (không chiếm chỗ)
        private Windows.UI.Xaml.Visibility _lineVisibility = Windows.UI.Xaml.Visibility.Visible;
        public Windows.UI.Xaml.Visibility LineVisibility { get { return _lineVisibility; } set { if (_lineVisibility != value) { _lineVisibility = value; OnPropertyChanged("LineVisibility"); } } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
