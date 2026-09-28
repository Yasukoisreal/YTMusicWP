using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YTMusicWP
{
    /// <summary>
    /// One identity the user can act as: a Google account's own channel or one of its brand channels.
    /// Several identities can share one cookie session (every Google account signed in to that browser
    /// session, plus their brand channels); <see cref="AuthUser"/> picks the Google account inside the session
    /// and <see cref="PageId"/> the brand channel.
    /// </summary>
    public sealed class YouTubeAccount
    {
        public string Name { get; set; }
        /// <summary>@handle or byline shown under the name.</summary>
        public string Handle { get; set; }
        /// <summary>Email of the owning Google account, when the switcher tells it.</summary>
        public string Email { get; set; }
        public string AvatarUrl { get; set; }
        /// <summary>Index of the owning Google account in the cookie session (the "authuser" URL parameter).</summary>
        public int AuthUser { get; set; }
        /// <summary>Brand channel id; null for the Google account's own channel.</summary>
        public string PageId { get; set; }
        public string GaiaId { get; set; }
        public string CookieString { get; set; }
        public string Sapisid { get; set; }

        /// <summary>Whether the switcher marked this identity as the one currently selected in its session.</summary>
        [JsonIgnore]
        public bool IsSelected { get; set; }

        /// <summary>Stable identity across sessions: owning Google account + channel.</summary>
        [JsonIgnore]
        public string Key
        {
            get { return (GaiaId ?? Email ?? Handle ?? Name ?? "") + "|" + (PageId ?? ""); }
        }
    }

    public static partial class InnerTubeClient
    {
        // Signed-in requests carry the selected account's cookies in an explicit Cookie header. A client with its
        // own cookie store would also send cookies set by earlier responses (anonymous visitor cookies, another
        // account's rotated session cookies), and the request would no longer be purely that account's.
        private static readonly HttpClient _authClient = CreateAuthClient();

        private static HttpClient CreateAuthClient()
        {
            var handler = new HttpClientHandler();
            try { handler.UseCookies = false; } catch { }
            try
            {
                if (handler.SupportsAutomaticDecompression)
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            }
            catch { }
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>Debug: whether YouTube treated a signed-in request as signed in (GFEEDBACK logged_in).</summary>
        [Conditional("DEBUG")]
        private static void LogSignedInState(string endpoint, JObject json)
        {
            try
            {
                var v = json.SelectToken("$.responseContext.serviceTrackingParams[?(@.service=='GFEEDBACK')].params[?(@.key=='logged_in')].value");
                Debug.WriteLine("[InnerTubeAuth] " + endpoint + " logged_in=" + (v != null ? v.ToString() : "?")
                    + " authuser=" + _authUser + (_pageId != null ? " (brand channel)" : ""));
            }
            catch { }
        }

        private static int _authUser = 0;
        private static string _pageId = null;

        public static int CookieAuthUser { get { return _authUser; } }
        public static string CookiePageId { get { return _pageId; } }

        /// <summary>Selects which identity of the cookie session requests act as.</summary>
        public static void SetCookieIdentity(int authUser, string pageId)
        {
            _authUser = authUser < 0 ? 0 : authUser;
            _pageId = string.IsNullOrEmpty(pageId) ? null : pageId;
        }

        /// <summary>
        /// Identity headers for cookie requests (same as music.youtube.com and SimpMusic): the Google account inside
        /// the cookie session and, for a brand channel, the channel. A brand channel's page id only works together
        /// with the authuser that owns it; with a wrong authuser YouTube answers as if signed out.
        /// </summary>
        private static void AddIdentityHeaders(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", _authUser.ToString());
            if (_pageId != null) request.Headers.TryAddWithoutValidation("X-Goog-PageId", _pageId);
        }

        private static void AddIdentityHeaders(Windows.Web.Http.HttpRequestMessage request)
        {
            request.Headers.TryAppendWithoutValidation("X-Goog-AuthUser", _authUser.ToString());
            if (_pageId != null) request.Headers.TryAppendWithoutValidation("X-Goog-PageId", _pageId);
        }

        /// <summary>
        /// Lists every identity reachable with a cookie session (music.youtube.com/getAccountSwitcherEndpoint):
        /// each signed-in Google account and each of its brand channels. Uses the current session when no cookie
        /// is given. Returns an empty list on failure.
        /// </summary>
        public static async Task<List<YouTubeAccount>> GetAccountListAsync(string cookieString = null, string sapisid = null)
        {
            bool currentSession = cookieString == null;
            if (currentSession)
            {
                cookieString = _cookieString;
                sapisid = _sapisid;
            }
            var result = new List<YouTubeAccount>();
            if (string.IsNullOrEmpty(cookieString) || string.IsNullOrEmpty(sapisid)) return result;

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, "https://music.youtube.com/getAccountSwitcherEndpoint"))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
                    if (!string.IsNullOrEmpty(CurrentLanguage)) request.Headers.TryAddWithoutValidation("Accept-Language", CurrentLanguage);
                    request.Headers.TryAddWithoutValidation("Origin", "https://music.youtube.com");
                    request.Headers.TryAddWithoutValidation("Referer", "https://music.youtube.com/");
                    request.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", "67");
                    request.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", "1.20260304.03.00");
                    request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", currentSession ? _authUser.ToString() : "0");
                    request.Headers.TryAddWithoutValidation("Cookie", cookieString);
                    request.Headers.TryAddWithoutValidation("Authorization", GenerateSAPISIDHash(sapisid));

                    using (var response = await _authClient.SendAsync(request).ConfigureAwait(false))
                    {
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine("[Accounts] switcher HTTP " + (int)response.StatusCode);
                            return result;
                        }
                        // The body is wrapped: ")]}'" before the JSON and a ";" after it, so read exactly one object
                        int start = text.IndexOf('{');
                        if (start < 0) return result;
                        using (var sr = new System.IO.StringReader(text.Substring(start)))
                        using (var jr = new JsonTextReader(sr))
                        {
                            result = ParseAccountSwitcher(JObject.Load(jr));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Accounts] switcher error: " + ex.Message);
            }

            foreach (var a in result)
            {
                a.CookieString = cookieString;
                a.Sapisid = sapisid;
            }
            Debug.WriteLine("[Accounts] switcher returned " + result.Count + " identities");
            foreach (var a in result)
                Debug.WriteLine("[Accounts]   " + a.Name + " authuser=" + a.AuthUser + (a.PageId != null ? " brand" : "") + (a.IsSelected ? " (selected)" : ""));
            return result;
        }

        private static readonly Regex _authUserRegex = new Regex(@"[?&]authuser=(\d+)");

        internal static List<YouTubeAccount> ParseAccountSwitcher(JObject root)
        {
            var list = new List<YouTubeAccount>();
            // One section per signed-in Google account; its header carries the account's email
            foreach (var section in root.SelectTokens("$..accountSectionListRenderer"))
            {
                string email = Text(section.SelectToken("header.googleAccountHeaderRenderer.email"));
                foreach (var itemSection in section.SelectTokens("$..accountItemSectionRenderer"))
                {
                    string sectionTitle = Text(itemSection.SelectToken("header.accountItemSectionHeaderRenderer.title"));
                    foreach (var item in itemSection.SelectTokens("$..accountItem"))
                    {
                        var acc = ParseAccountItem(item, email ?? (sectionTitle != null && sectionTitle.Contains("@") ? sectionTitle : null));
                        if (acc != null && !list.Any(a => a.Key == acc.Key)) list.Add(acc);
                    }
                }
            }
            return list;
        }

        private static YouTubeAccount ParseAccountItem(JToken item, string email)
        {
            if (item == null) return null;
            if (item["isDisabled"] != null && item["isDisabled"].Type == JTokenType.Boolean && (bool)item["isDisabled"]) return null;

            string name = Text(item["accountName"]);
            if (string.IsNullOrEmpty(name)) return null;

            var tokens = item.SelectToken("serviceEndpoint.selectActiveIdentityEndpoint.supportedTokens") as JArray;
            string pageId = item["onBehalfOfParameter"]?.ToString();
            string signinUrl = null, gaiaId = null;
            if (tokens != null)
            {
                foreach (var t in tokens)
                {
                    if (string.IsNullOrEmpty(pageId)) pageId = t.SelectToken("pageIdToken.pageId")?.ToString();
                    if (signinUrl == null) signinUrl = t.SelectToken("accountSigninToken.signinUrl")?.ToString();
                    if (gaiaId == null) gaiaId = t.SelectToken("accountStateToken.obfuscatedGaiaId")?.ToString();
                }
            }

            int authUser = 0;
            if (signinUrl != null)
            {
                var m = _authUserRegex.Match(signinUrl);
                if (m.Success) int.TryParse(m.Groups[1].Value, out authUser);
            }

            string avatar = null;
            var thumbs = item.SelectToken("accountPhoto.thumbnails") as JArray;
            if (thumbs != null && thumbs.Count > 0) avatar = thumbs[thumbs.Count - 1]["url"]?.ToString();
            if (avatar != null && avatar.StartsWith("//")) avatar = "https:" + avatar;

            return new YouTubeAccount
            {
                Name = name,
                Handle = Text(item["channelHandle"]) ?? Text(item["accountByline"]),
                Email = email,
                AvatarUrl = avatar,
                AuthUser = authUser,
                PageId = string.IsNullOrEmpty(pageId) ? null : pageId,
                GaiaId = gaiaId,
                IsSelected = item["isSelected"] != null && item["isSelected"].Type == JTokenType.Boolean && (bool)item["isSelected"]
            };
        }

        /// <summary>Text of a {simpleText} or {runs:[{text}]} node.</summary>
        private static string Text(JToken node)
        {
            if (node == null) return null;
            if (node.Type == JTokenType.String) return node.ToString();
            string s = node["simpleText"]?.ToString();
            if (!string.IsNullOrEmpty(s)) return s;
            var runs = node["runs"] as JArray;
            if (runs == null) return null;
            string joined = string.Concat(runs.Select(r => r["text"]?.ToString() ?? ""));
            return joined.Length > 0 ? joined : null;
        }
    }
}
