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
            // hqdefault.jpg has black bars; mqdefault.jpg is clean 320x180
            if (url.Contains("/hqdefault.jpg"))
                return url.Replace("/hqdefault.jpg", "/mqdefault.jpg");
            if (url.Contains("/sddefault.jpg"))
                return url.Replace("/sddefault.jpg", "/mqdefault.jpg");
            if (url.Contains("/maxresdefault.jpg"))
                return url.Replace("/maxresdefault.jpg", "/mqdefault.jpg");
            if (url.Contains("/default.jpg"))
                return url.Replace("/default.jpg", "/mqdefault.jpg");
            // Handle vi_webp variants
            if (url.Contains("/hqdefault.webp"))
                return url.Replace("/hqdefault.webp", "/mqdefault.webp");
            if (url.Contains("/sddefault.webp"))
                return url.Replace("/sddefault.webp", "/mqdefault.webp");
            return url;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
