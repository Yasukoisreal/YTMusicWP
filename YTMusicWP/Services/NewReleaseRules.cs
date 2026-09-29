using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace YTMusicWP.Services
{
    /// <summary>An album or single on a followed artist's page.</summary>
    public sealed class ReleaseInfo
    {
        public string BrowseId;
        public string Title;
        public string Artist;
        public string ChannelId;
        public string ThumbnailUrl;
        public string Subtitle;
        /// <summary>When the app first saw it (YouTube Music gives only the year of a release).</summary>
        public DateTime FoundAt;
    }

    /// <summary>
    /// Which followed artists to check and which of their releases are new. Pure logic (no WinRT) so the unit tests can
    /// run it; NewReleasesTracker does the network and storage.
    /// </summary>
    public static class NewReleaseRules
    {
        /// <summary>An artist is looked at again at most twice a day.</summary>
        public static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(12);

        /// <summary>How long a new release stays in the Home section.</summary>
        public static readonly TimeSpan ShowFor = TimeSpan.FromDays(30);

        private static readonly Regex Year = new Regex(@"\b(19|20)\d{2}\b");

        /// <summary>Albums, singles and EPs have MPREb_ browse ids (playlists, videos and related artists do not).</summary>
        public static bool IsRelease(string browseId)
        {
            return browseId != null && browseId.StartsWith("MPREb_", StringComparison.Ordinal);
        }

        /// <summary>The release year from a subtitle like "Single • 2026", 0 when there is none.</summary>
        public static int ReleaseYear(string subtitle)
        {
            if (string.IsNullOrEmpty(subtitle)) return 0;
            var matches = Year.Matches(subtitle);
            return matches.Count > 0 ? int.Parse(matches[matches.Count - 1].Value) : 0;
        }

        /// <summary>
        /// Up to <paramref name="max"/> artists due for a check: never checked first, then the longest unchecked, skipping
        /// those checked within <see cref="RecheckAfter"/>.
        /// </summary>
        public static List<string> PickArtistsToCheck(IEnumerable<string> channelIds, IDictionary<string, DateTime> lastChecked, DateTime now, int max)
        {
            return channelIds
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .Select(id =>
                {
                    DateTime checkedAt;
                    return new { Id = id, Checked = lastChecked.TryGetValue(id, out checkedAt) ? checkedAt : DateTime.MinValue };
                })
                .Where(a => now - a.Checked >= RecheckAfter)
                .OrderBy(a => a.Checked)
                .Take(max)
                .Select(a => a.Id)
                .ToList();
        }

        /// <summary>
        /// Releases not seen before. Only this year's and last year's count: an artist page can surface an old album for
        /// the first time (a reissue, a reordered shelf) and that is not news.
        /// </summary>
        public static List<ReleaseInfo> FindNew(IEnumerable<ReleaseInfo> current, ICollection<string> known, int currentYear)
        {
            return current
                .Where(r => IsRelease(r.BrowseId) && !known.Contains(r.BrowseId))
                .Where(r =>
                {
                    int year = ReleaseYear(r.Subtitle);
                    return year == 0 || year >= currentYear - 1;
                })
                .GroupBy(r => r.BrowseId)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>The releases to show: found within <see cref="ShowFor"/>, newest first, each once.</summary>
        public static List<ReleaseInfo> Recent(IEnumerable<ReleaseInfo> found, DateTime now)
        {
            return found
                .Where(r => now - r.FoundAt <= ShowFor)
                .OrderByDescending(r => r.FoundAt)
                .GroupBy(r => r.BrowseId)
                .Select(g => g.First())
                .ToList();
        }
    }
}
