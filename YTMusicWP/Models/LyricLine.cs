using System;
using System.ComponentModel;
using Windows.UI.Xaml.Media;

namespace YTMusicWP
{
    public class LyricLine : INotifyPropertyChanged
    {
        public TimeSpan Time { get; set; }

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
