using System;
using System.Collections.Generic;

namespace YTMusicWP.Services
{
    public enum AudioQualityPreference
    {
        Auto = 0,
        Low = 1,      // 48kbps (itag 139) -> 140 -> 18
        Medium = 2,   // 128kbps (itag 140) -> 18 -> 141 -> 139
        High = 3      // 256kbps (itag 141) -> 140 -> 18 -> 139
    }

    public class StreamFormatInfo
    {
        public int Itag { get; set; }
        public string Url { get; set; }
        public string MimeType { get; set; }
        public int Bitrate { get; set; }

        public StreamFormatInfo() { }

        public StreamFormatInfo(int itag, string url, string mimeType = null, int bitrate = 0)
        {
            Itag = itag;
            Url = url;
            MimeType = mimeType;
            Bitrate = bitrate;
        }
    }

    /// <summary>
    /// Pure algorithmic selector for YouTube audio stream formats based on user preference,
    /// format priorities, and network stream safety.
    /// </summary>
    public static class FormatSelector
    {
        private static readonly int[] HighPriorityItags = { 141, 140, 18, 139 };
        private static readonly int[] MediumPriorityItags = { 140, 18, 141, 139 };
        private static readonly int[] LowPriorityItags = { 139, 140, 18, 141 };

        public static int[] GetPreferredItagOrder(AudioQualityPreference preference)
        {
            switch (preference)
            {
                case AudioQualityPreference.High:
                    return HighPriorityItags;
                case AudioQualityPreference.Low:
                    return LowPriorityItags;
                case AudioQualityPreference.Medium:
                case AudioQualityPreference.Auto:
                default:
                    return MediumPriorityItags;
            }
        }

        public static bool IsLiveStreamUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf("live=1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("live/1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsValidStreamUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (IsLiveStreamUrl(url)) return false;
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static StreamFormatInfo SelectBestFormat(IEnumerable<StreamFormatInfo> formats, AudioQualityPreference preference = AudioQualityPreference.Auto)
        {
            if (formats == null) return null;

            int[] order = GetPreferredItagOrder(preference);

            // Fast indexed lookup for preferred itags
            foreach (int targetItag in order)
            {
                foreach (var fmt in formats)
                {
                    if (fmt != null && fmt.Itag == targetItag && IsValidStreamUrl(fmt.Url))
                    {
                        return fmt;
                    }
                }
            }

            // Fallback: If none of preferred itags matched, pick any valid audio format
            StreamFormatInfo fallback = null;
            foreach (var fmt in formats)
            {
                if (fmt != null && IsValidStreamUrl(fmt.Url))
                {
                    if (fmt.MimeType != null && fmt.MimeType.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return fmt;
                    }
                    if (fallback == null) fallback = fmt;
                }
            }

            return fallback;
        }
    }
}
