using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Models;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class SponsorBlockFilterTests
    {
        [TestMethod]
        public void ShouldSkip_PositionInsideSegment_ReturnsTrueAndEndTarget()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 15.0, End = 45.0, Category = "sponsor" }
            };

            double skipTarget;
            bool result = SponsorBlockFilter.ShouldSkip(20.0, segments, out skipTarget);

            Assert.IsTrue(result);
            Assert.AreEqual(45.0, skipTarget, 0.001);
        }

        [TestMethod]
        public void ShouldSkip_PositionBeforeSegment_ReturnsFalse()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 30.0, End = 60.0, Category = "sponsor" }
            };

            double skipTarget;
            bool result = SponsorBlockFilter.ShouldSkip(10.0, segments, out skipTarget);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldSkip_PositionAfterSegment_ReturnsFalse()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 30.0, End = 60.0, Category = "sponsor" }
            };

            double skipTarget;
            bool result = SponsorBlockFilter.ShouldSkip(70.0, segments, out skipTarget);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldSkip_PositionExactlyAtStart_ReturnsTrue()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 30.0, End = 60.0, Category = "sponsor" }
            };

            double skipTarget;
            bool result = SponsorBlockFilter.ShouldSkip(30.0, segments, out skipTarget);

            Assert.IsTrue(result);
            Assert.AreEqual(60.0, skipTarget, 0.001);
        }

        [TestMethod]
        public void ShouldSkip_PositionExactlyAtEnd_ReturnsFalse()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 30.0, End = 60.0, Category = "sponsor" }
            };

            double skipTarget;
            bool result = SponsorBlockFilter.ShouldSkip(60.0, segments, out skipTarget);

            Assert.IsFalse(result); // Already at the end, must not skip again
        }

        [TestMethod]
        public void ShouldSkip_MalformedOrNegativeSegments_ReturnsFalseSafely()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 50.0, End = 20.0 }, // End < Start
                new SponsorBlockSegment { Start = -10.0, End = 20.0 }, // Negative start
                new SponsorBlockSegment { Start = double.NaN, End = 30.0 },
                null
            };

            double skipTarget;
            bool result = SponsorBlockFilter.ShouldSkip(25.0, segments, out skipTarget);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void MergeSegments_OverlappingAndAdjacent_CombinesIntoSingleInterval()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 10.0, End = 25.0 },
                new SponsorBlockSegment { Start = 20.0, End = 40.0 }, // Overlaps
                new SponsorBlockSegment { Start = 40.0, End = 55.0 }  // Directly adjacent
            };

            var merged = SponsorBlockFilter.MergeSegments(segments);

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual(10.0, merged[0].Start, 0.001);
            Assert.AreEqual(55.0, merged[0].End, 0.001);
        }

        [TestMethod]
        public void MergeSegments_DisjointSegments_KeepsBothSeparated()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 10.0, End = 20.0 },
                new SponsorBlockSegment { Start = 50.0, End = 70.0 }
            };

            var merged = SponsorBlockFilter.MergeSegments(segments);

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(10.0, merged[0].Start, 0.001);
            Assert.AreEqual(20.0, merged[0].End, 0.001);
            Assert.AreEqual(50.0, merged[1].Start, 0.001);
            Assert.AreEqual(70.0, merged[1].End, 0.001);
        }

        [TestMethod]
        public void FilterByCategory_ReturnsOnlyMatchingAllowedCategories()
        {
            var segments = new List<SponsorBlockSegment>
            {
                new SponsorBlockSegment { Start = 10, End = 20, Category = "sponsor" },
                new SponsorBlockSegment { Start = 30, End = 40, Category = "music_offtopic" },
                new SponsorBlockSegment { Start = 50, End = 60, Category = "outro" }
            };

            var allowed = new HashSet<string> { "sponsor", "music_offtopic" };
            var filtered = SponsorBlockFilter.FilterByCategory(segments, allowed);

            Assert.AreEqual(2, filtered.Count);
            Assert.AreEqual("sponsor", filtered[0].Category);
            Assert.AreEqual("music_offtopic", filtered[1].Category);
        }
    }
}
