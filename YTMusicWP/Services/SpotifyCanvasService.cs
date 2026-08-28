using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Windows.Data.Json;

namespace YTMusicWP.Services
{
    public static class SpotifyCanvasService
    {
        private static string _spDcCookie = "";
        public static void SetSpDcCookie(string cookie)
        {
            _spDcCookie = cookie;
        }

        private static string _accessToken;
        private static DateTime _tokenExpiresAt;

        // Extract url ending with .mp4 from byte array
        private static string ExtractMp4Url(byte[] data)
        {
            string content = Encoding.UTF8.GetString(data, 0, data.Length);
            Match match = Regex.Match(content, @"https?://[^\s""'\\]+\.mp4(?:\?[^\s""'\\]*)?");
            if (match.Success)
            {
                return match.Value;
            }
            // Fallback: Sometimes it might not have .mp4 if it's a different format?
            // Spotify canvas is almost always .mp4
            return null;
        }

        public static async Task<string> GetCanvasUrlAsync(string songName, string artistName)
        {
            if (string.IsNullOrEmpty(_spDcCookie)) return null;

            try
            {
                // 1. Get Access Token if needed
                if (_accessToken == null || DateTime.Now >= _tokenExpiresAt)
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("Cookie", "sp_dc=" + _spDcCookie);
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                        
                        var tokenResponse = await client.GetAsync("https://open.spotify.com/get_access_token?reason=transport&productType=web_player");
                        if (!tokenResponse.IsSuccessStatusCode) return null;
                        
                        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                        JsonObject json = JsonObject.Parse(tokenJson);
                        _accessToken = json.GetNamedString("accessToken");
                        
                        // Token usually valid for 1 hour
                        _tokenExpiresAt = DateTime.Now.AddMinutes(50);
                    }
                }

                if (string.IsNullOrEmpty(_accessToken)) return null;

                string trackId = null;

                // 2. Search for the track
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _accessToken);
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                    string query = Uri.EscapeDataString(songName + " " + artistName);
                    string searchUrl = $"https://api-partner.spotify.com/pathfinder/v1/query?operationName=searchTracks&variables=%7B%22searchTerm%22%3A%22{query}%22%2C%22offset%22%3A0%2C%22limit%22%3A1%2C%22numberOfTopResults%22%3A1%2C%22includeAudiobooks%22%3Afalse%2C%22includePreReleases%22%3Afalse%7D&extensions=%7B%22persistedQuery%22%3A%7B%22version%22%3A1%2C%22sha256Hash%22%3A%22bc1ca2fcd0ba1013a0fc88e6cc4f190af501851e3dafd3e1ef85840297694428%22%7D%7D";
                    
                    var searchResponse = await client.GetAsync(searchUrl);
                    if (!searchResponse.IsSuccessStatusCode) return null;

                    var searchJson = await searchResponse.Content.ReadAsStringAsync();
                    
                    // Simple string search to find the first uri: "spotify:track:xxxx"
                    Match match = Regex.Match(searchJson, @"""uri""\s*:\s*""spotify:track:([a-zA-Z0-9]+)""");
                    if (match.Success)
                    {
                        trackId = match.Groups[1].Value;
                    }
                }

                if (string.IsNullOrEmpty(trackId)) return null;

                // 3. Request Canvas
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _accessToken);
                    client.DefaultRequestHeaders.Add("User-Agent", "Spotify/9.0.34.593 iOS/18.4 (iPhone15,3)");
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/protobuf"));

                    // Build Protobuf manually
                    string trackUri = "spotify:track:" + trackId;
                    byte[] trackUriBytes = Encoding.UTF8.GetBytes(trackUri);
                    
                    byte[] trackMsg = new byte[trackUriBytes.Length + 2];
                    trackMsg[0] = 0x0A; // Tag 1, length-delimited
                    trackMsg[1] = (byte)trackUriBytes.Length;
                    Buffer.BlockCopy(trackUriBytes, 0, trackMsg, 2, trackUriBytes.Length);

                    byte[] canvasReq = new byte[trackMsg.Length + 2];
                    canvasReq[0] = 0x0A; // Tag 1, length-delimited
                    canvasReq[1] = (byte)trackMsg.Length;
                    Buffer.BlockCopy(trackMsg, 0, canvasReq, 2, trackMsg.Length);

                    ByteArrayContent content = new ByteArrayContent(canvasReq);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/protobuf");

                    var canvasRes = await client.PostAsync("https://spclient.wg.spotify.com/canvaz-cache/v0/canvases", content);
                    if (!canvasRes.IsSuccessStatusCode) return null;

                    byte[] resBytes = await canvasRes.Content.ReadAsByteArrayAsync();
                    
                    // Extract URL
                    return ExtractMp4Url(resBytes);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Canvas Error: " + ex.Message);
                return null;
            }
        }
    }
}
