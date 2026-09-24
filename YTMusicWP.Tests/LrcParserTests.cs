using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class LrcParserTests
    {
        [TestMethod]
        public void TryParseLrcTime_StandardTwoDigitMilliseconds_ParsesCorrectly()
        {
            TimeSpan result;
            bool success = LrcParser.TryParseLrcTime("01:23.45", out result);

            Assert.IsTrue(success);
            Assert.AreEqual(0, result.Hours);
            Assert.AreEqual(1, result.Minutes);
            Assert.AreEqual(23, result.Seconds);
            Assert.AreEqual(450, result.Milliseconds);
        }

        [TestMethod]
        public void TryParseLrcTime_ThreeDigitMilliseconds_ParsesCorrectly()
        {
            TimeSpan result;
            bool success = LrcParser.TryParseLrcTime("02:15.890", out result);

            Assert.IsTrue(success);
            Assert.AreEqual(2, result.Minutes);
            Assert.AreEqual(15, result.Seconds);
            Assert.AreEqual(890, result.Milliseconds);
        }

        [TestMethod]
        public void TryParseLrcTime_NoMilliseconds_ParsesSecondsAccurately()
        {
            TimeSpan result;
            bool success = LrcParser.TryParseLrcTime("00:45", out result);

            Assert.IsTrue(success);
            Assert.AreEqual(0, result.Minutes);
            Assert.AreEqual(45, result.Seconds);
            Assert.AreEqual(0, result.Milliseconds);
        }

        [TestMethod]
        public void TryParseLrcTime_SurroundingWhitespace_TrimsAndParses()
        {
            TimeSpan result;
            bool success = LrcParser.TryParseLrcTime("  03:10.50  ", out result);

            Assert.IsTrue(success);
            Assert.AreEqual(3, result.Minutes);
            Assert.AreEqual(10, result.Seconds);
            Assert.AreEqual(500, result.Milliseconds);
        }

        [TestMethod]
        public void TryParseLrcTime_InvalidInputs_ReturnsFalseSafely()
        {
            TimeSpan result;
            Assert.IsFalse(LrcParser.TryParseLrcTime(null, out result));
            Assert.IsFalse(LrcParser.TryParseLrcTime("", out result));
            Assert.IsFalse(LrcParser.TryParseLrcTime("   ", out result));
            Assert.IsFalse(LrcParser.TryParseLrcTime("invalid", out result));
            Assert.IsFalse(LrcParser.TryParseLrcTime(":30", out result));
            Assert.IsFalse(LrcParser.TryParseLrcTime("01:", out result));
            Assert.IsFalse(LrcParser.TryParseLrcTime("aa:bb", out result));
        }

        [TestMethod]
        public void ParseLrcTime_ValidInput_ReturnsTimeSpan()
        {
            TimeSpan result = LrcParser.ParseLrcTime("01:10.00");
            Assert.AreEqual(TimeSpan.FromSeconds(70), result);
        }
    }
}
