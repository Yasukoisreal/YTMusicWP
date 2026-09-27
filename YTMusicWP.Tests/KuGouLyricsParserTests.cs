using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class KuGouLyricsParserTests
    {
        private static readonly byte[] Key =
        {
            0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69
        };

        /// <summary>Tạo KRC giống KuGou: "krc1" + XOR(zlib(text)).</summary>
        private static byte[] EncodeKrc(string text)
        {
            byte[] deflated;
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
                {
                    var data = Encoding.UTF8.GetBytes(text);
                    ds.Write(data, 0, data.Length);
                }
                deflated = ms.ToArray();
            }
            var zlib = new List<byte> { 0x78, 0x9C };
            zlib.AddRange(deflated);
            zlib.AddRange(new byte[] { 0, 0, 0, 0 }); // adler32 (không kiểm tra)

            var result = new List<byte> { (byte)'k', (byte)'r', (byte)'c', (byte)'1' };
            for (int i = 0; i < zlib.Count; i++) result.Add((byte)(zlib[i] ^ Key[i % Key.Length]));
            return result.ToArray();
        }

        private const string SampleKrc =
            "[ti:Faded]\n[ar:Alan Walker]\n[offset:0]\n" +
            "[0,3000]<0,500,0>Faded<500,500,0> <1000,500,0>-<1500,500,0> <2000,500,0>Alan<2500,500,0> Walker\n" +
            "[3000,2000]<0,500,0>作词<500,500,0>：<1000,1000,0>Jesper\n" +
            "[10996,3810]<0,225,0>All <225,574,0>I <799,1368,0>knew\n" +
            "[16046,4090]<0,159,0>Is <159,199,0>I <358,2000,0>know\n" +
            "[20859,3394]<0,223,0>Bad <223,2065,0>decisions\n";

        [TestMethod]
        public void DecodeKrc_RoundTrip_ReturnsOriginalText()
        {
            Assert.AreEqual(SampleKrc, KuGouLyricsParser.DecodeKrc(EncodeKrc(SampleKrc)));
        }

        [TestMethod]
        public void DecodeKrc_WrongMagic_ReturnsNull()
        {
            var bytes = EncodeKrc(SampleKrc);
            bytes[0] = (byte)'x';
            Assert.IsNull(KuGouLyricsParser.DecodeKrc(bytes));
        }

        [TestMethod]
        public void KrcToEnhancedLrc_DropsTitleAndCreditLines_KeepsWordTimings()
        {
            string lrc = KuGouLyricsParser.KrcToEnhancedLrc(SampleKrc, "Faded");
            var lines = lrc.Split('\n');

            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("[00:10.99]<00:10.99>All <00:11.22>I <00:11.79>knew<00:13.16>", lines[0]);
            StringAssert.StartsWith(lines[2], "[00:20.85]<00:20.85>Bad ");
        }

        [TestMethod]
        public void KrcToEnhancedLrc_OutputParsesAsEnhancedLrc_LastWordKeepsFullDuration()
        {
            string lrc = KuGouLyricsParser.KrcToEnhancedLrc(SampleKrc, "Faded");
            string line = lrc.Split('\n')[1]; // "Is I know" – "know" ngân 2000ms
            string body = line.Substring(line.IndexOf(']') + 1);

            string plain;
            List<YTMusicWP.LyricWord> words;
            Assert.IsTrue(LrcParser.TryParseEnhancedWords(body, TimeSpan.FromMilliseconds(16046), out plain, out words));
            Assert.AreEqual("Is I know", plain);

            var know = words.Find(w => w.Text == "know");
            Assert.IsNotNull(know);
            Assert.AreEqual(TimeSpan.FromMilliseconds(16400), know.StartTime);
            Assert.AreEqual(TimeSpan.FromMilliseconds(18400), know.EndTime); // không bị cắt còn 500ms
        }

        [TestMethod]
        public void KrcToEnhancedLrc_AppliesOffset()
        {
            string krc = SampleKrc.Replace("[offset:0]", "[offset:500]");
            string lrc = KuGouLyricsParser.KrcToEnhancedLrc(krc, "Faded");
            StringAssert.StartsWith(lrc, "[00:10.49]<00:10.49>All ");
        }

        [TestMethod]
        public void IsCreditLine_OnlyMatchesCreditPrefixes()
        {
            Assert.IsTrue(KuGouLyricsParser.IsCreditLine("作词：Jesper Borgen"));
            Assert.IsTrue(KuGouLyricsParser.IsCreditLine("Lyrics by: Someone"));
            Assert.IsTrue(KuGouLyricsParser.IsCreditLine("Composer : Alan Walker"));
            Assert.IsFalse(KuGouLyricsParser.IsCreditLine("Love: it's all we need"));
            Assert.IsFalse(KuGouLyricsParser.IsCreditLine("You were the shadow to my light"));
        }

        [TestMethod]
        public void IsAcceptable_RejectsDurationMismatchBeyondTolerance()
        {
            Assert.IsTrue(KuGouLyricsParser.IsAcceptable("Faded", "Faded - Alan Walker", 212, 218));
            Assert.IsFalse(KuGouLyricsParser.IsAcceptable("Faded", "Faded - Alan Walker", 212, 240));
        }

        [TestMethod]
        public void IsAcceptable_RejectsOtherVersionsUnlessRequested()
        {
            Assert.IsFalse(KuGouLyricsParser.IsAcceptable("Faded", "Faded - Alan Walker (Mashup)", 212, 212));
            Assert.IsFalse(KuGouLyricsParser.IsAcceptable("Faded", "Faded (Remix)", 212, 212));
            Assert.IsTrue(KuGouLyricsParser.IsAcceptable("Faded (Remix)", "Faded (Remix)", 212, 212));
            // "live" là từ riêng, không được khớp nhầm trong "alive"
            Assert.IsTrue(KuGouLyricsParser.IsAcceptable("Alive", "Alive - Sia", 263, 263));
        }

        [TestMethod]
        public void IsAcceptable_UnknownDuration_RequiresTitleMatch()
        {
            Assert.IsTrue(KuGouLyricsParser.IsAcceptable("Faded", "Faded - Alan Walker", 0, 212));
            Assert.IsFalse(KuGouLyricsParser.IsAcceptable("Faded", "Alone - Alan Walker", 0, 212));
        }

        [TestMethod]
        public void HeaderIsOtherVersion_DetectsMashupHeader()
        {
            Assert.IsTrue(KuGouLyricsParser.HeaderIsOtherVersion("[ti:Faded - Alan Walker (Mashup)]\n", "Faded"));
            Assert.IsFalse(KuGouLyricsParser.HeaderIsOtherVersion("[ti:Faded]\n", "Faded"));
        }

        [TestMethod]
        public void CleanLrc_KeepsTimedLyricLinesOnly()
        {
            string lrc = "[ti:Faded]\n[00:00.00]作词：Jesper\n[00:10.99]All I knew\n[00:16.04]Is I know\n[00:20.85]Bad decisions\n[00:25.00]\n";
            string cleaned = KuGouLyricsParser.CleanLrc(lrc, "Faded");
            Assert.AreEqual("[00:10.99]All I knew\n[00:16.04]Is I know\n[00:20.85]Bad decisions", cleaned);
        }
    }
}
