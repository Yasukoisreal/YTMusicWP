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

        [TestMethod]
        public void TryParseEnhancedWords_ValidEnhancedLrc_ParsesWordsAndTimestamps()
        {
            string raw = "<00:10.00> Never <00:10.50> gonna <00:11.00> give <00:11.50> you <00:12.00> up";
            string plainText;
            System.Collections.Generic.List<YTMusicWP.LyricWord> words;

            bool ok = LrcParser.TryParseEnhancedWords(raw, TimeSpan.FromSeconds(10), out plainText, out words);

            Assert.IsTrue(ok);
            Assert.IsNotNull(words);
            Assert.AreEqual(5, words.Count);
            Assert.AreEqual("Never gonna give you up", plainText);

            Assert.AreEqual("Never ", words[0].Text);
            Assert.AreEqual(TimeSpan.FromSeconds(10.0), words[0].StartTime);
            Assert.AreEqual(TimeSpan.FromSeconds(10.5), words[0].EndTime);

            Assert.AreEqual("gonna ", words[1].Text);
            Assert.AreEqual(TimeSpan.FromSeconds(10.5), words[1].StartTime);
            Assert.AreEqual(TimeSpan.FromSeconds(11.0), words[1].EndTime);

            Assert.AreEqual("up", words[4].Text);
            Assert.AreEqual(TimeSpan.FromSeconds(12.0), words[4].StartTime);
            Assert.AreEqual(TimeSpan.FromSeconds(12.5), words[4].EndTime);
        }

        [TestMethod]
        public void TryParseEnhancedWords_PlainLineWithoutTags_ReturnsFalse()
        {
            string raw = "Just a normal lyric line without word sync";
            string plainText;
            System.Collections.Generic.List<YTMusicWP.LyricWord> words;

            bool ok = LrcParser.TryParseEnhancedWords(raw, TimeSpan.FromSeconds(5), out plainText, out words);

            Assert.IsFalse(ok);
            Assert.IsNull(words);
            Assert.AreEqual(raw, plainText);
        }

        [TestMethod]
        public void TryParseEnhancedWords_InvalidOrEmpty_ReturnsFalseSafely()
        {
            string plainText;
            System.Collections.Generic.List<YTMusicWP.LyricWord> words;

            Assert.IsFalse(LrcParser.TryParseEnhancedWords(null, TimeSpan.Zero, out plainText, out words));
            Assert.IsFalse(LrcParser.TryParseEnhancedWords("", TimeSpan.Zero, out plainText, out words));
            Assert.IsFalse(LrcParser.TryParseEnhancedWords("   ", TimeSpan.Zero, out plainText, out words));
            Assert.IsFalse(LrcParser.TryParseEnhancedWords("<invalid> word", TimeSpan.Zero, out plainText, out words));
        }

        [TestMethod]
        public void TryParseEnhancedWords_InterludeDots_ParsesCorrectly()
        {
            string raw = "<00:05.00>•   <00:07.00>•   <00:09.00>•";
            string plainText;
            System.Collections.Generic.List<YTMusicWP.LyricWord> words;

            bool ok = LrcParser.TryParseEnhancedWords(raw, TimeSpan.FromSeconds(5), out plainText, out words);

            Assert.IsTrue(ok);
            Assert.AreEqual(3, words.Count);
            Assert.AreEqual("•   •   •", plainText);
            Assert.AreEqual(TimeSpan.FromSeconds(5), words[0].StartTime);
            Assert.AreEqual(TimeSpan.FromSeconds(7), words[0].EndTime);
            Assert.AreEqual(TimeSpan.FromSeconds(7), words[1].StartTime);
            Assert.AreEqual(TimeSpan.FromSeconds(9), words[1].EndTime);
            Assert.AreEqual(TimeSpan.FromSeconds(9), words[2].StartTime);
        }
    }
}
