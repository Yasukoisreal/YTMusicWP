using System;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Lightweight, zero-allocation LRC timestamp and lyric line parser in C# 6.0.
    /// Handles standard [mm:ss.xx] and [mm:ss.xxx] timestamps with graceful fallbacks.
    /// </summary>
    public static class LrcParser
    {
        public static bool TryParseLrcTime(string timeStr, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(timeStr)) return false;
            timeStr = timeStr.Trim();
            if (timeStr.Length == 0 || !char.IsDigit(timeStr[0])) return false;

            try
            {
                int colonIdx = timeStr.IndexOf(':');
                int dotIdx = timeStr.IndexOf('.');

                if (colonIdx > 0)
                {
                    int min;
                    if (!int.TryParse(timeStr.Substring(0, colonIdx), out min)) return false;
                    int sec = 0;
                    int ms = 0;

                    if (dotIdx > 0)
                    {
                        if (!int.TryParse(timeStr.Substring(colonIdx + 1, dotIdx - colonIdx - 1), out sec)) return false;
                        string msStr = timeStr.Substring(dotIdx + 1).PadRight(3, '0');
                        if (msStr.Length > 3) msStr = msStr.Substring(0, 3);
                        int.TryParse(msStr, out ms);
                    }
                    else
                    {
                        if (!int.TryParse(timeStr.Substring(colonIdx + 1), out sec)) return false;
                    }
                    result = new TimeSpan(0, 0, min, sec, ms);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static TimeSpan ParseLrcTime(string timeStr)
        {
            TimeSpan res;
            TryParseLrcTime(timeStr, out res);
            return res;
        }
    }
}
