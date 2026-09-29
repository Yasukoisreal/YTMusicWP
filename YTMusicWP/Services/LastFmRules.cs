using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Last.fm scrobbling rules and request signing. Pure logic (no WinRT) so the unit tests can run it; the network
    /// side is LastFmClient.
    /// </summary>
    public static class LastFmRules
    {
        /// <summary>Last.fm refuses scrobbles older than two weeks.</summary>
        public static readonly TimeSpan MaxScrobbleAge = TimeSpan.FromDays(14);

        /// <summary>
        /// Last.fm's rule: the track is longer than 30 s and was played for half its length or 4 minutes. It also needs an
        /// artist and a title: Last.fm refuses the whole batch when one play lacks them (a local file without tags), so
        /// such a play would block every later scrobble.
        /// </summary>
        public static bool IsScrobblable(PlayRecord play, DateTime now)
        {
            if (play == null || play.Duration <= 30) return false; // unknown length (livestreams) or too short
            if (now - play.Start > MaxScrobbleAge) return false;
            if (CleanArtist(play.Artist).Length == 0 || CleanTitle(play.Title, play.Artist).Length == 0) return false;
            return play.Seconds >= Math.Min(play.Duration / 2.0, 240);
        }

        /// <summary>
        /// The text whose MD5 is api_sig: every parameter except format and callback, sorted by name, as name+value,
        /// then the shared secret (UTF-8).
        /// </summary>
        public static string SignatureBase(IDictionary<string, string> parameters, string secret)
        {
            var sb = new StringBuilder();
            foreach (var p in parameters.Where(p => p.Key != "format" && p.Key != "callback").OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sb.Append(p.Key).Append(p.Value);
            }
            sb.Append(secret);
            return sb.ToString();
        }

        /// <summary>Seconds since 1970-01-01 UTC for a local time.</summary>
        public static long UnixTime(DateTime local)
        {
            return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static readonly Regex VideoSuffix = new Regex(
            @"\s*[\(\[](official\s*(music\s*)?(video|audio|mv|visualizer|visualiser|lyric\s*video)|lyric\s*video|lyrics|audio|visualizer|mv|hd|4k)[\)\]]",
            RegexOptions.IgnoreCase);

        /// <summary>The artist as Last.fm knows it: YouTube's " - Topic" and "VEVO" channel suffixes removed.</summary>
        public static string CleanArtist(string artist)
        {
            string a = (artist ?? "").Trim();
            if (a.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)) a = a.Substring(0, a.Length - 8).Trim();
            if (a.Length > 4 && a.EndsWith("VEVO", StringComparison.Ordinal)) a = a.Substring(0, a.Length - 4).Trim();
            return a;
        }

        /// <summary>
        /// The song title as Last.fm knows it: video labels like "(Official Video)" removed, and a leading "Artist - "
        /// (common in video titles) removed when it repeats the artist.
        /// </summary>
        public static string CleanTitle(string title, string artist)
        {
            string t = VideoSuffix.Replace(title ?? "", "").Trim();
            string a = CleanArtist(artist);
            if (a.Length > 0 && t.StartsWith(a + " - ", StringComparison.OrdinalIgnoreCase)) t = t.Substring(a.Length + 3).Trim();
            return t;
        }
    }
}
