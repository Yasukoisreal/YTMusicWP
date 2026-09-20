using System;
using Windows.UI;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace YTMusicWP.Converters
{
    public class IsPlayingToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isPlaying = value is bool && (bool)value;
            if (isPlaying)
                return new SolidColorBrush(Color.FromArgb(255, 29, 185, 84));
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
