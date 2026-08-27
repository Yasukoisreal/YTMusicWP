using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Data.Xml.Dom;

namespace YTMusicWP.Services
{
    public static class AppleMusicLyricsApi
    {
        public static async Task<string[]> GetLyricsAsync(string title, string artist, int duration = -1)
        {
            try
            {
                string url = "https://lyrics-api.boidu.dev/getLyrics?s=" + Uri.EscapeDataString(title) +
                             "&a=" + Uri.EscapeDataString(artist);
                if (duration > 0)
                {
                    url += "&d=" + duration;
                }

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Add("User-Agent", "YTMusicWP/1.0");

                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonStr = await response.Content.ReadAsStringAsync();
                        JsonObject jsonObj;
                        if (JsonObject.TryParse(jsonStr, out jsonObj))
                        {
                            if (jsonObj.ContainsKey("ttml") && jsonObj["ttml"].ValueType == JsonValueType.String)
                            {
                                string ttml = jsonObj["ttml"].GetString();
                                return ParseTTML(ttml);
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

        private static string[] ParseTTML(string ttml)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(ttml);

                var pNodes = doc.GetElementsByTagName("p");
                var syncedLrc = new StringBuilder();
                var plainLrc = new StringBuilder();

                foreach (var pNode in pNodes)
                {
                    var el = pNode as XmlElement;
                    if (el == null) continue;

                    string beginAttr = el.GetAttribute("begin");
                    if (string.IsNullOrEmpty(beginAttr)) continue;

                    double startTime = ParseTime(beginAttr);
                    if (startTime < 0) continue;

                    string text = ExtractAllText(el).Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    TimeSpan ts = TimeSpan.FromSeconds(startTime);
                    string lrcTime = $"[{ts.Minutes:D2}:{ts.Seconds:D2}.{(ts.Milliseconds / 10):D2}]";

                    syncedLrc.AppendLine($"{lrcTime} {text}");
                    plainLrc.AppendLine(text);
                }

                if (syncedLrc.Length > 0)
                {
                    return new string[] { syncedLrc.ToString(), plainLrc.ToString() };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TTML Parse Error: " + ex.Message);
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
                if (timeStr.Contains(":"))
                {
                    var parts = timeStr.Split(':');
                    if (parts.Length == 2)
                    {
                        double m = double.Parse(parts[0]);
                        double s = double.Parse(parts[1]);
                        return (m * 60) + s;
                    }
                    else if (parts.Length == 3)
                    {
                        double h = double.Parse(parts[0]);
                        double m = double.Parse(parts[1]);
                        double s = double.Parse(parts[2]);
                        return (h * 3600) + (m * 60) + s;
                    }
                }
                else if (timeStr.EndsWith("s"))
                {
                    return double.Parse(timeStr.TrimEnd('s'));
                }
                else if (timeStr.EndsWith("ms"))
                {
                    return double.Parse(timeStr.Substring(0, timeStr.Length - 2)) / 1000.0;
                }
                else if (timeStr.Contains("."))
                {
                    return double.Parse(timeStr);
                }
            }
            catch { }
            return -1;
        }
    }
}
