using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Data.Xml.Dom;

namespace YTMusicWP.Services
{
    public class AppleMusicLyricsResult
    {
        public string SyncedLrc { get; set; }
        public string PlainLyrics { get; set; }
        public List<LyricLine> Lines { get; set; }
        public bool HasWordSync { get; set; }
    }

    public static class AppleMusicLyricsApi
    {
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("User-Agent", "YTMusicWP/1.0");
            return client;
        }

        public static async Task<AppleMusicLyricsResult> GetLyricsResultAsync(string title, string artist, int duration = -1)
        {
            try
            {
                string url = "https://lyrics-api.boidu.dev/getLyrics?s=" + Uri.EscapeDataString(title ?? "") +
                             "&a=" + Uri.EscapeDataString(artist ?? "");
                if (duration > 0)
                {
                    url += "&d=" + duration;
                }

                using (var response = await _httpClient.GetAsync(url))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonStr = await response.Content.ReadAsStringAsync();
                        JsonObject jsonObj;
                        if (JsonObject.TryParse(jsonStr, out jsonObj))
                        {
                            if (jsonObj.ContainsKey("ttml") && jsonObj["ttml"].ValueType == JsonValueType.String)
                            {
                                string ttml = jsonObj["ttml"].GetString();
                                return ParseTTMLResult(ttml);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Apple Music Lyrics Error: " + ex.Message);
            }
            return null;
        }

        public static async Task<string[]> GetLyricsAsync(string title, string artist, int duration = -1)
        {
            var res = await GetLyricsResultAsync(title, artist, duration);
            if (res != null && (!string.IsNullOrWhiteSpace(res.SyncedLrc) || !string.IsNullOrWhiteSpace(res.PlainLyrics)))
            {
                return new string[] { res.SyncedLrc, res.PlainLyrics };
            }
            return null;
        }

        public static AppleMusicLyricsResult ParseTTMLResult(string ttml)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(ttml);

                var pNodes = doc.GetElementsByTagName("p");
                var parsedLines = new List<LyricLine>();

                foreach (var pNode in pNodes)
                {
                    var el = pNode as XmlElement;
                    if (el == null) continue;

                    string beginAttr = el.GetAttribute("begin");
                    if (string.IsNullOrEmpty(beginAttr)) continue;

                    double startTime = ParseTime(beginAttr);
                    if (startTime < 0) continue;

                    string endAttr = el.GetAttribute("end");
                    double endTime = !string.IsNullOrEmpty(endAttr) ? ParseTime(endAttr) : -1;

                    // Extract word-level spans
                    var words = new List<LyricWord>();
                    CollectSpans(el, words, startTime, endTime);

                    // Refine word end boundaries
                    for (int k = 0; k < words.Count; k++)
                    {
                        var w = words[k];
                        if (w.EndTime <= w.StartTime)
                        {
                            if (k < words.Count - 1)
                            {
                                w.EndTime = words[k + 1].StartTime;
                            }
                            else if (endTime > w.StartTime.TotalSeconds)
                            {
                                w.EndTime = TimeSpan.FromSeconds(endTime);
                            }
                            else
                            {
                                w.EndTime = w.StartTime + TimeSpan.FromMilliseconds(500);
                            }
                        }
                    }

                    string lineText;
                    if (words.Count > 0)
                    {
                        var sb = new StringBuilder();
                        for (int k = 0; k < words.Count; k++) sb.Append(words[k].Text);
                        lineText = sb.ToString().Trim();
                    }
                    else
                    {
                        lineText = ExtractAllText(el).Trim();
                    }

                    if (string.IsNullOrWhiteSpace(lineText)) continue;

                    TimeSpan lineStartTime = TimeSpan.FromSeconds(startTime);
                    TimeSpan lineEndTimeVal = endTime > 0 ? TimeSpan.FromSeconds(endTime) : (words.Count > 0 ? words[words.Count - 1].EndTime : TimeSpan.Zero);

                    parsedLines.Add(new LyricLine
                    {
                        Time = lineStartTime,
                        EndTime = lineEndTimeVal,
                        Text = lineText,
                        Words = (words.Count > 0) ? words : null
                    });
                }

                if (parsedLines.Count == 0) return null;

                parsedLines.Sort((a, b) => a.Time.CompareTo(b.Time));

                // Interlude dots injection (gaps >= 3000ms, matching SimpMusic / Apple Music)
                var finalLines = new List<LyricLine>(parsedLines.Count + 4);

                // 1. Intro instrumental break (>= 5.0s)
                if (parsedLines[0].Time.TotalSeconds >= 5.0)
                {
                    double introStart = 1.0;
                    double introEnd = parsedLines[0].Time.TotalSeconds;
                    double step = (introEnd - introStart) / 3.0;
                    finalLines.Add(new LyricLine
                    {
                        Time = TimeSpan.FromSeconds(introStart),
                        EndTime = parsedLines[0].Time,
                        Text = "•   •   •",
                        IsInterlude = true,
                        Words = new List<LyricWord>
                        {
                            new LyricWord { Text = "•   ", StartTime = TimeSpan.FromSeconds(introStart), EndTime = TimeSpan.FromSeconds(introStart + step) },
                            new LyricWord { Text = "•   ", StartTime = TimeSpan.FromSeconds(introStart + step), EndTime = TimeSpan.FromSeconds(introStart + step * 2) },
                            new LyricWord { Text = "•",     StartTime = TimeSpan.FromSeconds(introStart + step * 2), EndTime = parsedLines[0].Time }
                        }
                    });
                }

                // 2. Insert lines and detect interlude gaps between consecutive lines
                for (int i = 0; i < parsedLines.Count; i++)
                {
                    var cur = parsedLines[i];
                    finalLines.Add(cur);

                    if (i < parsedLines.Count - 1)
                    {
                        var next = parsedLines[i + 1];
                        double curEndSec;
                        if (cur.EndTime > cur.Time)
                            curEndSec = cur.EndTime.TotalSeconds;
                        else if (cur.HasWords)
                            curEndSec = cur.Words[cur.Words.Count - 1].EndTime.TotalSeconds;
                        else
                            curEndSec = cur.Time.TotalSeconds + 3.0;

                        double nextStartSec = next.Time.TotalSeconds;
                        double gap = nextStartSec - curEndSec;
                        if (gap >= 3.0) // INTERLUDE_MIN_GAP_MS = 3_000L
                        {
                            double dotsStart = curEndSec;
                            double dotsEnd = nextStartSec;
                            double step = (dotsEnd - dotsStart) / 3.0;
                            finalLines.Add(new LyricLine
                            {
                                Time = TimeSpan.FromSeconds(dotsStart),
                                EndTime = next.Time,
                                Text = "•   •   •",
                                IsInterlude = true,
                                Words = new List<LyricWord>
                                {
                                    new LyricWord { Text = "•   ", StartTime = TimeSpan.FromSeconds(dotsStart), EndTime = TimeSpan.FromSeconds(dotsStart + step) },
                                    new LyricWord { Text = "•   ", StartTime = TimeSpan.FromSeconds(dotsStart + step), EndTime = TimeSpan.FromSeconds(dotsStart + step * 2) },
                                    new LyricWord { Text = "•",     StartTime = TimeSpan.FromSeconds(dotsStart + step * 2), EndTime = next.Time }
                                }
                            });
                        }
                    }
                }

                // Build synced and plain LRC strings
                var syncedLrc = new StringBuilder();
                var plainLrc = new StringBuilder();
                bool hasWordSync = false;

                foreach (var line in finalLines)
                {
                    if (line.HasWords && !line.IsInterlude) hasWordSync = true;
                    TimeSpan ts = line.Time;
                    string lrcTime = string.Format("[{0:D2}:{1:D2}.{2:D2}]", ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                    syncedLrc.AppendLine(lrcTime + " " + line.Text);
                    plainLrc.AppendLine(line.Text);
                }

                return new AppleMusicLyricsResult
                {
                    SyncedLrc = syncedLrc.ToString(),
                    PlainLyrics = plainLrc.ToString(),
                    Lines = finalLines,
                    HasWordSync = hasWordSync
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TTML Parse Error: " + ex.Message);
            }
            return null;
        }

        private static void CollectSpans(IXmlNode node, List<LyricWord> words, double lineStart, double lineEnd)
        {
            if (node == null || node.ChildNodes == null) return;

            foreach (var child in node.ChildNodes)
            {
                if (child.NodeType == NodeType.ElementNode)
                {
                    var el = child as XmlElement;
                    if (el != null)
                    {
                        string bAttr = el.GetAttribute("begin");
                        string eAttr = el.GetAttribute("end");

                        // Check if this element contains child elements with begin (e.g. wrapper spans)
                        bool hasChildWithBegin = false;
                        if (el.ChildNodes != null)
                        {
                            foreach (var grandChild in el.ChildNodes)
                            {
                                var gEl = grandChild as XmlElement;
                                if (gEl != null && !string.IsNullOrEmpty(gEl.GetAttribute("begin")))
                                {
                                    hasChildWithBegin = true;
                                    break;
                                }
                            }
                        }

                        if (hasChildWithBegin || string.IsNullOrEmpty(bAttr))
                        {
                            CollectSpans(el, words, lineStart, lineEnd);
                        }
                        else
                        {
                            double s = ParseTime(bAttr);
                            if (s >= 0)
                            {
                                double e = !string.IsNullOrEmpty(eAttr) ? ParseTime(eAttr) : -1;
                                string text = ExtractAllText(el);
                                if (!string.IsNullOrEmpty(text))
                                {
                                    words.Add(new LyricWord
                                    {
                                        Text = text,
                                        StartTime = TimeSpan.FromSeconds(s),
                                        EndTime = e > 0 ? TimeSpan.FromSeconds(e) : TimeSpan.Zero
                                    });
                                }
                            }
                        }
                    }
                }
                else if (child.NodeType == NodeType.TextNode)
                {
                    string txt = child.NodeValue?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(txt) && words.Count > 0)
                    {
                        words[words.Count - 1].Text += txt;
                    }
                }
            }
        }

        private static string[] ParseTTML(string ttml)
        {
            var res = ParseTTMLResult(ttml);
            if (res != null && (!string.IsNullOrWhiteSpace(res.SyncedLrc) || !string.IsNullOrWhiteSpace(res.PlainLyrics)))
            {
                return new string[] { res.SyncedLrc, res.PlainLyrics };
            }
            return null;
        }

        private static string ExtractAllText(IXmlNode node)
        {
            if (node.NodeType == NodeType.TextNode)
            {
                return node.NodeValue?.ToString() ?? "";
            }

            var sb = new StringBuilder();
            foreach (var child in node.ChildNodes)
            {
                sb.Append(ExtractAllText(child));
            }
            return sb.ToString();
        }

        private static double ParseTime(string timeStr)
        {
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (timeStr.Contains(":"))
                {
                    var parts = timeStr.Split(':');
                    if (parts.Length == 2)
                    {
                        double m = double.Parse(parts[0], inv);
                        double s = double.Parse(parts[1], inv);
                        return (m * 60) + s;
                    }
                    else if (parts.Length == 3)
                    {
                        double h = double.Parse(parts[0], inv);
                        double m = double.Parse(parts[1], inv);
                        double s = double.Parse(parts[2], inv);
                        return (h * 3600) + (m * 60) + s;
                    }
                }
                else if (timeStr.EndsWith("s"))
                {
                    return double.Parse(timeStr.TrimEnd('s'), inv);
                }
                else if (timeStr.EndsWith("ms"))
                {
                    return double.Parse(timeStr.Substring(0, timeStr.Length - 2), inv) / 1000.0;
                }
                else
                {
                    double s;
                    if (double.TryParse(timeStr, System.Globalization.NumberStyles.Any, inv, out s))
                    {
                        return s;
                    }
                }
            }
            catch { }
            return -1;
        }
    }
}
