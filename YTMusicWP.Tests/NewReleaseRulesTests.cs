using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class NewReleaseRulesTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 29, 20, 0, 0);

        private static ReleaseInfo Release(string id, string subtitle, int daysAgoFound = 0)
        {
            return new ReleaseInfo { BrowseId = id, Title = id, Subtitle = subtitle, FoundAt = Now.AddDays(-daysAgoFound) };
        }

        [TestMethod]
        public void ReleaseYear_ReadsTheYearFromTheSubtitle()
        {
            Assert.AreEqual(2026, NewReleaseRules.ReleaseYear("Single • 2026"));
            Assert.AreEqual(2019, NewReleaseRules.ReleaseYear("Album • 1,2 N lượt xem • 2019"));
            Assert.AreEqual(0, NewReleaseRules.ReleaseYear("EP"));
            Assert.AreEqual(0, NewReleaseRules.ReleaseYear(null));
        }

        [TestMethod]
        public void FindNew_KeepsUnknownRecentReleasesOnly()
        {
            var current = new[]
            {
                Release("MPREb_known", "Album • 2026"),
                Release("MPREb_new", "Single • 2026"),
                Release("MPREb_lastyear", "EP • 2025"),
                Release("MPREb_old", "Album • 2015"),
                Release("VLPL123", "Playlist"),
                Release("MPREb_new", "Single • 2026")
            };
            var found = NewReleaseRules.FindNew(current, new HashSet<string> { "MPREb_known" }, 2026);

            CollectionAssert.AreEqual(new[] { "MPREb_new", "MPREb_lastyear" }, found.Select(r => r.BrowseId).ToArray());
        }

        [TestMethod]
        public void PickArtistsToCheck_NeverCheckedFirstThenOldest()
        {
            var lastChecked = new Dictionary<string, DateTime>
            {
                { "recent", Now.AddHours(-2) },
                { "old", Now.AddDays(-3) },
                { "older", Now.AddDays(-5) }
            };
            var picked = NewReleaseRules.PickArtistsToCheck(new[] { "recent", "old", "new", "older" }, lastChecked, Now, 2);

            CollectionAssert.AreEqual(new[] { "new", "older" }, picked.ToArray());
            Assert.IsFalse(NewReleaseRules.PickArtistsToCheck(new[] { "recent" }, lastChecked, Now, 5).Any(), "checked 2 hours ago is not due");
        }

        [TestMethod]
        public void Recent_DropsOldFindsAndDuplicates()
        {
            var found = new[]
            {
                Release("a", "", daysAgoFound: 40),
                Release("b", "", daysAgoFound: 2),
                Release("c", "", daysAgoFound: 1),
                Release("b", "", daysAgoFound: 10)
            };
            CollectionAssert.AreEqual(new[] { "c", "b" }, NewReleaseRules.Recent(found, Now).Select(r => r.BrowseId).ToArray());
        }
    }
}
