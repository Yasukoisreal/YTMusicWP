using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;

namespace YTMusicWP.Services
{
    public static class SpotifyCanvasService
    {
        // --- State ---
        private static string _spDcCookie = "";
        private static string _personalToken;
        private static long _personalTokenExpiresMs;
        private static string _clientToken;
        private static long _clientTokenExpiresMs;
        private static int[] _totpCipher;
        private static int _totpVersion;

        // Hardcoded fallback TOTP secret V22 (same as SimpMusic)
        private static readonly int[] TOTP_CIPHER_V22 = { 99, 101, 119, 123, 69, 120, 91, 123, 97, 74, 53, 48, 76, 102, 55, 69, 110, 54 };
        private const int TOTP_VERSION_V22 = 22;

        private const string USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/74.0.3729.157 Safari/537.36";
        private const string BASE32_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static void SetSpDcCookie(string cookie)
        {
            if (cookie != null)
            {
                _spDcCookie = cookie.Replace("sp_dc=", "").Replace(";", "").Trim();
                _personalToken = null;
                _clientToken = null;
            }
        }

        private static long GetUnixTimeMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        /// <summary>
        /// WP8.1 HttpClient default handler has UseCookies=true, which silently
        /// strips manually-set Cookie headers. We must disable that.
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
            return new HttpClient(handler);
        }

        #region TOTP Generation

        private static string Base32Encode(byte[] data)
        {
            if (data.Length == 0) return "";
            var result = new StringBuilder();
            int bits = 0, value = 0;
            foreach (byte b in data)
            {
                value = (value << 8) | (b & 0xFF);
                bits += 8;
                while (bits >= 5)
                {
                    result.Append(BASE32_ALPHABET[(value >> (bits - 5)) & 0x1F]);
                    bits -= 5;
                }
            }
            if (bits > 0)
                result.Append(BASE32_ALPHABET[(value << (5 - bits)) & 0x1F]);
            while (result.Length % 8 != 0)
                result.Append('=');
            return result.ToString();
        }

        /// <summary>
        /// Port of SimpMusic's SpotifyTotp.generateSecret():
        /// XOR transform cipher bytes → join as decimal string → Base32 encode
        /// </summary>
        private static string GenerateTotpSecret(int[] cipherBytes)
        {
            var transformed = new int[cipherBytes.Length];
            for (int i = 0; i < cipherBytes.Length; i++)
                transformed[i] = cipherBytes[i] ^ ((i % 33) + 9);

            string joined = string.Concat(transformed.Select(x => x.ToString()));
            
            // Hex encode
            byte[] joinedBytes = Encoding.UTF8.GetBytes(joined);
            string hex = BitConverter.ToString(joinedBytes).Replace("-", "").ToLowerInvariant();

            // Base64 encode
            byte[] hexBytes = Encoding.UTF8.GetBytes(hex);
            string base64 = Convert.ToBase64String(hexBytes);

            // Base32 encode
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            return Base32Encode(base64Bytes).TrimEnd('=');
        }

        /// <summary>
        /// Standard TOTP (RFC 6238): HMAC-SHA1, 30s step, 6 digits.
        /// Uses WinRT MacAlgorithmProvider available on WP8.1.
        /// </summary>
        private static string ComputeTotp(string secret, long serverTimeSeconds)
        {
            long counter = serverTimeSeconds / 30;

            // Counter as 8-byte big-endian
            byte[] counterBytes = new byte[8];
            long tmp = counter;
            for (int i = 7; i >= 0; i--)
            {
                counterBytes[i] = (byte)(tmp & 0xFF);
                tmp >>= 8;
            }

            // HMAC-SHA1 via WinRT API
            byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
            var provider = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha1);
            var keyBuffer = CryptographicBuffer.CreateFromByteArray(keyBytes);
            var cryptoKey = provider.CreateKey(keyBuffer);
            var dataBuffer = CryptographicBuffer.CreateFromByteArray(counterBytes);
            var signedBuffer = CryptographicEngine.Sign(cryptoKey, dataBuffer);

