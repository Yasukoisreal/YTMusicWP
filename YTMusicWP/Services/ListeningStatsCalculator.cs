using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace YTMusicWP.Services
{
    /// <summary>One play from the audio task's listening log (AudioPlayerTask.ListeningLog).</summary>
    public sealed class PlayRecord
    {
        public DateTime Start;
        public int Seconds;
        public string VideoId;
        public string Title;
        public string Artist;
        /// <summary>The track's length in seconds, 0 when unknown (a livestream, or a line written before it was logged).</summary>
        public int Duration;
        /// <summary>Line number in the log (0-based), used by Last.fm to remember what it already scrobbled.</summary>
        public int Line;
    }

    /// <summary>A song or an artist in a top list.</summary>
    public sealed class StatsEntry
    {
        public string VideoId;
        public string Title;
        public string Artist;
        public int Seconds;
        public int Plays;
    }

    public sealed class ListeningSummary
    {
        public int Seconds;
        public int Plays;
        public int Songs;
        public int Artists;
        public List<StatsEntry> TopSongs = new List<StatsEntry>();
        public List<StatsEntry> TopArtists = new List<StatsEntry>();
        /// <summary>Seconds listened in each hour of the day (0-23), a play that crosses an hour split between both.</summary>
        public int[] SecondsByHour = new int[24];
        public DateTime? BiggestDay;
        public int BiggestDaySeconds;
    }

    /// <summary>
    /// Listening stats from the play log. Pure logic (no WinRT) so the unit tests can run it.
    /// </summary>
    public static class ListeningStatsCalculator
    {
        /// <summary>The play log in LocalFolder, written by AudioPlayerTask.ListeningLog.</summary>
        public const string LogFileName = "listening_history.txt";

        /// <summary>
        /// Parses log lines ("yyyy-MM-dd HH:mm:ss \t seconds \t videoId \t title \t artist [\t duration]"); bad lines are
        /// skipped.
        /// </summary>
        public static List<PlayRecord> Parse(IEnumerable<string> lines)
        {
            var plays = new List<PlayRecord>();
            if (lines == null) return plays;
            int lineNumber = -1;
            foreach (var line in lines)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.TrimEnd('\r', '\n').Split('\t');
                if (parts.Length < 5) continue;
                DateTime start;
                int seconds;
                if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) || seconds <= 0) continue;
                if (string.IsNullOrEmpty(parts[2])) continue;
                int duration = 0;
                if (parts.Length > 5) int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out duration);
                plays.Add(new PlayRecord { Start = start, Seconds = seconds, VideoId = parts[2], Title = parts[3], Artist = parts[4], Duration = duration, Line = lineNumber });
            }
            return plays;
        }

        /// <summary>Stats for the plays that started in [from, to).</summary>
        public static ListeningSummary Summarize(IEnumerable<PlayRecord> plays, DateTime from, DateTime to, int top = 5)
        {
            var summary = new ListeningSummary();
            var songs = new Dictionary<string, StatsEntry>();
            var artists = new Dictionary<string, StatsEntry>(StringComparer.OrdinalIgnoreCase);
            var days = new Dictionary<DateTime, int>();

            foreach (var play in plays)
            {
                if (play.Start < from || play.Start >= to) continue;
                summary.Seconds += play.Seconds;
                summary.Plays++;

                StatsEntry song;
                if (!songs.TryGetValue(play.VideoId, out song))
                {
                    song = new StatsEntry { VideoId = play.VideoId, Title = play.Title, Artist = CleanArtist(play.Artist) };
                    songs[play.VideoId] = song;
                }
                song.Seconds += play.Seconds;
                song.Plays++;

                foreach (var name in SplitArtists(play.Artist))
                {
                    StatsEntry artist;
                    if (!artists.TryGetValue(name, out artist))
                    {
                        artist = new StatsEntry { Artist = name, Title = name, VideoId = play.VideoId };
                        artists[name] = artist;
                    }
                    artist.Seconds += play.Seconds;
                    artist.Plays++;
                }

                int daySeconds;
                days.TryGetValue(play.Start.Date, out daySeconds);
                days[play.Start.Date] = daySeconds + play.Seconds;

                AddByHour(summary.SecondsByHour, play.Start, play.Seconds);
            }

            summary.Songs = songs.Count;
            summary.Artists = artists.Count;
            summary.TopSongs = songs.Values.OrderByDescending(s => s.Seconds).ThenByDescending(s => s.Plays).Take(top).ToList();
            summary.TopArtists = artists.Values.OrderByDescending(a => a.Seconds).ThenByDescending(a => a.Plays).Take(top).ToList();
            foreach (var day in days)
            {
                if (day.Value > summary.BiggestDaySeconds)
                {
                    summary.BiggestDaySeconds = day.Value;
                    summary.BiggestDay = day.Key;
                }
            }
            return summary;
        }

        /// <summary>Spreads a play over the hours of the day it covers (a 3-hour livestream fills three bars, not one).</summary>
        public static void AddByHour(int[] secondsByHour, DateTime start, int seconds)
        {
            var at = start;
            int left = seconds;
            while (left > 0)
            {
                var nextHour = at.Date.AddHours(at.Hour + 1);
                int inThisHour = (int)Math.Min(left, Math.Ceiling((nextHour - at).TotalSeconds));
                if (inThisHour <= 0) inThisHour = 1;
                secondsByHour[at.Hour] += inThisHour;
                left -= inThisHour;
                at = nextHour;
            }
        }

        /// <summary>The artists of a track ("A & B", "A, B", "A feat. B" credit both); YouTube's " - Topic" suffix removed.</summary>
        public static List<string> SplitArtists(string artist)
        {
            var names = new List<string>();
            string cleaned = CleanArtist(artist);
            if (string.IsNullOrEmpty(cleaned)) return names;
            foreach (var part in cleaned.Split(new[] { ", ", " & ", " feat. ", " ft. ", " x ", " X " }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                if (name.Length > 0 && !names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) names.Add(name);
            }
            return names;
        }

        private static string CleanArtist(string artist)
        {
            if (string.IsNullOrWhiteSpace(artist)) return "";
            string cleaned = artist.Trim();
            if (cleaned.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned.Substring(0, cleaned.Length - 8).Trim();
            return cleaned;
        }

        /// <summary>Start of the period containing <paramref name="now"/>: "week" (Monday), "month", "year", anything else = all time.</summary>
        public static DateTime PeriodStart(string period, DateTime now)
        {
            switch (period)
            {
                case "week":
                    int sinceMonday = ((int)now.DayOfWeek + 6) % 7;
                    return now.Date.AddDays(-sinceMonday);
                case "month":
                    return new DateTime(now.Year, now.Month, 1);
                case "year":
                    return new DateTime(now.Year, 1, 1);
                default:
                    return DateTime.MinValue;
            }
        }
    }
}
