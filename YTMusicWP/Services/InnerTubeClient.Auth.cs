using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace YTMusicWP
{
    public static partial class InnerTubeClient
    {
        // ==========================================
        // COOKIE-BASED AUTH (SAPISIDHASH)
        // ==========================================
        private static string _cookieString = null;
        private static string _sapisid = null;

        public static bool HasCookieAuth => !string.IsNullOrEmpty(_cookieString) && !string.IsNullOrEmpty(_sapisid);

        public static void SetCookieAuth(string cookieString, string sapisid)
        {
            _cookieString = cookieString;
            _sapisid = sapisid;
        }

        public static void ClearCookieAuth()
        {
            _cookieString = null;
            _sapisid = null;
        }

        public static void LoadCookieAuthFromSettings()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (settings.ContainsKey("GoogleCookieString") && settings.ContainsKey("GoogleSAPISID"))
                {
                    _cookieString = settings["GoogleCookieString"]?.ToString();
                    _sapisid = settings["GoogleSAPISID"]?.ToString();
                }
            }
            catch { }
        }

        private static string GenerateSAPISIDHash(string sapisid, string origin = "https://music.youtube.com")
        {
            long timestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string input = timestamp + " " + sapisid + " " + origin;

            // SHA1 hash
            var provider = Windows.Security.Cryptography.Core.HashAlgorithmProvider.OpenAlgorithm(
                Windows.Security.Cryptography.Core.HashAlgorithmNames.Sha1);
            var buffer = Windows.Security.Cryptography.CryptographicBuffer.ConvertStringToBinary(
                input, Windows.Security.Cryptography.BinaryStringEncoding.Utf8);
            var hashBuffer = provider.HashData(buffer);
            string hash = Windows.Security.Cryptography.CryptographicBuffer.EncodeToHexString(hashBuffer);

            return "SAPISIDHASH " + timestamp + "_" + hash;
        }

        /// <summary>
        /// Cookie-based InnerTube POST request using SAPISIDHASH auth.
        /// This is the same approach SimpMusic uses - works perfectly with WEB_REMIX.
        /// </summary>
        public static async Task<JObject> CookieInnerTubePostAsync(string endpoint, JObject extraParams, string clientName = "WEB_REMIX", string clientVersion = "1.20260304.03.00")
        {
            string visitorData = await GetVisitorDataAsync();
            var clientObj = new JObject
            {
                ["clientName"] = clientName,
                ["clientVersion"] = clientVersion,
                ["hl"] = CurrentLanguage,
                ["gl"] = CurrentRegion,
                ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"
            };
            if (!string.IsNullOrEmpty(visitorData))
                clientObj["visitorData"] = visitorData;

            var body = new JObject
            {
                ["context"] = new JObject { ["client"] = clientObj }
            };
            foreach (var prop in extraParams.Properties())
                body[prop.Name] = prop.Value;

            string apiKey = "AIzaSyC9XL3ZjWddXya6X74dJoCTL-WEYFDNX30";
            string url = $"https://music.youtube.com/youtubei/v1/{endpoint}?key={apiKey}&prettyPrint=false";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");

            // Headers matching SimpMusic's WEB_REMIX approach
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
            request.Headers.Add("Origin", "https://music.youtube.com");
            request.Headers.Add("Referer", "https://music.youtube.com/");
            request.Headers.Add("X-Goog-Authuser", "0");

            // Cookie + SAPISIDHASH auth
            request.Headers.Add("Cookie", _cookieString);
            request.Headers.Add("Authorization", GenerateSAPISIDHash(_sapisid));

            using (var response = await _client.SendAsync(request))
            {
                string resultJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return new JObject { ["_error"] = (int)response.StatusCode, ["_body"] = resultJson.Length > 100 ? resultJson.Substring(0, 100) : resultJson };

                return JObject.Parse(resultJson);
            }
        }

        /// <summary>
        /// Authenticated InnerTube POST request using OAuth Bearer Token.
        /// Defaults to TVHTML5 client but can be overridden.
        /// </summary>
        public static async Task<JObject> AuthInnerTubePostAsync(string endpoint, JObject extraParams, string accessToken, string clientName = "TVHTML5", string clientVersion = "7.20241016.00.00")
        {
            string visitorData = await GetVisitorDataAsync();
            var clientObj = new JObject
            {
                ["clientName"] = clientName,
                ["clientVersion"] = clientVersion,
                ["hl"] = CurrentLanguage,
                ["gl"] = CurrentRegion
            };
            if (clientName == "WEB_REMIX")
            {
                clientObj["osName"] = "Windows";
                clientObj["osVersion"] = "10.0";
                clientObj["platform"] = "DESKTOP";
            }
            if (clientName == "ANDROID_MUSIC")
            {
                clientObj["osName"] = "Android";
                clientObj["osVersion"] = "12";
                clientObj["androidSdkVersion"] = 31;
            }
            if (!string.IsNullOrEmpty(visitorData))
                clientObj["visitorData"] = visitorData;

            var body = new JObject
            {
                ["context"] = new JObject { ["client"] = clientObj }
            };
            foreach (var prop in extraParams.Properties())
                body[prop.Name] = prop.Value;

            string domain = "music.youtube.com";
            string apiKey = "AIzaSyDCU8hByM-4DrUqRUYnGn-3llEO78bcxq8";
            if (clientName == "WEB_REMIX") apiKey = "AIzaSyC9XL3ZjWddXya6X74dJoCTL-WEYFDNX30";
            if (clientName == "ANDROID_MUSIC") apiKey = "AIzaSyA8eiZmM1FaDVjRy-df2KTyQ_vz_yYM39w";
            string url = $"https://{domain}/youtubei/v1/{endpoint}?key={apiKey}&prettyPrint=false";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
            
            // Required headers for standard API calls
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Origin", $"https://{domain}");
            request.Headers.Add("Referer", $"https://{domain}/");
            
            // OAuth2 token or Cookie fallback
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Add("Authorization", "Bearer " + accessToken);
            }
            else if (HasCookieAuth)
            {
                request.Headers.Add("Cookie", _cookieString);
                request.Headers.Add("Authorization", GenerateSAPISIDHash(_sapisid, "https://music.youtube.com"));
            }

            using (var response = await _client.SendAsync(request))
            {
                string resultJson = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                    return new JObject { ["_error"] = (int)response.StatusCode, ["_body"] = resultJson.Length > 100 ? resultJson.Substring(0, 100) : resultJson };
                
                return JObject.Parse(resultJson);
            }
        }
    }
}
