using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class Mp4KeyframeParserTests
    {
        // ---- tiny MP4 box builder ------------------------------------------------------------

        private static byte[] U32(long v) { return new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }; }

        private static byte[] Box(string type, params byte[][] parts)
        {
            var body = parts.SelectMany(p => p).ToArray();
            return U32(8 + body.Length).Concat(Encoding.ASCII.GetBytes(type)).Concat(body).ToArray();
        }

        private static byte[] FullBox(string type, int version, params byte[][] parts)
        {
            return Box(type, new[] { new byte[] { (byte)version, 0, 0, 0 } }.Concat(parts).ToArray());
        }

        private static byte[] Table(params long[] values) { return values.SelectMany(U32).ToArray(); }

        /// <summary>
        /// 30 fps video (timescale 15360, 512 ticks/frame), 300 frames = 10 s, keyframes every 60 frames (2 s),
        /// optional audio track first, optional B-frame composition offset + edit list shift.
        /// </summary>
        private static byte[] BuildMoov(bool withAudioFirst = true, bool withCtts = false, bool withStss = true)
        {
            var videoStbl = new List<byte[]>
            {
                FullBox("stts", 0, Table(1, 300, 512)),
            };
            if (withStss) videoStbl.Add(FullBox("stss", 0, Table(5, 1, 61, 121, 181, 241)));
            if (withCtts) videoStbl.Add(FullBox("ctts", 0, Table(1, 300, 1024))); // every sample shown 2 frames later

            var trakParts = new List<byte[]> { Box("tkhd", new byte[84]) };
            if (withCtts) trakParts.Add(Box("edts", FullBox("elst", 0, Table(1, 10000, 1024, 0x00010000)))); // media_time 1024
            trakParts.Add(Box("mdia",
                FullBox("mdhd", 0, Table(0, 0, 15360, 153600, 0)),
                FullBox("hdlr", 0, Table(0), Encoding.ASCII.GetBytes("vide"), new byte[13]),
                Box("minf", Box("stbl", videoStbl.ToArray()))));
            var video = Box("trak", trakParts.ToArray());

            var audio = Box("trak", Box("mdia",
                FullBox("mdhd", 0, Table(0, 0, 44100, 441000, 0)),
                FullBox("hdlr", 0, Table(0), Encoding.ASCII.GetBytes("soun"), new byte[13]),
                Box("minf", Box("stbl", FullBox("stts", 0, Table(1, 430, 1024))))));

            var tracks = withAudioFirst ? new[] { audio, video } : new[] { video, audio };
            return Box("moov", new[] { FullBox("mvhd", 0, Table(0, 0, 1000, 10000), new byte[80]) }.Concat(tracks).ToArray());
        }

        // ---- tests ---------------------------------------------------------------------------

        [TestMethod]
        public void TryFindMoov_FindsMoovAfterFtyp()
        {
            var moov = BuildMoov();
            var file = Box("ftyp", Encoding.ASCII.GetBytes("mp42"), U32(0)).Concat(moov).Concat(Box("mdat", new byte[64])).ToArray();

            long offset, size;
            Assert.IsTrue(Mp4KeyframeParser.TryFindMoov(file, file.Length, out offset, out size));
            Assert.AreEqual(16, offset);
            Assert.AreEqual(moov.Length, size);
        }

        [TestMethod]
        public void TryFindMoov_MoovAfterMdatBeyondHead_ReturnsFalse()
        {
            var file = Box("ftyp", Encoding.ASCII.GetBytes("mp42"), U32(0)).Concat(Box("mdat", new byte[5000])).Concat(BuildMoov()).ToArray();
            long offset, size;
            Assert.IsFalse(Mp4KeyframeParser.TryFindMoov(file, 1024, out offset, out size)); // only the first 1 KB is known
        }

        [TestMethod]
        public void ParseMoov_ReadsDurationAndVideoKeyframes_SkippingAudioTrack()
        {
            var info = Mp4KeyframeParser.ParseMoov(BuildMoov(withAudioFirst: true));
            Assert.IsNotNull(info);
            Assert.AreEqual(10.0, info.DurationSeconds, 1e-9);
            CollectionAssert.AreEqual(new[] { 0.0, 2.0, 4.0, 6.0, 8.0 }, info.KeyframeSeconds.Select(k => Math.Round(k, 6)).ToArray());
        }

        [TestMethod]
        public void ParseMoov_AppliesCompositionOffsetAndEditList()
        {
            // ctts +1024 and elst media_time 1024 cancel out: presentation times equal decode times
            var info = Mp4KeyframeParser.ParseMoov(BuildMoov(withCtts: true));
            CollectionAssert.AreEqual(new[] { 0.0, 2.0, 4.0, 6.0, 8.0 }, info.KeyframeSeconds.Select(k => Math.Round(k, 6)).ToArray());
        }

        [TestMethod]
        public void ParseMoov_NoStss_EverySampleIsKeyframe()
        {
            var info = Mp4KeyframeParser.ParseMoov(BuildMoov(withStss: false));
            Assert.AreEqual(300, info.KeyframeSeconds.Count);
        }

        [TestMethod]
        public void ChooseStart_PicksLastKeyframeAtOrBeforeTarget_NudgedEarlier()
        {
            var keys = new List<double> { 0, 2, 4, 6, 8 };
            Assert.AreEqual(3.999, Mp4KeyframeParser.ChooseStart(keys, 5.5), 1e-9);
            Assert.AreEqual(5.999, Mp4KeyframeParser.ChooseStart(keys, 6.0), 1e-9);
            Assert.AreEqual(0.0, Mp4KeyframeParser.ChooseStart(keys, 0.5), 1e-9);
            Assert.AreEqual(0.0, Mp4KeyframeParser.ChooseStart(new List<double>(), 5), 1e-9);
        }
    }
}
