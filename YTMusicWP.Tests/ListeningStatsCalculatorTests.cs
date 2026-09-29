using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class ListeningStatsCalculatorTests
    {
        private static PlayRecord Play(string start, int seconds, string videoId, string title, string artist)
        {
            return new PlayRecord { Start = DateTime.Parse(start), Seconds = seconds, VideoId = videoId, Title = title, Artist = artist };
        }

        [TestMethod]
        public void Parse_ReadsValidLinesAndSkipsBadOnes()
        {
            var plays = ListeningStatsCalculator.Parse(new[]
            {
                "2026-09-29 08:15:02\t215\tabc123\tSong A\tArtist A",
                "not a line",
                "2026-09-29 08:20:00\tforty\tdef456\tSong B\tArtist B",
                "2026-09-29 08:25:00\t0\tghi789\tSong C\tArtist C",
                "",
                "2026-09-29 09:00:00\t60\tjkl000\tSong D\tArtist D\r"
            });

            Assert.AreEqual(2, plays.Count);
            Assert.AreEqual(new DateTime(2026, 9, 29, 8, 15, 2), plays[0].Start);
            Assert.AreEqual(215, plays[0].Seconds);
            Assert.AreEqual("Song A", plays[0].Title);
            Assert.AreEqual("Artist D", plays[1].Artist);
        }

        [TestMethod]
        public void Summarize_CountsOnlyThePeriodAndRanksBySeconds()
        {
            var plays = new List<PlayRecord>
            {
                Play("2026-08-31 23:00", 600, "old", "Old", "Somebody"),
                Play("2026-09-01 10:00", 200, "a", "Song A", "Artist A"),
                Play("2026-09-02 10:00", 200, "a", "Song A", "Artist A"),
                Play("2026-09-03 10:00", 300, "b", "Song B", "Artist B - Topic"),
            };

            var summary = ListeningStatsCalculator.Summarize(plays, new DateTime(2026, 9, 1), DateTime.MaxValue);

            Assert.AreEqual(700, summary.Seconds);
            Assert.AreEqual(3, summary.Plays);
            Assert.AreEqual(2, summary.Songs);
            Assert.AreEqual("a", summary.TopSongs[0].VideoId);
            Assert.AreEqual(2, summary.TopSongs[0].Plays);
            Assert.AreEqual("Artist B", summary.TopSongs[1].Artist, "YouTube's \" - Topic\" suffix is dropped");
            Assert.AreEqual(new DateTime(2026, 9, 3), summary.BiggestDay);
            Assert.AreEqual(300, summary.BiggestDaySeconds);
        }

        [TestMethod]
        public void Summarize_CreditsEveryArtistOfACollaboration()
        {
            var plays = new List<PlayRecord>
            {
                Play("2026-09-01 10:00", 120, "x", "Stay", "The Kid LAROI & Justin Bieber"),
                Play("2026-09-01 11:00", 60, "y", "Peaches", "Justin Bieber feat. Daniel Caesar"),
            };

            var summary = ListeningStatsCalculator.Summarize(plays, DateTime.MinValue, DateTime.MaxValue);

            Assert.AreEqual(3, summary.Artists);
            Assert.AreEqual("Justin Bieber", summary.TopArtists[0].Artist);
            Assert.AreEqual(180, summary.TopArtists[0].Seconds);
        }

        [TestMethod]
        public void AddByHour_SplitsALongPlayAcrossHours()
        {
            var byHour = new int[24];
            ListeningStatsCalculator.AddByHour(byHour, new DateTime(2026, 9, 29, 22, 30, 0), 2 * 3600);

            Assert.AreEqual(1800, byHour[22]);
            Assert.AreEqual(3600, byHour[23]);
            Assert.AreEqual(1800, byHour[0], "the play runs past midnight");
        }

        [TestMethod]
        public void SplitArtists_KeepsASingleNameWhole()
        {
            CollectionAssert.AreEqual(new[] { "Lil Nas X" }, ListeningStatsCalculator.SplitArtists("Lil Nas X").ToArray());
            CollectionAssert.AreEqual(new[] { "A", "B" }, ListeningStatsCalculator.SplitArtists("A, B, A").ToArray());
        }

        [TestMethod]
        public void PeriodStart_WeekStartsOnMonday()
        {
            var sunday = new DateTime(2026, 10, 4, 18, 0, 0);
            Assert.AreEqual(new DateTime(2026, 9, 28), ListeningStatsCalculator.PeriodStart("week", sunday));
            Assert.AreEqual(new DateTime(2026, 10, 1), ListeningStatsCalculator.PeriodStart("month", sunday));
            Assert.AreEqual(new DateTime(2026, 1, 1), ListeningStatsCalculator.PeriodStart("year", sunday));
            Assert.AreEqual(DateTime.MinValue, ListeningStatsCalculator.PeriodStart("all", sunday));
        }
    }
}
