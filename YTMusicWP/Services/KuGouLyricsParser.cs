using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Logic thuần (không mạng) cho lyrics KuGou: giải mã KRC, chuyển sang enhanced LRC, bỏ dòng credit,
    /// và lọc bản khác bản gốc (mashup/remix/cover...). Tách riêng để unit test được.
    /// </summary>
    public static class KuGouLyricsParser
    {
        public const int DurationToleranceSec = 8;

        // Khoá XOR công khai của định dạng KRC
        private static readonly byte[] KrcKey =
        {
            0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69
        };

        // Phiên bản khác bản gốc: loại nếu tên bài người dùng đang nghe không có từ này
        private static readonly string[] VariantKeywords =
        {
            "mashup", "remix", "cover", "live", "karaoke", "instrumental", "acoustic", "nightcore",
            "sped up", "slowed", "8d", "dj", "伴奏", "翻唱", "现场", "纯音乐"
        };

        private static readonly string[] CreditKeywords =
        {
            "词", "曲", "编曲", "制作", "混音", "母带", "演唱", "原唱", "监制", "和声", "吉他", "贝斯", "鼓", "录音",
            "lyrics", "lyricist", "composer", "composed", "producer", "produced", "arranger", "arranged",
            "written", "mix", "master", "vocal", "guitar", "bass", "drum"
        };

        /// <summary>KRC = "krc1" + XOR(key) + zlib. Trả về văn bản KRC hoặc null.</summary>
        public static string DecodeKrc(byte[] raw)
        {
            if (raw == null || raw.Length < 8 || raw[0] != (byte)'k' || raw[1] != (byte)'r' || raw[2] != (byte)'c' || raw[3] != (byte)'1') return null;

            var body = new byte[raw.Length - 4];
            for (int i = 0; i < body.Length; i++) body[i] = (byte)(raw[i + 4] ^ KrcKey[i % KrcKey.Length]);

            // zlib = 2 byte header + deflate + adler32 → DeflateStream đọc phần deflate (bỏ 2 byte đầu)
            using (var input = new MemoryStream(body, 2, body.Length - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                deflate.CopyTo(output);
                var bytes = output.ToArray();
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            }
        }

        public static string DecodeKrcBase64(string base64Content)
        {
            if (string.IsNullOrEmpty(base64Content)) return null;
            return DecodeKrc(Convert.FromBase64String(base64Content));
        }

        private static readonly Regex KrcLineRegex = new Regex(@"^\[(\d+),(\d+)\](.*)$");
        private static readonly Regex KrcWordRegex = new Regex(@"<(\d+),(\d+),\d+>([^<]*)");

        /// <summary>
        /// "[lineStartMs,lineDurMs]&lt;offMs,durMs,0&gt;word..." → "[mm:ss.xx]&lt;mm:ss.xx&gt;word...&lt;mm:ss.xx&gt;"
        /// Thẻ thời gian rỗng ở cuối là mốc kết thúc của từ cuối, để từ ngân dài được quét đủ thời lượng.
        /// </summary>
        public static string KrcToEnhancedLrc(string krc, string title)
        {
            if (string.IsNullOrEmpty(krc)) return null;
            int offsetMs = ReadOffset(krc);
            var lines = new List<KeyValuePair<long, string>>();
            foreach (var rawLine in krc.Split('\n'))
            {
                var m = KrcLineRegex.Match(rawLine.TrimEnd('\r'));
                if (!m.Success) continue;
                long lineStart = long.Parse(m.Groups[1].Value) - offsetMs;
                var words = KrcWordRegex.Matches(m.Groups[3].Value);
                if (words.Count == 0) continue;

                var sb = new StringBuilder();
                var plain = new StringBuilder();
                long lastEnd = lineStart;
                foreach (Match w in words)
                {
                    long ws = lineStart + long.Parse(w.Groups[1].Value);
                    long we = ws + long.Parse(w.Groups[2].Value);
                    string text = w.Groups[3].Value;
                    sb.Append('<').Append(FormatTime(ws)).Append('>').Append(text);
                    plain.Append(text);
                    if (we > lastEnd) lastEnd = we;
                }
                if (plain.ToString().Trim().Length == 0) continue;
                sb.Append('<').Append(FormatTime(lastEnd)).Append('>');
                lines.Add(new KeyValuePair<long, string>(lineStart, "[" + FormatTime(Math.Max(0, lineStart)) + "]" + sb));
            }
            return FinishLines(lines, title);
        }

        private static readonly Regex LrcLineRegex = new Regex(@"^\[(\d{1,3}):(\d{2})[.:](\d{2,3})\](.*)$");

        /// <summary>LRC theo dòng của KuGou: bỏ metadata, dòng rỗng và credit ở đầu.</summary>
        public static string CleanLrc(string lrc, string title)
        {
            if (string.IsNullOrEmpty(lrc)) return null;
            var lines = new List<KeyValuePair<long, string>>();
            foreach (var rawLine in lrc.Replace("&apos;", "'").Split('\n'))
            {
                string l = rawLine.TrimEnd('\r');
                var m = LrcLineRegex.Match(l);
                if (!m.Success || m.Groups[4].Value.Trim().Length == 0) continue;
                long ms = long.Parse(m.Groups[1].Value) * 60000 + long.Parse(m.Groups[2].Value) * 1000
                          + (m.Groups[3].Value.Length == 3 ? long.Parse(m.Groups[3].Value) : long.Parse(m.Groups[3].Value) * 10);
                lines.Add(new KeyValuePair<long, string>(ms, l));
            }
            return FinishLines(lines, title);
        }

        /// <summary>Bỏ các dòng giới thiệu/credit ở đầu (dòng "Tên bài - Ca sĩ", 作词/作曲/Lyrics by...) rồi ghép lại.</summary>
        private static string FinishLines(List<KeyValuePair<long, string>> lines, string title)
        {
            if (lines.Count < 3) return null;
            string normTitle = Normalize(NormalizeTitle(title));
            int start = 0;
            for (int i = 0; i < Math.Min(8, lines.Count); i++)
            {
                string text = StripTags(lines[i].Value);
                string plain = Normalize(text);
                bool isTitleLine = normTitle.Length > 0 && plain.StartsWith(normTitle) && text.Contains(" - ");
                if (IsCreditLine(text) || isTitleLine) start = i + 1;
            }
            if (lines.Count - start < 3) return null;

            var sb = new StringBuilder();
            for (int i = start; i < lines.Count; i++) sb.Append(lines[i].Value).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>Dòng credit kiểu "作词：xxx" / "Lyrics by: xxx": phần trước dấu ':' ngắn VÀ chứa từ khoá credit.</summary>
        public static bool IsCreditLine(string text)
        {
            var m = Regex.Match(text ?? "", @"^\s*([^:：]{1,24})[:：]");
            if (!m.Success) return false;
            string prefix = m.Groups[1].Value.ToLowerInvariant();
            foreach (var kw in CreditKeywords)
            {
                if (prefix.Contains(kw)) return true;
            }
            return false;
        }

        /// <summary>
        /// Ứng viên có dùng được không: lệch thời lượng tối đa 8s, không phải bản khác (mashup/remix...);
        /// nếu không biết thời lượng thì bắt buộc tên bài phải khớp.
        /// </summary>
        public static bool IsAcceptable(string requestedTitle, string candidateName, int requestedDur, int candidateDur)
        {
            if (requestedDur > 0 && candidateDur > 0 && Math.Abs(requestedDur - candidateDur) > DurationToleranceSec) return false;
            if (IsOtherVersion(candidateName, requestedTitle)) return false;

            if (requestedDur <= 0 || candidateDur <= 0)
            {
                string reqCore = Normalize(NormalizeTitle(requestedTitle));
                return reqCore.Length > 0 && Normalize(candidateName).Contains(reqCore);
            }
            return true;
        }

        /// <summary>True khi header [ti:...] của KRC là một phiên bản khác bài đang nghe.</summary>
        public static bool HeaderIsOtherVersion(string krc, string requestedTitle)
        {
            var m = Regex.Match(krc ?? "", @"\[ti:([^\]]*)\]");
            return m.Success && IsOtherVersion(m.Groups[1].Value, requestedTitle);
        }

        private static bool IsOtherVersion(string candidate, string requestedTitle)
        {
            string cand = Normalize(candidate);
            string req = Normalize(requestedTitle);
            foreach (var kw in VariantKeywords)
            {
                if (ContainsWord(cand, kw) && !ContainsWord(req, kw)) return true;
            }
            return false;
        }

        public static string BuildKeyword(string title, string artist)
        {
            string a = Regex.Replace(artist ?? "", @"\(.*?\)|（.*?）", "").Replace(" & ", "、").Replace(", ", "、").Trim();
            return NormalizeTitle(title) + " - " + a;
        }

        private static int ReadOffset(string krc)
        {
            var m = Regex.Match(krc, @"\[offset:\s*(-?\d+)\s*\]");
            int v;
            return m.Success && int.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        private static bool ContainsWord(string text, string word)
        {
            if (Regex.IsMatch(word, "^[a-z0-9 ]+$"))
                return Regex.IsMatch(text, @"(^|[^a-z0-9])" + Regex.Escape(word) + @"($|[^a-z0-9])");
            return text.Contains(word);
        }

        private static string StripTags(string line)
        {
            return Regex.Replace(line ?? "", @"\[[^\]]*\]|<[^>]*>", "");
        }

        private static string Normalize(string s)
        {
            return Regex.Replace((s ?? "").ToLowerInvariant(), @"\s+", " ").Trim();
        }

        private static string NormalizeTitle(string title)
        {
            return Regex.Replace(title ?? "", @"\(.*?\)|（.*?）|\[.*?\]|「.*?」|『.*?』|《.*?》", "").Trim();
        }

        private static string FormatTime(long ms)
        {
            if (ms < 0) ms = 0;
            long min = ms / 60000;
            long sec = (ms % 60000) / 1000;
            long cs = (ms % 1000) / 10;
            return min.ToString("00") + ":" + sec.ToString("00") + "." + cs.ToString("00");
        }
    }
}
