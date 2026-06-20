using System;
using System.ComponentModel;
using Windows.UI.Xaml.Media;

namespace YTMusicWP
{
    public class LyricLine : INotifyPropertyChanged
    {
        public TimeSpan Time { get; set; }

        private string _text;
        public string Text { get { return _text; } set { _text = value; OnPropertyChanged("Text"); } }

        // [OPT] Static shared brush — avoids creating 50-100 brush objects per song
        private static readonly SolidColorBrush _defaultLyricBrush = new SolidColorBrush(Windows.UI.Colors.Gray);

        private SolidColorBrush _colorBrush = _defaultLyricBrush;
        public SolidColorBrush ColorBrush { get { return _colorBrush; } set { _colorBrush = value; OnPropertyChanged("ColorBrush"); } }

        private double _fontSize = 22;
        public double FontSize { get { return _fontSize; } set { _fontSize = value; OnPropertyChanged("FontSize"); } }

        private double _opacity = 0.5;
        public double Opacity { get { return _opacity; } set { _opacity = value; OnPropertyChanged("Opacity"); } }

        private Windows.UI.Text.FontWeight _fontWeight = Windows.UI.Text.FontWeights.Normal;
        public Windows.UI.Text.FontWeight FontWeight { get { return _fontWeight; } set { _fontWeight = value; OnPropertyChanged("FontWeight"); } }

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
