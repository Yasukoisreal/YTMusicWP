using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace YTMusicWP
{
    public static partial class InnerTubeClient
    {
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
            
            // OAuth2 token
            request.Headers.Add("Authorization", "Bearer " + accessToken);

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
