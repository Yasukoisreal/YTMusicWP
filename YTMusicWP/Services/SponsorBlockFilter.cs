using System;
using System.Collections.Generic;
using System.Linq;
using YTMusicWP.Models;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Pure algorithmic filter for evaluating and merging SponsorBlock segment intervals.
    /// </summary>
    internal static class SponsorBlockFilter
    {
        /// <summary>
        /// Checks if current playback position falls inside any active sponsor segment.
        /// If so, provides the target second to seek/skip to.
        /// </summary>
        public static bool ShouldSkip(double currentPosition, IEnumerable<SponsorBlockSegment> segments, out double skipTarget)
        {
            skipTarget = 0;
            if (segments == null || double.IsNaN(currentPosition) || double.IsInfinity(currentPosition) || currentPosition < 0)
            {
                return false;
            }

            foreach (var seg in segments)
            {
                if (seg == null) continue;
                if (double.IsNaN(seg.Start) || double.IsNaN(seg.End) || seg.End <= seg.Start || seg.Start < 0)
                {
                    continue;
                }

                // If currently inside the segment:
                // We use [Start, End) - if already at or beyond End, do not skip.
                if (currentPosition >= seg.Start && currentPosition < seg.End)
                {
                    skipTarget = seg.End;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Merges overlapping or adjacent segments to prevent double-skipping jitter.
        /// </summary>
        public static List<SponsorBlockSegment> MergeSegments(IEnumerable<SponsorBlockSegment> segments)
        {
            var result = new List<SponsorBlockSegment>();
            if (segments == null) return result;

            // Filter valid segments and sort by Start time ascending
            var valid = segments
                .Where(s => s != null && !double.IsNaN(s.Start) && !double.IsNaN(s.End) && s.End > s.Start && s.Start >= 0)
                .OrderBy(s => s.Start)
                .ToList();

            if (valid.Count == 0) return result;

            SponsorBlockSegment current = new SponsorBlockSegment
            {
                Start = valid[0].Start,
                End = valid[0].End,
                Category = valid[0].Category,
                ActionType = valid[0].ActionType
            };

            for (int i = 1; i < valid.Count; i++)
            {
                var next = valid[i];
                if (next.Start <= current.End)
                {
                    // Overlapping or touching: expand current.End
                    if (next.End > current.End)
                    {
                        current.End = next.End;
                    }
                }
                else
                {
                    result.Add(current);
                    current = new SponsorBlockSegment
                    {
                        Start = next.Start,
                        End = next.End,
                        Category = next.Category,
                        ActionType = next.ActionType
                    };
                }
            }
            result.Add(current);

            return result;
        }

        /// <summary>
        /// Filters segments to only those matching permitted category names.
        /// </summary>
        public static List<SponsorBlockSegment> FilterByCategory(IEnumerable<SponsorBlockSegment> segments, HashSet<string> allowedCategories)
        {
            var result = new List<SponsorBlockSegment>();
            if (segments == null || allowedCategories == null || allowedCategories.Count == 0)
            {
                return result;
            }

            foreach (var seg in segments)
            {
                if (seg != null && !string.IsNullOrEmpty(seg.Category) && allowedCategories.Contains(seg.Category.ToLowerInvariant()))
                {
                    result.Add(seg);
                }
            }

            return result;
        }
    }
}
