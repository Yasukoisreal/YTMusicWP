using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Pure parsing and formatting utilities for video/audio duration strings.
    /// Supports standard colon formats ("3:45", "1:02:15"), ISO 8601 ("PT3M45S"), and numeric seconds.
    /// </summary>
    public static class TrackDurationParser
    {
        private static readonly Regex IsoDurationRegex = new Regex(
            @"^PT(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+(?:\.\d+)?)S)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Attempts to parse a duration string into total seconds.
        /// Returns true if successfully parsed, false otherwise.
        /// </summary>
        public static bool TryParseDuration(string durationStr, out double totalSeconds)
        {
            totalSeconds = 0;
            if (string.IsNullOrWhiteSpace(durationStr)) return false;

            durationStr = durationStr.Trim();

            // 1. Check for standard colon format ("3:45", "03:45", "1:02:15")
            if (durationStr.Contains(":"))
            {
                string[] parts = durationStr.Split(':');
                if (parts.Length == 2)
                {
                    double min, sec;
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out min) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
                    {
                        if (min >= 0 && sec >= 0 && sec < 60)
                        {
                            totalSeconds = (min * 60) + sec;
                            return true;
                        }
                    }
                }
                else if (parts.Length == 3)
                {
                    double hrs, min, sec;
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out hrs) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out min) &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
                    {
                        if (hrs >= 0 && min >= 0 && min < 60 && sec >= 0 && sec < 60)
                        {
                            totalSeconds = (hrs * 3600) + (min * 60) + sec;
                            return true;
                        }
                    }
                }
                return false;
            }

            // 2. Check for ISO 8601 format ("PT3M45S", "PT1H2M30S", "PT45S")
            if (durationStr.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
            {
                var match = IsoDurationRegex.Match(durationStr);
                if (match.Success)
                {
                    double hrs = 0, min = 0, sec = 0;
                    if (match.Groups["hours"].Success)
                        double.TryParse(match.Groups["hours"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out hrs);
                    if (match.Groups["minutes"].Success)
                        double.TryParse(match.Groups["minutes"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out min);
                    if (match.Groups["seconds"].Success)
                        double.TryParse(match.Groups["seconds"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out sec);

                    totalSeconds = (hrs * 3600) + (min * 60) + sec;
                    return true;
                }
                return false;
            }

            // 3. Check for plain numeric seconds ("225", "225.5")
            double rawSec;
            if (double.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out rawSec))
            {
                if (rawSec >= 0 && !double.IsNaN(rawSec) && !double.IsInfinity(rawSec))
                {
                    totalSeconds = rawSec;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Formats numeric seconds into human-readable text ("3:45", "1:02:15").
        /// </summary>
        public static string FormatDisplayTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            {
                return "0:00";
            }

            int totalSec = (int)Math.Floor(seconds);
            int hrs = totalSec / 3600;
            int min = (totalSec % 3600) / 60;
            int sec = totalSec % 60;

            if (hrs > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}:{2:D2}", hrs, min, sec);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}", min, sec);
        }
    }
}
