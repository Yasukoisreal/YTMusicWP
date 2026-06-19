using System;
using Windows.UI.Xaml.Data;

namespace YTMusicWP.Converters
{
    /// <summary>
    /// Converts YouTube thumbnail URLs to square-friendly versions.
    /// YTM thumbnails → 1:1 crop. YouTube thumbnails → mqdefault (no letterbox).
    /// </summary>
    public class SquareThumbnailConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string url = value as string;
            if (string.IsNullOrEmpty(url)) return url;

            // YouTube Music thumbnails — request square crop
            if (url.Contains("lh3.googleusercontent.com") || url.Contains("yt3.ggpht.com"))
            {
                int eqIdx = url.LastIndexOf("=");
                if (eqIdx > 0)
                    return url.Substring(0, eqIdx) + "=w226-h226-l90-rj";
                return url + "=w226-h226-l90-rj";
            }
            // YouTube video thumbnails — use mqdefault (true 16:9, no letterbox bars)
            if (url.Contains("hqdefault.jpg"))
                return url.Replace("hqdefault.jpg", "mqdefault.jpg");
            if (url.Contains("sddefault.jpg"))
                return url.Replace("sddefault.jpg", "mqdefault.jpg");
            return url;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
