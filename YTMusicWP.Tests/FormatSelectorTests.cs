using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTMusicWP.Services;

namespace YTMusicWP.Tests
{
    [TestClass]
    public class FormatSelectorTests
    {
        [TestMethod]
        public void SelectBestFormat_HighQuality_PrefersItag141()
        {
            var formats = new List<StreamFormatInfo>
            {
                new StreamFormatInfo(18, "https://googlevideo.com/videoplayback?itag=18", "video/mp4", 96000),
                new StreamFormatInfo(140, "https://googlevideo.com/videoplayback?itag=140", "audio/mp4", 128000),
                new StreamFormatInfo(141, "https://googlevideo.com/videoplayback?itag=141", "audio/mp4", 256000),
                new StreamFormatInfo(139, "https://googlevideo.com/videoplayback?itag=139", "audio/mp4", 48000)
            };

            var selected = FormatSelector.SelectBestFormat(formats, AudioQualityPreference.High);

            Assert.IsNotNull(selected);
            Assert.AreEqual(141, selected.Itag);
        }

        [TestMethod]
        public void SelectBestFormat_MediumQuality_PrefersItag140()
        {
            var formats = new List<StreamFormatInfo>
            {
                new StreamFormatInfo(18, "https://googlevideo.com/videoplayback?itag=18", "video/mp4", 96000),
                new StreamFormatInfo(140, "https://googlevideo.com/videoplayback?itag=140", "audio/mp4", 128000),
                new StreamFormatInfo(141, "https://googlevideo.com/videoplayback?itag=141", "audio/mp4", 256000)
            };

            var selected = FormatSelector.SelectBestFormat(formats, AudioQualityPreference.Medium);

            Assert.IsNotNull(selected);
            Assert.AreEqual(140, selected.Itag);
        }

        [TestMethod]
        public void SelectBestFormat_LowQuality_PrefersItag139()
        {
            var formats = new List<StreamFormatInfo>
            {
                new StreamFormatInfo(18, "https://googlevideo.com/videoplayback?itag=18", "video/mp4", 96000),
                new StreamFormatInfo(140, "https://googlevideo.com/videoplayback?itag=140", "audio/mp4", 128000),
                new StreamFormatInfo(139, "https://googlevideo.com/videoplayback?itag=139", "audio/mp4", 48000)
            };

            var selected = FormatSelector.SelectBestFormat(formats, AudioQualityPreference.Low);

            Assert.IsNotNull(selected);
            Assert.AreEqual(139, selected.Itag);
        }

        [TestMethod]
        public void SelectBestFormat_MissingPreferred_FallsBackToItag18()
        {
            // Only itag 18 available
            var formats = new List<StreamFormatInfo>
            {
                new StreamFormatInfo(18, "https://googlevideo.com/videoplayback?itag=18", "video/mp4", 96000)
            };

            var selected = FormatSelector.SelectBestFormat(formats, AudioQualityPreference.High);

            Assert.IsNotNull(selected);
            Assert.AreEqual(18, selected.Itag);
        }

        [TestMethod]
        public void SelectBestFormat_LiveStreamUrls_AreFilteredOut()
        {
            var formats = new List<StreamFormatInfo>
            {
                new StreamFormatInfo(140, "https://googlevideo.com/videoplayback?itag=140&live=1", "audio/mp4", 128000),
                new StreamFormatInfo(18, "https://googlevideo.com/videoplayback?itag=18", "video/mp4", 96000)
            };

            var selected = FormatSelector.SelectBestFormat(formats, AudioQualityPreference.Medium);

            Assert.IsNotNull(selected);
            Assert.AreEqual(18, selected.Itag); // Itag 140 was skipped because of live=1
        }

        [TestMethod]
        public void SelectBestFormat_EmptyOrNullFormats_ReturnsNull()
        {
            Assert.IsNull(FormatSelector.SelectBestFormat(null));
            Assert.IsNull(FormatSelector.SelectBestFormat(new List<StreamFormatInfo>()));
        }

        [TestMethod]
        public void SelectBestFormat_InvalidOrWhitespaceUrls_AreIgnored()
        {
            var formats = new List<StreamFormatInfo>
            {
                new StreamFormatInfo(140, "", "audio/mp4"),
                new StreamFormatInfo(141, "   ", "audio/mp4"),
                new StreamFormatInfo(18, "https://googlevideo.com/valid", "video/mp4")
            };

            var selected = FormatSelector.SelectBestFormat(formats, AudioQualityPreference.High);

            Assert.IsNotNull(selected);
            Assert.AreEqual(18, selected.Itag);
        }

        [TestMethod]
        public void IsLiveStreamUrl_CorrectlyDetectsLivePatterns()
        {
            Assert.IsTrue(FormatSelector.IsLiveStreamUrl("https://example.com/play?live=1"));
            Assert.IsTrue(FormatSelector.IsLiveStreamUrl("https://example.com/live/1/stream"));
            Assert.IsFalse(FormatSelector.IsLiveStreamUrl("https://example.com/play?id=12345"));
            Assert.IsFalse(FormatSelector.IsLiveStreamUrl(null));
        }

        [TestMethod]
        public void SelectBestFormat_RejectsWebmAndOpusOnWp81()
        {
            var formats = new List<StreamFormatInfo>
            {
                new StreamFormatInfo(251, "https://googlevideo.com/opus", "audio/webm; codecs=\"opus\"", 160000),
                new StreamFormatInfo(250, "https://googlevideo.com/opus_low", "audio/webm", 70000),
                new StreamFormatInfo(140, "https://googlevideo.com/m4a", "audio/mp4", 128000)
            };

            var selected = FormatSelector.SelectBestFormat(formats, AudioQualityPreference.High);

            Assert.IsNotNull(selected);
            Assert.AreEqual(140, selected.Itag);
        }

        [TestMethod]
        public void SelectBestFormat_RejectsVideoOnlyStreamsInFallback()
        {
            var formats = new List<StreamFormatInfo>
            {
                new StreamFormatInfo(137, "https://googlevideo.com/1080p_video_only", "video/mp4", 4000000),
                new StreamFormatInfo(136, "https://googlevideo.com/720p_video_only", "video/mp4", 2000000)
            };

            var selected = FormatSelector.SelectBestFormat(formats, AudioQualityPreference.Medium);

            Assert.IsNull(selected);
        }
    }
}
