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

        public static bool IsWp81SupportedAudio(StreamFormatInfo fmt)
        {
            if (fmt == null || !IsValidStreamUrl(fmt.Url)) return false;

            // Reject WebM and Opus formats (not natively supported by WP8.1 MediaEngine)
            if (!string.IsNullOrEmpty(fmt.MimeType))
            {
                if (fmt.MimeType.IndexOf("webm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fmt.MimeType.IndexOf("opus", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            // Known audio-containing itags (141=256k AAC, 140=128k AAC, 139=48k AAC, 18=360p MP4 w/AAC, 22=720p MP4 w/AAC)
            if (fmt.Itag == 141 || fmt.Itag == 140 || fmt.Itag == 18 || fmt.Itag == 139 || fmt.Itag == 22)
                return true;

            if (!string.IsNullOrEmpty(fmt.MimeType))
            {
                if (fmt.MimeType.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (fmt.MimeType.IndexOf("video/mp4", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Filter out video-only itags without audio track
                    int itag = fmt.Itag;
                    if (itag == 133 || itag == 134 || itag == 135 || itag == 136 || itag == 137 || itag == 138 || itag == 160 || itag == 264 || itag == 298 || itag == 299)
                        return false;

                    return true;
                }
            }

            return false;
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
                    if (fmt != null && fmt.Itag == targetItag && IsWp81SupportedAudio(fmt))
                    {
                        return fmt;
                    }
                }
            }

            // Fallback: Pick any valid audio format compatible with WP8.1
            foreach (var fmt in formats)
            {
                if (IsWp81SupportedAudio(fmt))
                {
                    return fmt;
                }
            }

            return null;
        }
    }
}
