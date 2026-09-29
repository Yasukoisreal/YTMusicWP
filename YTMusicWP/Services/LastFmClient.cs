using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Last.fm web sign-in, "now playing" and scrobbling. Scrobbles come from the listening log the audio task writes and
    /// are sent after the fact (Last.fm accepts them for two weeks), so the audio task, already close to its memory cap
    /// during livestreams, does no Last.fm work.
    /// </summary>
    public static class LastFmClient
    {
        private const string ApiUrl = "https://ws.audioscrobbler.com/2.0/";
        private const string AuthUrl = "https://www.last.fm/api/auth/";
        /// <summary>Where Last.fm sends the browser after the user approves; the auth broker stops there (nothing loads it).</summary>
        public const string CallbackUrl = "https://localhost/ytmusicwp-lastfm";

        private const string SessionKeySetting = "LastFmSessionKey";
        private const string UserNameSetting = "LastFmUserName";
        private const string ScrobbledLinesSetting = "LastFmScrobbledLines";
        private const int BatchSize = 50;
        private const int InvalidSessionError = 9;

        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly SemaphoreSlim _scrobbleGate = new SemaphoreSlim(1, 1);

        /// <summary>This build has API credentials (see LastFmCredentials).</summary>
        public static bool IsAvailable { get { return LastFmCredentials.IsAvailable; } }

        public static bool IsSignedIn { get { return IsAvailable && !string.IsNullOrEmpty(Setting(SessionKeySetting)); } }

        public static string UserName { get { return Setting(UserNameSetting); } }

        public static Uri AuthUri
        {
            get { return new Uri(AuthUrl + "?api_key=" + Uri.EscapeDataString(LastFmCredentials.ApiKey) + "&cb=" + Uri.EscapeDataString(CallbackUrl)); }
        }

        /// <summary>
        /// Finishes the web sign-in with the callback URL (it carries ?token=). Plays logged before this point are not
        /// scrobbled: only listening from now on goes to Last.fm.
        /// </summary>
        public static async Task<bool> CompleteSignInAsync(string callbackUrl)
        {
            string token = QueryValue(callbackUrl, "token");
            if (string.IsNullOrEmpty(token)) return false;

            var json = await CallAsync(new Dictionary<string, string> { { "method", "auth.getSession" }, { "token", token } }, false);
            string key = json?["session"]?["key"]?.ToString();
            if (string.IsNullOrEmpty(key)) return false;

            var lines = await ReadLogAsync();
            var ls = ApplicationData.Current.LocalSettings.Values;
            ls[SessionKeySetting] = key;
            ls[UserNameSetting] = json["session"]?["name"]?.ToString() ?? "";
            ls[ScrobbledLinesSetting] = lines != null ? lines.Count : 0;
            return true;
        }

        public static void SignOut()
        {
            var ls = ApplicationData.Current.LocalSettings.Values;
            ls.Remove(SessionKeySetting);
            ls.Remove(UserNameSetting);
            ls.Remove(ScrobbledLinesSetting);
        }

        /// <summary>Shows the track as playing now on the user's profile (not a scrobble).</summary>
        public static async Task UpdateNowPlayingAsync(string title, string artist, double durationSeconds)
        {
            if (!IsSignedIn || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return;
            var p = new Dictionary<string, string>
            {
                { "method", "track.updateNowPlaying" },
                { "artist", LastFmRules.CleanArtist(artist) },
                { "track", LastFmRules.CleanTitle(title, artist) },
                { "sk", Setting(SessionKeySetting) }
            };
            if (durationSeconds > 30) p["duration"] = ((int)durationSeconds).ToString(CultureInfo.InvariantCulture);
            HandleResult(await CallAsync(p, true));
        }

        /// <summary>Sends the plays logged since the last run, in batches of 50. Safe to call often.</summary>
        public static async Task ScrobblePendingAsync()
        {
            if (!IsSignedIn) return;
            if (!await _scrobbleGate.WaitAsync(0)) return; // a run is already sending
            try
            {
                var lines = await ReadLogAsync();
                if (lines == null) return;

                int done = SettingInt(ScrobbledLinesSetting);
                if (done > lines.Count)
                {
                    // The log was cleared: start over from its end, never resend old plays
                    SaveScrobbledLines(lines.Count);
                    return;
                }

                var now = DateTime.Now;
                var plays = ListeningStatsCalculator.Parse(lines).Where(p => p.Line >= done && LastFmRules.IsScrobblable(p, now)).ToList();
                for (int start = 0; start < plays.Count; start += BatchSize)
                {
                    var batch = plays.Skip(start).Take(BatchSize).ToList();
                    var p = new Dictionary<string, string> { { "method", "track.scrobble" }, { "sk", Setting(SessionKeySetting) } };
                    for (int i = 0; i < batch.Count; i++)
                    {
                        string n = "[" + i + "]";
                        p["artist" + n] = LastFmRules.CleanArtist(batch[i].Artist);
                        p["track" + n] = LastFmRules.CleanTitle(batch[i].Title, batch[i].Artist);
                        p["timestamp" + n] = LastFmRules.UnixTime(batch[i].Start).ToString(CultureInfo.InvariantCulture);
                        p["duration" + n] = batch[i].Duration.ToString(CultureInfo.InvariantCulture);
                    }

                    var json = await CallAsync(p, true);
                    if (json == null) return; // offline: this batch goes out on a later run
                    if (!HandleResult(json)) return;
                    SaveScrobbledLines(batch[batch.Count - 1].Line + 1);
                }
                // Lines that were not scrobblable (skips, livestreams) are done too
                SaveScrobbledLines(lines.Count);
            }
            catch { }
            finally
            {
                _scrobbleGate.Release();
            }
        }

        /// <summary>True when the call worked. A revoked session signs out; other errors keep the plays for a later run.</summary>
        private static bool HandleResult(JObject json)
        {
            if (json == null) return false;
            var error = json["error"];
            if (error == null) return true;
            if (error.Type == JTokenType.Integer && error.Value<int>() == InvalidSessionError) SignOut();
            return false;
        }

        private static async Task<JObject> CallAsync(Dictionary<string, string> parameters, bool post)
        {
            parameters["api_key"] = LastFmCredentials.ApiKey;
            parameters["api_sig"] = Md5(LastFmRules.SignatureBase(parameters, LastFmCredentials.Secret));
            parameters["format"] = "json";
            try
            {
                HttpResponseMessage resp;
                if (post)
                {
                    resp = await _client.PostAsync(ApiUrl, new FormUrlEncodedContent(parameters)).ConfigureAwait(false);
                }
                else
                {
                    string query = string.Join("&", parameters.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
                    resp = await _client.GetAsync(ApiUrl + "?" + query).ConfigureAwait(false);
                }
                using (resp)
                {
                    // Errors come back as JSON too ({"error": 9, "message": ...}), with a 4xx status
                    return JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
            catch
            {
                return null;
            }
        }

        private static async Task<IList<string>> ReadLogAsync()
        {
            // The audio task may be appending right now: one retry covers the brief sharing violation
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var file = await ApplicationData.Current.LocalFolder.GetFileAsync(ListeningStatsCalculator.LogFileName);
                    return await FileIO.ReadLinesAsync(file);
                }
                catch (System.IO.FileNotFoundException)
                {
                    return new List<string>();
                }
                catch
                {
                    if (attempt == 0) await Task.Delay(300);
                }
            }
            return null;
        }

        private static string Md5(string text)
        {
            var md5 = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
            var hash = md5.HashData(CryptographicBuffer.ConvertStringToBinary(text, BinaryStringEncoding.Utf8));
            return CryptographicBuffer.EncodeToHexString(hash);
        }

        private static string QueryValue(string url, string name)
        {
            try
            {
                var uri = new Uri(url);
                if (string.IsNullOrEmpty(uri.Query)) return null;
                return new WwwFormUrlDecoder(uri.Query.TrimStart('?')).GetFirstValueByName(name);
            }
            catch
            {
                return null;
            }
        }

        private static string Setting(string key)
        {
            object value;
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out value) ? value as string : null;
        }

        private static int SettingInt(string key)
        {
            object value;
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out value) && value is int ? (int)value : 0;
        }

        private static void SaveScrobbledLines(int lines)
        {
            ApplicationData.Current.LocalSettings.Values[ScrobbledLinesSetting] = lines;
        }
    }
}