            byte[] hash;
            CryptographicBuffer.CopyToByteArray(signedBuffer, out hash);

            // Dynamic truncation (RFC 4226)
            int offset = hash[hash.Length - 1] & 0x0F;
            int code = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);
            return (code % 1000000).ToString("D6");
        }

        #endregion

        #region API Methods

        /// <summary>
        /// Fetch latest TOTP secret from GitHub. Falls back to hardcoded V22.
        /// </summary>
        private static async Task FetchTotpSecretAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);
                    var response = await client.GetAsync(
                        "https://raw.githubusercontent.com/xyloflake/spot-secrets-go/refs/heads/main/secrets/secretDict.json");
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        JsonObject obj = JsonObject.Parse(json);
                        int maxVer = 0;
                        string maxKey = null;
                        foreach (var key in obj.Keys)
                        {
                            int v;
                            if (int.TryParse(key, out v) && v > maxVer)
                            {
                                maxVer = v;
                                maxKey = key;
                            }
                        }
                        if (maxKey != null)
                        {
                            _totpVersion = maxVer;
                            JsonArray arr = obj.GetNamedArray(maxKey);
                            _totpCipher = new int[arr.Count];
                            for (uint i = 0; i < arr.Count; i++)
                                _totpCipher[i] = (int)arr.GetNumberAt(i);
                            Debug.WriteLine("TOTP: fetched V" + _totpVersion + " from GitHub");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TOTP fetch failed: " + ex.Message);
            }

            // Fallback to hardcoded V22
            _totpCipher = TOTP_CIPHER_V22;
            _totpVersion = TOTP_VERSION_V22;
            Debug.WriteLine("TOTP: using hardcoded V22 fallback");
        }

        /// <summary>
        /// Try Spotify server-time API, fall back to device time (NTP-synced, good enough for 30s TOTP window)
        /// </summary>
        private static async Task<long> GetServerTimeAsync()
        {
            try
            {
                using (var client = CreateHttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);
                    client.DefaultRequestHeaders.Add("Cookie", "sp_dc=" + _spDcCookie);
                    client.DefaultRequestHeaders.Add("App-platform", "WebPlayer");
                    client.DefaultRequestHeaders.Add("Spotify-App-Version", "1.2.61.20.g3b4cd5b2");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://open.spotify.com");
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://open.spotify.com/");

                    var response = await client.GetAsync("https://open.spotify.com/api/server-time");
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        JsonObject obj = JsonObject.Parse(json);
                        long serverTime = (long)obj.GetNamedNumber("serverTime");
                        Debug.WriteLine("TOTP: using Spotify server time: " + serverTime);
                        return serverTime;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("server-time API failed: " + ex.Message);
            }

            // Fallback: device local time (NTP-synced on WP8.1)
            long localTime = GetUnixTimeMs() / 1000;
            Debug.WriteLine("TOTP: using device local time: " + localTime);
            return localTime;
        }

        /// <summary>
        /// Get personal access token using TOTP auth (same as SimpMusic's SpotifyAuth.refreshToken)
        /// </summary>
        private static async Task<string> RefreshPersonalTokenAsync()
        {
            if (_totpCipher == null)
                await FetchTotpSecretAsync();

            long serverTime = await GetServerTimeAsync();
            string secret = GenerateTotpSecret(_totpCipher ?? TOTP_CIPHER_V22);
            string otp = ComputeTotp(secret, serverTime);
            int version = _totpVersion != 0 ? _totpVersion : TOTP_VERSION_V22;

            // Try "transport" first
            string token = await RequestTokenAsync(otp, "transport", version);
            if (token == null || token.Length != 374)
            {
                // Fallback to "init" (same as SimpMusic)
                token = await RequestTokenAsync(otp, "init", version);
            }
            return token;
        }

        private static async Task<string> RequestTokenAsync(string otp, string reason, int totpVersion)
        {
            using (var client = CreateHttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);
                client.DefaultRequestHeaders.Add("Cookie", "sp_dc=" + _spDcCookie);
                client.DefaultRequestHeaders.Add("App-platform", "WebPlayer");
                client.DefaultRequestHeaders.Add("Spotify-App-Version", "1.2.61.20.g3b4cd5b2");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://open.spotify.com");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://open.spotify.com/");

                string url = string.Format(
                    "https://open.spotify.com/api/token?reason={0}&productType=mobile-web-player&totp={1}&totpServer={1}&totpVer={2}",
                    reason, otp, totpVersion);

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    string errJson = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Token failed ({reason}): {response.StatusCode} {errJson}");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                JsonObject obj = JsonObject.Parse(json);

                string accessToken = obj.ContainsKey("accessToken") ? obj.GetNamedString("accessToken") : "";
                if (string.IsNullOrEmpty(accessToken)) return null;

                _personalToken = accessToken;
                _personalTokenExpiresMs = obj.ContainsKey("accessTokenExpirationTimestampMs")
                    ? (long)obj.GetNamedNumber("accessTokenExpirationTimestampMs")
                    : GetUnixTimeMs() + 3000000; // ~50 min fallback

                return accessToken;
            }
        }

        /// <summary>
        /// Get client token from clienttoken.spotify.com (no sp_dc needed)
        /// </summary>
        private static async Task<string> RefreshClientTokenAsync()
        {
            using (var client = CreateHttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36 Edg/135.0.0.0");
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                string deviceId = Guid.NewGuid().ToString();
                string body = "{\"client_data\":{\"client_version\":\"1.2.62.476.g2ad6e7f3\","
                    + "\"client_id\":\"d8a5ed958d274c2e8ee717e6a4b0971d\","
                    + "\"js_sdk_data\":{\"device_brand\":\"Apple\",\"device_model\":\"unknown\","
                    + "\"os\":\"macos\",\"os_version\":\"10.15.7\","
                    + "\"device_id\":\"" + deviceId + "\","
                    + "\"device_type\":\"computer\"}}}";

                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://clienttoken.spotify.com/v1/clienttoken", content);
                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync();
                JsonObject obj = JsonObject.Parse(json);
                JsonObject grantedToken = obj.GetNamedObject("granted_token");

                _clientToken = grantedToken.GetNamedString("token");
                int expiresAfter = (int)grantedToken.GetNamedNumber("expires_after_seconds");
                _clientTokenExpiresMs = GetUnixTimeMs() + (expiresAfter * 1000L);

                return _clientToken;
            }
        }

        #endregion

        #region Canvas Flow

        private static string ExtractMp4Url(byte[] data)
        {
            string content = Encoding.UTF8.GetString(data, 0, data.Length);
            Match match = Regex.Match(content, @"https?://[^\s""'\\]+\.mp4(?:\?[^\s""'\\]*)?");
            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// Main entry point: fetch Spotify Canvas video URL for a given track.
        /// Returns the .mp4 URL on success, "ERROR: ..." on failure, or null if no cookie.
        /// </summary>
        public static async Task<string> GetCanvasUrlAsync(string songName, string artistName)
        {
            if (string.IsNullOrEmpty(_spDcCookie)) return "ERROR: No sp_dc cookie";

            try
            {
                long now = GetUnixTimeMs();

                // 1. Refresh Personal Token if needed
                if (string.IsNullOrEmpty(_personalToken) || now >= _personalTokenExpiresMs)
                {
                    string result = await RefreshPersonalTokenAsync();
                    if (string.IsNullOrEmpty(result))
                        return "ERROR: Failed to get personal token (TOTP auth failed, check sp_dc cookie)";
                }

                // 2. Refresh Client Token if needed
                if (string.IsNullOrEmpty(_clientToken) || now >= _clientTokenExpiresMs)
                {
                    string result = await RefreshClientTokenAsync();
                    if (string.IsNullOrEmpty(result))
                        return "ERROR: Failed to get client token";
                }

                // 3. Search for the track on Spotify
                string trackId = null;
                using (var client = CreateHttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _personalToken);
                    client.DefaultRequestHeaders.Add("Client-Token", _clientToken);
                    client.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);

                    string query = Uri.EscapeDataString(songName + " " + artistName);
                    string searchUrl = "https://api-partner.spotify.com/pathfinder/v1/query?operationName=searchTracks"
                        + "&variables=%7B%22searchTerm%22%3A%22" + query
                        + "%22%2C%22offset%22%3A0%2C%22limit%22%3A3%2C%22numberOfTopResults%22%3A3%2C%22includeAudiobooks%22%3Afalse%2C%22includePreReleases%22%3Afalse%7D"
                        + "&extensions=%7B%22persistedQuery%22%3A%7B%22version%22%3A1%2C%22sha256Hash%22%3A%22bc1ca2fcd0ba1013a0fc88e6cc4f190af501851e3dafd3e1ef85840297694428%22%7D%7D";

                    var searchResponse = await client.GetAsync(searchUrl);
                    if (!searchResponse.IsSuccessStatusCode)
                        return "ERROR: Search failed (" + searchResponse.StatusCode + ")";

                    string searchJson = await searchResponse.Content.ReadAsStringAsync();
                    Match match = Regex.Match(searchJson, @"""uri""\s*:\s*""spotify:track:([a-zA-Z0-9]+)""");
                    if (match.Success)
                        trackId = match.Groups[1].Value;
                }

                if (string.IsNullOrEmpty(trackId))
                    return "ERROR: Track not found on Spotify";

                // 4. Request Canvas with both tokens
                using (var client = CreateHttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _personalToken);
                    client.DefaultRequestHeaders.Add("Client-Token", _clientToken);
                    client.DefaultRequestHeaders.Add("User-Agent", "Spotify/9.0.34.593 iOS/18.4 (iPhone15,3)");
                    client.DefaultRequestHeaders.Accept.Add(
                        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/protobuf"));

                    // Build Protobuf: CanvasRequest { repeated Track { string track_uri = 1 } = 1 }
                    string trackUri = "spotify:track:" + trackId;
                    byte[] trackUriBytes = Encoding.UTF8.GetBytes(trackUri);

                    // Inner message: Track { track_uri = trackUri }
                    byte[] trackMsg = new byte[trackUriBytes.Length + 2];
                    trackMsg[0] = 0x0A; // field 1, wire type 2 (length-delimited)
                    trackMsg[1] = (byte)trackUriBytes.Length;
                    Buffer.BlockCopy(trackUriBytes, 0, trackMsg, 2, trackUriBytes.Length);

                    // Outer message: CanvasRequest { tracks = [trackMsg] }
                    byte[] canvasReq = new byte[trackMsg.Length + 2];
                    canvasReq[0] = 0x0A; // field 1, wire type 2
                    canvasReq[1] = (byte)trackMsg.Length;
                    Buffer.BlockCopy(trackMsg, 0, canvasReq, 2, trackMsg.Length);

                    var reqContent = new ByteArrayContent(canvasReq);
                    reqContent.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("application/protobuf");

                    var canvasRes = await client.PostAsync(
                        "https://spclient.wg.spotify.com/canvaz-cache/v0/canvases", reqContent);
                    if (!canvasRes.IsSuccessStatusCode)
                        return "ERROR: Canvas request failed (" + canvasRes.StatusCode + ")";

                    byte[] resBytes = await canvasRes.Content.ReadAsByteArrayAsync();
                    string url = ExtractMp4Url(resBytes);
                    if (string.IsNullOrEmpty(url))
                        return "ERROR: No canvas video for this track";
                    return url;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Canvas Error: " + ex.Message);
                return "ERROR: " + ex.Message;
            }
        }

        #endregion
    }
}
