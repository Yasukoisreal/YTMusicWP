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
                var agentSides = ReadAgentSides(doc, pNodes);

                // One group per <p>: the sung line, then its background vocals as their own line right below it
                var groups = new List<List<LyricLine>>();

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

                    // Word-level spans; Apple TTML marks background vocals with ttm:role="x-bg" on a <span> wrapping
                    // them, either around the whole line or around a few words inside it
                    var words = new List<LyricWord>();
                    var bgWords = new List<LyricWord>();
                    CollectSpans(el, words, bgWords, IsBackgroundRole(GetRoleAttribute(el)));
                    RefineWordEnds(words, endTime);
                    RefineWordEnds(bgWords, endTime);

                    string agent = GetAgentAttribute(el);
                    bool opposite;
                    if (string.IsNullOrEmpty(agent) || !agentSides.TryGetValue(agent, out opposite)) opposite = false;

                    var group = new List<LyricLine>(2);
                    if (words.Count > 0 || bgWords.Count == 0)
                    {
                        string lineText = words.Count > 0 ? JoinWords(words) : ExtractAllText(el).Trim();
                        if (!string.IsNullOrWhiteSpace(lineText))
                        {
                            bool isSecondary = lineText.StartsWith("(") && lineText.EndsWith(")");
                            group.Add(new LyricLine
                            {
                                Time = TimeSpan.FromSeconds(startTime),
                                EndTime = endTime > 0 ? TimeSpan.FromSeconds(endTime) : (words.Count > 0 ? words[words.Count - 1].EndTime : TimeSpan.Zero),
                                Text = lineText,
                                IsBackground = isSecondary,
                                IsOppositeSide = opposite,
                                FontStyle = isSecondary ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
                                Words = words.Count > 0 ? words : null
                            });
                        }
                    }
                    if (bgWords.Count > 0)
                    {
                        string bgText = JoinWords(bgWords);
                        if (!string.IsNullOrWhiteSpace(bgText))
                        {
                            // A whole-line background part keeps the <p> timing; one inside a line runs on its own words
                            bool alone = group.Count == 0;
                            group.Add(new LyricLine
                            {
                                Time = alone ? TimeSpan.FromSeconds(startTime) : bgWords[0].StartTime,
                                EndTime = alone && endTime > 0 ? TimeSpan.FromSeconds(endTime) : bgWords[bgWords.Count - 1].EndTime,
                                Text = bgText,
                                IsBackground = true,
                                IsOppositeSide = opposite,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Words = bgWords
                            });
                        }
                    }
                    if (group.Count > 0) groups.Add(group);
                }

                if (groups.Count == 0) return null;

                // Stable sort by the sung line's start, background line kept right under its line
                var parsedLines = groups.OrderBy(g => g[0].Time).SelectMany(g => g).ToList();

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
                        Text = "• • •",
                        IsInterlude = true,
                        Words = new List<LyricWord>
                        {
                            new LyricWord { Text = "• ", StartTime = TimeSpan.FromSeconds(introStart), EndTime = TimeSpan.FromSeconds(introStart + step) },
                            new LyricWord { Text = "• ", StartTime = TimeSpan.FromSeconds(introStart + step), EndTime = TimeSpan.FromSeconds(introStart + step * 2) },
                            new LyricWord { Text = "•",     StartTime = TimeSpan.FromSeconds(introStart + step * 2), EndTime = parsedLines[0].Time }
                        }
                    });
                }

                // 2. Insert lines and detect interlude gaps between consecutive lines. The gap is measured from the latest
                // end so far: a background line can end before the line above it (duets can overlap too)
                double sungUntil = 0;
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
                        sungUntil = Math.Max(sungUntil, curEndSec);
                        curEndSec = sungUntil;

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
                                Text = "• • •",
                                IsInterlude = true,
                                Words = new List<LyricWord>
                                {
                                    new LyricWord { Text = "• ", StartTime = TimeSpan.FromSeconds(dotsStart), EndTime = TimeSpan.FromSeconds(dotsStart + step) },
                                    new LyricWord { Text = "• ", StartTime = TimeSpan.FromSeconds(dotsStart + step), EndTime = TimeSpan.FromSeconds(dotsStart + step * 2) },
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

        private static string GetRoleAttribute(XmlElement el)
        {
            string role = el.GetAttribute("ttm:role");
            if (string.IsNullOrEmpty(role)) role = el.GetAttribute("role");
            return role;
        }

        private static bool IsBackgroundRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return false;
            return role.IndexOf("x-bg", StringComparison.OrdinalIgnoreCase) >= 0
                || role.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0
                || role.IndexOf("secondary", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetAgentAttribute(XmlElement el)
        {
            string agent = el.GetAttribute("ttm:agent");
            if (string.IsNullOrEmpty(agent)) agent = el.GetAttribute("agent");
            return agent;
        }

        /// <summary>
        /// Which singers (ttm:agent ids) are drawn on the right, like Apple Music duets: in order of first appearance the
        /// 1st person is on the left, the 2nd on the right, the 3rd on the left... A "group" agent (everyone) stays left.
        /// </summary>
        private static Dictionary<string, bool> ReadAgentSides(XmlDocument doc, XmlNodeList pNodes)
        {
            var sides = new Dictionary<string, bool>(StringComparer.Ordinal);
            var groupAgents = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var node in doc.GetElementsByTagName("ttm:agent"))
                {
                    var a = node as XmlElement;
                    if (a == null) continue;
                    string id = a.GetAttribute("xml:id");
                    if (!string.IsNullOrEmpty(id) && string.Equals(a.GetAttribute("type"), "group", StringComparison.OrdinalIgnoreCase))
                        groupAgents.Add(id);
                }
            }
            catch { }

            int persons = 0;
            foreach (var pNode in pNodes)
            {
                var el = pNode as XmlElement;
                if (el == null) continue;
                string agent = GetAgentAttribute(el);
                if (string.IsNullOrEmpty(agent) || sides.ContainsKey(agent)) continue;
                sides[agent] = !groupAgents.Contains(agent) && (persons++ % 2 == 1);
            }
            return sides;
        }

        private static string JoinWords(List<LyricWord> words)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < words.Count; k++) sb.Append(words[k].Text);
            return sb.ToString().Trim();
        }

        /// <summary>Words without an end run until the next word, or the line end, or 0.5 s.</summary>
        private static void RefineWordEnds(List<LyricWord> words, double lineEnd)
        {
            for (int k = 0; k < words.Count; k++)
            {
                var w = words[k];
                if (w.EndTime > w.StartTime) continue;
                if (k < words.Count - 1) w.EndTime = words[k + 1].StartTime;
                else if (lineEnd > w.StartTime.TotalSeconds) w.EndTime = TimeSpan.FromSeconds(lineEnd);
                else w.EndTime = w.StartTime + TimeSpan.FromMilliseconds(500);
            }
        }

        /// <summary>
        /// Collects the timed word spans of a line. Spans inside a background-vocal span (ttm:role="x-bg") go to
        /// <paramref name="bgWords"/>, the rest to <paramref name="words"/>; untimed text (spaces) joins the word before it.
        /// </summary>
        private static void CollectSpans(IXmlNode node, List<LyricWord> words, List<LyricWord> bgWords, bool inBackground)
        {
            if (node == null || node.ChildNodes == null) return;
            var target = inBackground ? bgWords : words;

            foreach (var child in node.ChildNodes)
            {
                if (child.NodeType == NodeType.ElementNode)
                {
                    var el = child as XmlElement;
                    if (el != null)
                    {
                        bool isBg = inBackground || IsBackgroundRole(GetRoleAttribute(el));
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
                            CollectSpans(el, words, bgWords, isBg);
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
                                    (isBg ? bgWords : words).Add(new LyricWord
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
                    if (!string.IsNullOrEmpty(txt) && target.Count > 0)
                    {
                        target[target.Count - 1].Text += txt;
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
                else if (timeStr.EndsWith("ms")) // before "s": "500ms" ends with "s" too
                {
                    return double.Parse(timeStr.Substring(0, timeStr.Length - 2), inv) / 1000.0;
                }
                else if (timeStr.EndsWith("s"))
                {
                    return double.Parse(timeStr.TrimEnd('s'), inv);
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
