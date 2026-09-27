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

        /// <summary>
        /// Parses Enhanced LRC word timestamps like "<00:12.34> word1 <00:13.00> word2"
        /// into individual LyricWord elements and plain line text.
        /// Returns true if at least one word timestamp was parsed.
        /// </summary>
        public static bool TryParseEnhancedWords(string text, TimeSpan lineStartTime, out string plainText, out System.Collections.Generic.List<LyricWord> words)
        {
            plainText = text;
            words = null;
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf('<') < 0 || text.IndexOf('>') < 0)
            {
                return false;
            }

            var list = new System.Collections.Generic.List<LyricWord>();
            int idx = 0;
            int len = text.Length;

            while (idx < len)
            {
                int open = text.IndexOf('<', idx);
                if (open < 0) break;

                int close = text.IndexOf('>', open);
                if (close < 0) break;

                string timeStr = text.Substring(open + 1, close - open - 1);
                TimeSpan wTime;
                if (!TryParseLrcTime(timeStr, out wTime))
                {
                    idx = close + 1;
                    continue;
                }

                // Word text is between this '>' and next '<' (or end of line)
                int nextOpen = text.IndexOf('<', close + 1);
                string wText = (nextOpen >= 0) ? text.Substring(close + 1, nextOpen - close - 1) : text.Substring(close + 1);

                if (list.Count == 0)
                {
                    wText = wText.TrimStart();
                }
                else if (list[list.Count - 1].Text.EndsWith(" ") && wText.StartsWith(" "))
                {
                    wText = wText.TrimStart();
                }

                list.Add(new LyricWord
                {
                    Text = wText,
                    StartTime = wTime,
                    EndTime = TimeSpan.Zero
                });

                idx = (nextOpen >= 0) ? nextOpen : len;
            }

            if (list.Count == 0) return false;

            // Set word end boundaries
            for (int i = 0; i < list.Count; i++)
            {
                if (i < list.Count - 1)
                {
                    list[i].EndTime = list[i + 1].StartTime;
                }
                else
                {
                    list[i].EndTime = list[i].StartTime + TimeSpan.FromMilliseconds(500);
                }
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append(list[i].Text);
            }
            plainText = sb.ToString().Trim();
            words = list;
            return true;
        }
    }
}
