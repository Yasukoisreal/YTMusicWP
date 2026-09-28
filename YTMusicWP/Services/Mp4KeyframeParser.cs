using System;
using System.Collections.Generic;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Minimal ISO-BMFF (MP4) reader for the Samples tab: finds the presentation times of the video keyframes
    /// so a clip can start exactly on a keyframe. Seeking a progressive MP4 in MediaElement to a time between two
    /// keyframes makes the decoder start mid-GOP (grey macroblocks until the next keyframe).
    /// Pure logic (no WinRT) so it can be unit tested.
    /// </summary>
    public static class Mp4KeyframeParser
    {
        public sealed class VideoTrackInfo
        {
            /// <summary>Movie duration in seconds (mvhd).</summary>
            public double DurationSeconds;
            /// <summary>Keyframe presentation times in seconds, ascending.</summary>
            public List<double> KeyframeSeconds = new List<double>();
        }

        /// <summary>
        /// Highest H.264 level (level_idc x10) Windows Phone 8.1 decodes. Above it the decoder refuses the video track:
        /// MediaElement opens and plays the sound over a black picture. Seen on a YouTube itag 18 file declaring
        /// Level 6.2 for a 640x360 stream (an encoder mislabel; the chart feed is otherwise 3.0 / 2.1).
        /// </summary>
        public const int MaxDecodableAvcLevel = 51;

        /// <summary>
        /// level_idc of the first 'avcC' (H.264 decoder configuration) in <paramref name="moov"/>, e.g. 30 for Level 3.0;
        /// -1 when there is none (not H.264, or not within these bytes).
        /// </summary>
        public static int ReadAvcLevel(byte[] moov)
        {
            if (moov == null) return -1;
            // avcC body: configurationVersion, AVCProfileIndication, profile_compatibility, AVCLevelIndication
            for (int i = 4; i + 8 <= moov.Length; i++)
            {
                if (moov[i] == (byte)'a' && moov[i + 1] == (byte)'v' && moov[i + 2] == (byte)'c' && moov[i + 3] == (byte)'C')
                    return moov[i + 7];
            }
            return -1;
        }

        /// <summary>
        /// Locates the top-level 'moov' box in the first bytes of a file.
        /// Returns false if the header bytes end before 'moov' starts (e.g. moov stored at the end of the file).
        /// </summary>
        public static bool TryFindMoov(byte[] head, int length, out long offset, out long size)
        {
            offset = 0; size = 0;
            long pos = 0;
            while (pos + 8 <= length)
            {
                long boxSize = ReadUInt32(head, (int)pos);
                string type = ReadType(head, (int)pos + 4);
                int header = 8;
                if (boxSize == 1)
                {
                    if (pos + 16 > length) return false;
                    boxSize = (long)ReadUInt64(head, (int)pos + 8);
                    header = 16;
                }
                if (boxSize < header) return false; // size 0 (to end of file) or corrupt: not a usable moov
                if (type == "moov")
                {
                    offset = pos;
                    size = boxSize;
                    return true;
                }
                pos += boxSize;
            }
            return false;
        }

        /// <summary>Parses a complete 'moov' box (including its 8-byte header). Returns null if there is no video track.</summary>
        public static VideoTrackInfo ParseMoov(byte[] moov)
        {
            if (moov == null || moov.Length < 8 || ReadType(moov, 4) != "moov") return null;
            var info = new VideoTrackInfo();

            foreach (var box in Children(moov, 8, moov.Length))
            {
                if (box.Type == "mvhd")
                {
                    int p = box.Body;
                    int version = moov[p];
                    long timescale, duration;
                    if (version == 1) { timescale = ReadUInt32(moov, p + 20); duration = (long)ReadUInt64(moov, p + 24); }
                    else { timescale = ReadUInt32(moov, p + 12); duration = ReadUInt32(moov, p + 16); }
                    if (timescale > 0) info.DurationSeconds = (double)duration / timescale;
                }
                else if (box.Type == "trak" && info.KeyframeSeconds.Count == 0)
                {
                    var keyframes = ParseVideoTrak(moov, box);
                    if (keyframes != null) info.KeyframeSeconds = keyframes;
                }
            }
            return info.KeyframeSeconds.Count > 0 ? info : null;
        }

        /// <summary>
        /// The keyframe to start a clip at: the last keyframe at or before <paramref name="targetSeconds"/>
        /// (or the first one after it), nudged 1 ms earlier so rounding can never land just past the keyframe.
        /// </summary>
        public static double ChooseStart(IList<double> keyframes, double targetSeconds)
        {
            if (keyframes == null || keyframes.Count == 0) return 0;
            double best = keyframes[0];
            foreach (double k in keyframes)
            {
                if (k <= targetSeconds) best = k;
                else break;
            }
            return Math.Max(0, best - 0.001);
        }

        // ---------------------------------------------------------------------------------------------

        private static List<double> ParseVideoTrak(byte[] d, Box trak)
        {
            Box mdia = Find(d, trak, "mdia");
            if (mdia == null) return null;
            Box hdlr = Find(d, mdia, "hdlr");
            if (hdlr == null || ReadType(d, hdlr.Body + 8) != "vide") return null;

            Box mdhd = Find(d, mdia, "mdhd");
            if (mdhd == null) return null;
            int mv = d[mdhd.Body];
            long timescale = mv == 1 ? ReadUInt32(d, mdhd.Body + 20) : ReadUInt32(d, mdhd.Body + 12);
            if (timescale <= 0) return null;

            Box stbl = Find(d, Find(d, mdia, "minf"), "stbl");
            if (stbl == null) return null;
            Box stts = Find(d, stbl, "stts");
            if (stts == null) return null;
            Box stss = Find(d, stbl, "stss");
            Box ctts = Find(d, stbl, "ctts");

            // Edit list: presentation time 0 corresponds to media time 'mediaTime' of the first non-empty edit
            long mediaTime = 0;
            Box edts = Find(d, trak, "edts");
            Box elst = edts != null ? Find(d, edts, "elst") : null;
            if (elst != null)
            {
                int ev = d[elst.Body];
                long count = ReadUInt32(d, elst.Body + 4);
                int p = elst.Body + 8;
                for (long i = 0; i < count; i++)
                {
                    long mt = ev == 1 ? (long)ReadUInt64(d, p + 8) : (int)ReadUInt32(d, p + 4);
                    p += ev == 1 ? 20 : 12;
                    if (mt >= 0) { mediaTime = mt; break; }
                }
            }

            // Sync sample numbers (1-based). No stss box = every sample is a keyframe.
            HashSet<long> sync = null;
            if (stss != null)
            {
                long count = ReadUInt32(d, stss.Body + 4);
                sync = new HashSet<long>();
                for (long i = 0; i < count; i++) sync.Add(ReadUInt32(d, stss.Body + 8 + (int)(i * 4)));
            }

            // Composition offsets per sample (run-length encoded)
            var cttsRuns = new List<KeyValuePair<long, long>>();
            if (ctts != null)
            {
                int cv = d[ctts.Body];
                long count = ReadUInt32(d, ctts.Body + 4);
                for (long i = 0; i < count; i++)
                {
                    int p = ctts.Body + 8 + (int)(i * 8);
                    long n = ReadUInt32(d, p);
                    long off = cv == 1 ? (int)ReadUInt32(d, p + 4) : ReadUInt32(d, p + 4);
                    cttsRuns.Add(new KeyValuePair<long, long>(n, off));
                }
            }

            var result = new List<double>();
            long sttsCount = ReadUInt32(d, stts.Body + 4);
            long sample = 1, dts = 0;
            int cttsRun = 0; long cttsLeft = cttsRuns.Count > 0 ? cttsRuns[0].Key : 0;
            for (long i = 0; i < sttsCount; i++)
            {
                int p = stts.Body + 8 + (int)(i * 8);
                long n = ReadUInt32(d, p);
                long delta = ReadUInt32(d, p + 4);
                for (long j = 0; j < n; j++, sample++)
                {
                    long offset = 0;
                    if (cttsRun < cttsRuns.Count)
                    {
                        offset = cttsRuns[cttsRun].Value;
                        if (--cttsLeft <= 0 && ++cttsRun < cttsRuns.Count) cttsLeft = cttsRuns[cttsRun].Key;
                    }
                    if (sync == null || sync.Contains(sample))
                        result.Add(Math.Max(0, (double)(dts + offset - mediaTime) / timescale));
                    dts += delta;
                }
            }
            result.Sort();
            return result;
        }

        private sealed class Box
        {
            public string Type;
            public int Start;   // offset of the size field
            public int Body;    // offset of the payload
            public int End;     // offset just past the box
        }

        private static IEnumerable<Box> Children(byte[] d, int start, int end)
        {
            int pos = start;
            while (pos + 8 <= end)
            {
                long size = ReadUInt32(d, pos);
                int header = 8;
                if (size == 1) { size = (long)ReadUInt64(d, pos + 8); header = 16; }
                else if (size == 0) size = end - pos;
                if (size < header || pos + size > end) yield break;
                yield return new Box { Type = ReadType(d, pos + 4), Start = pos, Body = pos + header, End = (int)(pos + size) };
                pos += (int)size;
            }
        }

        private static Box Find(byte[] d, Box parent, string type)
        {
            if (parent == null || type == null) return null;
            foreach (var b in Children(d, parent.Body, parent.End))
                if (b.Type == type) return b;
            return null;
        }

        private static long ReadUInt32(byte[] d, int p)
        {
            return ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3];
        }

        private static ulong ReadUInt64(byte[] d, int p)
        {
            return ((ulong)ReadUInt32(d, p) << 32) | (ulong)ReadUInt32(d, p + 4);
        }

        private static string ReadType(byte[] d, int p)
        {
            if (p + 4 > d.Length) return "";
            return new string(new[] { (char)d[p], (char)d[p + 1], (char)d[p + 2], (char)d[p + 3] });
        }
    }
}
