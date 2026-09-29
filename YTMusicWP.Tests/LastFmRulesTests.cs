using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class LastFmRulesTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 29, 20, 0, 0);

        private static PlayRecord Play(int seconds, int duration, int daysAgo = 0)
        {
            return new PlayRecord { Start = Now.AddDays(-daysAgo), Seconds = seconds, Duration = duration, Title = "T", Artist = "A", VideoId = "v" };
        }

        [TestMethod]
        public void IsScrobblable_NeedsHalfTheTrackOrFourMinutes()
        {
            Assert.IsTrue(LastFmRules.IsScrobblable(Play(100, 200), Now), "half of a 200 s track");
            Assert.IsFalse(LastFmRules.IsScrobblable(Play(99, 200), Now));
            Assert.IsTrue(LastFmRules.IsScrobblable(Play(240, 1200), Now), "4 minutes of a 20-minute track");
            Assert.IsFalse(LastFmRules.IsScrobblable(Play(239, 1200), Now));
        }

        [TestMethod]
        public void IsScrobblable_SkipsShortUnknownAndOldPlays()
        {
            Assert.IsFalse(LastFmRules.IsScrobblable(Play(30, 30), Now), "tracks of 30 s or less");
            Assert.IsFalse(LastFmRules.IsScrobblable(Play(3600, 0), Now), "unknown length: livestreams");
            Assert.IsFalse(LastFmRules.IsScrobblable(Play(200, 200, daysAgo: 15), Now), "Last.fm refuses scrobbles older than 14 days");
        }

        [TestMethod]
        public void IsScrobblable_NeedsArtistAndTitle()
        {
            var noTitle = Play(200, 200);
            noTitle.Title = "(Official Video)";
            Assert.IsFalse(LastFmRules.IsScrobblable(noTitle, Now), "nothing left after cleaning the title");

            var untagged = Play(200, 200);
            untagged.Artist = "";
            Assert.IsFalse(LastFmRules.IsScrobblable(untagged, Now), "a local file without tags");
        }

        [TestMethod]
        public void SignatureBase_SortsParametersAndLeavesOutFormat()
        {
            var p = new Dictionary<string, string>
            {
                { "method", "auth.getSession" },
                { "api_key", "KEY" },
                { "token", "TOK" },
                { "format", "json" }
            };
            Assert.AreEqual("api_keyKEYmethodauth.getSessiontokenTOKsecret", LastFmRules.SignatureBase(p, "secret"));
        }

        [TestMethod]
        public void SignatureBase_SortsArrayParametersByName()
        {
            var p = new Dictionary<string, string> { { "track[0]", "b" }, { "artist[0]", "a" } };
            Assert.AreEqual("artist[0]atrack[0]bS", LastFmRules.SignatureBase(p, "S"));
        }

        [TestMethod]
        public void CleanTitleAndArtist_RemoveYouTubeNoise()
        {
            Assert.AreEqual("Rick Astley", LastFmRules.CleanArtist("Rick Astley - Topic"));
            Assert.AreEqual("Rick Astley", LastFmRules.CleanArtist("Rick AstleyVEVO"));
            Assert.AreEqual("Rick Astley - Never Gonna Give You Up", LastFmRules.CleanTitle("Rick Astley - Never Gonna Give You Up (Official Music Video)", "RickAstleyVEVO"),
                "only an exact artist prefix is removed: \"RickAstley\" is not \"Rick Astley\"");
            Assert.AreEqual("Never Gonna Give You Up", LastFmRules.CleanTitle("Rick Astley - Never Gonna Give You Up (Official Video)", "Rick Astley"));
            Assert.AreEqual("Stay (with Justin Bieber)", LastFmRules.CleanTitle("Stay (with Justin Bieber)", "The Kid LAROI"));
        }

        [TestMethod]
        public void UnixTime_IsSecondsSinceEpochUtc()
        {
            var local = new DateTime(1970, 1, 1, 0, 0, 10, DateTimeKind.Utc).ToLocalTime();
            Assert.AreEqual(10, LastFmRules.UnixTime(local));
        }
    }
}
