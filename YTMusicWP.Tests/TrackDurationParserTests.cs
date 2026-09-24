using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class TrackDurationParserTests
    {
        [TestMethod]
        public void TryParseDuration_StandardMinutesAndSeconds_ParsesAccurately()
        {
            double sec;
            bool ok = TrackDurationParser.TryParseDuration("3:45", out sec);

            Assert.IsTrue(ok);
            Assert.AreEqual(225.0, sec, 0.001);
        }

        [TestMethod]
        public void TryParseDuration_TwoDigitPaddedMinutes_ParsesAccurately()
        {
            double sec;
            bool ok = TrackDurationParser.TryParseDuration("03:45", out sec);

            Assert.IsTrue(ok);
            Assert.AreEqual(225.0, sec, 0.001);
        }

        [TestMethod]
        public void TryParseDuration_HoursMinutesAndSeconds_ParsesAccurately()
        {
            double sec;
            bool ok = TrackDurationParser.TryParseDuration("1:02:15", out sec);

            Assert.IsTrue(ok);
            Assert.AreEqual(3735.0, sec, 0.001);
        }

        [TestMethod]
        public void TryParseDuration_Iso8601Standard_ParsesAccurately()
        {
            double sec;
            bool ok = TrackDurationParser.TryParseDuration("PT3M45S", out sec);

            Assert.IsTrue(ok);
            Assert.AreEqual(225.0, sec, 0.001);

            ok = TrackDurationParser.TryParseDuration("PT1H2M30S", out sec);
            Assert.IsTrue(ok);
            Assert.AreEqual(3750.0, sec, 0.001);
        }

        [TestMethod]
        public void TryParseDuration_Iso8601SecondsOnly_ParsesAccurately()
        {
            double sec;
            bool ok = TrackDurationParser.TryParseDuration("PT45S", out sec);

            Assert.IsTrue(ok);
            Assert.AreEqual(45.0, sec, 0.001);
        }

        [TestMethod]
        public void TryParseDuration_NumericSeconds_ParsesAccurately()
        {
            double sec;
            bool ok = TrackDurationParser.TryParseDuration("225", out sec);

            Assert.IsTrue(ok);
            Assert.AreEqual(225.0, sec, 0.001);
        }

        [TestMethod]
        public void TryParseDuration_InvalidAndNullInputs_ReturnsFalseSafely()
        {
            double sec;
            Assert.IsFalse(TrackDurationParser.TryParseDuration(null, out sec));
            Assert.IsFalse(TrackDurationParser.TryParseDuration("", out sec));
            Assert.IsFalse(TrackDurationParser.TryParseDuration("   ", out sec));
            Assert.IsFalse(TrackDurationParser.TryParseDuration("invalid:text", out sec));
            Assert.IsFalse(TrackDurationParser.TryParseDuration("-15", out sec));
            Assert.IsFalse(TrackDurationParser.TryParseDuration("3:65", out sec)); // Invalid seconds >= 60
        }

        [TestMethod]
        public void FormatDisplayTime_FormatsVariousSecondsAccurately()
        {
            Assert.AreEqual("0:00", TrackDurationParser.FormatDisplayTime(0));
            Assert.AreEqual("0:00", TrackDurationParser.FormatDisplayTime(-10));
            Assert.AreEqual("0:05", TrackDurationParser.FormatDisplayTime(5));
            Assert.AreEqual("3:45", TrackDurationParser.FormatDisplayTime(225));
            Assert.AreEqual("1:02:15", TrackDurationParser.FormatDisplayTime(3735));
        }
    }
}
