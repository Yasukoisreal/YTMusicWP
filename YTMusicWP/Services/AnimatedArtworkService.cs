using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Animated album artwork ("motion artwork") from the Apple Music catalog, shown looping and muted in place of
    /// the cover in Now Playing. No account is needed: the web player's public bearer token is read from its JS
    /// bundle. Each variant of an artwork is an HLS playlist of byte ranges over ONE fragmented MP4, which the
    /// WP8.1 MediaElement plays straight from its URL (it cannot play HLS itself).
    /// Results are cached per videoId, including "no artwork", so a song is looked up once.
    /// </summary>
    public static class AnimatedArtworkService
    {
        private const string TokenKey = "AmWebToken";
        private const string TokenExpiryKey = "AmWebTokenExpiry";
        private const string CacheFile = "animated_artwork_cache.json";
        private const int MaxCacheEntries = 400;

        private static readonly HttpClient _http = CreateClient();
        private static Dictionary<string, string> _cache;
        private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        private static string _token;

        private static long NowUnixSeconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            try { handler.UseCookies = false; } catch { }
            try
            {
                if (handler.SupportsAutomaticDecompression)
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            }
            catch { }
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
            return client;
        }

        /// <summary>User setting; defaults to off on 512 MB devices (the video costs ~15-20 MB while it plays).</summary>
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    var s = ApplicationData.Current.LocalSettings.Values;
                    return s.ContainsKey("AnimatedArtwork") ? (bool)s["AnimatedArtwork"] : !MemoryHelper.IsLowMemoryDevice;
                }
                catch { return false; }
            }
            set
            {
                try { ApplicationData.Current.LocalSettings.Values["AnimatedArtwork"] = value; } catch { }
            }
        }

        /// <summary>
        /// URL of a square, looping MP4 for the track's album, or null when there is none (or no confident match).
        /// </summary>
        public static async Task<string> GetVideoUrlAsync(YouTubeTrack track, CancellationToken ct)
        {
            if (track == null || string.IsNullOrEmpty(track.VideoId) || track.VideoId.StartsWith("LOCAL:")) return null;
            await LoadCacheAsync();
            string cached;
            if (_cache.TryGetValue(track.VideoId, out cached)) return cached.Length > 0 ? cached : null;

            string url = null;
            try
            {
                url = await LookUpAsync(track, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Network trouble: do not remember "none", try again next time
                Debug.WriteLine("[AnimatedArtwork] lookup failed: " + ex.Message);
                return null;
            }
            Debug.WriteLine("[AnimatedArtwork] '" + track.Title + "' -> " + (url ?? "none"));
            await RememberAsync(track.VideoId, url ?? "");
            return url;
        }

        /// <summary>Drops a cached URL that failed to play, so the next play looks it up again.</summary>
        public static async Task ForgetAsync(string videoId)
        {
            if (string.IsNullOrEmpty(videoId)) return;
            await LoadCacheAsync();
            if (_cache.Remove(videoId)) await SaveCacheAsync();
        }

        // ── Lookup ───────────────────────────────────────────────────────────────────────────────────────────

        private static async Task<string> LookUpAsync(YouTubeTrack track, CancellationToken ct)
        {
            string artist = PrimaryArtist(track.ChannelName);
            string title = CleanTitle(track.Title);
            if (string.IsNullOrEmpty(title)) return null;

            JObject json = await SearchAsync(title + " " + artist, ct);
            if (json == null) return null;
            var songs = json.SelectToken("resources.songs") as JObject;
            var albums = json.SelectToken("resources.albums") as JObject;
            if (songs == null || albums == null) return null;

            string master = null;
            string matchedAlbum = null;
            foreach (var song in songs.Properties().Select(p => p.Value))
            {
                var attrs = song["attributes"];
                if (attrs == null) continue;
                // Only a confident match: the wrong animated cover is worse than the still one
                if (!SameTitle((string)attrs["name"], title) || !SameArtist((string)attrs["artistName"], artist)) continue;

                JToken album = null;
                string albumId = (string)song.SelectToken("relationships.albums.data[0].id");
                if (albumId != null) album = albums[albumId];
                if (album == null)
                {
                    string albumName = (string)attrs["albumName"];
                    album = albums.Properties().Select(p => p.Value).FirstOrDefault(a => (string)a.SelectToken("attributes.name") == albumName);
                }
                var ev = album?.SelectToken("attributes.editorialVideo");
                if (ev == null) continue;
                // Square cuts only: they replace a square cover
                master = (string)ev.SelectToken("motionDetailSquare.video") ?? (string)ev.SelectToken("motionSquareVideo1x1.video");
                if (master != null)
                {
                    matchedAlbum = (string)album.SelectToken("attributes.name");
                    break;
                }
            }
            if (master == null) return null;
            Debug.WriteLine("[AnimatedArtwork] '" + track.Title + "' -> album '" + matchedAlbum + "'");
            return await ResolveMp4Async(master, ct);
        }

        private static async Task<JObject> SearchAsync(string term, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string token = await GetTokenAsync(ct, forceRefresh: attempt > 0);
                if (token == null) return null;
                string url = "https://amp-api-edge.music.apple.com/v1/catalog/us/search"
                    + "?term=" + Uri.EscapeDataString(term)
                    + "&types=songs&include%5Bsongs%5D=albums&format%5Bresources%5D=map&extend=editorialVideo&l=en-US&limit=5&platform=web";
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                    req.Headers.TryAddWithoutValidation("Origin", "https://music.apple.com");
                    req.Headers.TryAddWithoutValidation("Referer", "https://music.apple.com/");
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");
                    using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        if ((int)resp.StatusCode == 401) continue; // expired token: refresh once
                        if (!resp.IsSuccessStatusCode) return null;
                        using (var stream = await resp.Content.ReadAsStreamAsync())
                        using (var reader = new StreamReader(stream))
                        using (var jr = new JsonTextReader(reader))
                            return JObject.Load(jr);
                    }
                }
            }
            return null;
        }

        /// <summary>Master playlist -> narrowest H.264 rendition that fills the cover -> the fMP4 its byte ranges point into.</summary>
        private static async Task<string> ResolveMp4Async(string masterUrl, CancellationToken ct)
        {
            string master = await GetStringAsync(masterUrl, ct);
            var variants = new List<Tuple<string, int, int>>(); // uri, width, bandwidth
            foreach (Match m in Regex.Matches(master, @"#EXT-X-STREAM-INF:([^\n]*)\r?\n(\S+)"))
            {
                string attrs = m.Groups[1].Value;
                var codecs = Regex.Match(attrs, "CODECS=\"([^\"]+)\"");
                var res = Regex.Match(attrs, @"RESOLUTION=(\d+)x\d+");
                var bw = Regex.Match(attrs, @"AVERAGE-BANDWIDTH=(\d+)");
                if (!bw.Success) bw = Regex.Match(attrs, @"(?:^|,)BANDWIDTH=(\d+)");
                // H.264 only: the HEVC renditions are 10-bit, which the phone cannot decode
                if (!codecs.Success || !codecs.Groups[1].Value.StartsWith("avc1") || !res.Success || !bw.Success) continue;
                variants.Add(Tuple.Create(Resolve(masterUrl, m.Groups[2].Value), int.Parse(res.Groups[1].Value), int.Parse(bw.Groups[1].Value)));
            }
            if (variants.Count == 0) return null;

            int minWidth = MemoryHelper.IsLowMemoryDevice ? 360 : 480;
            var wide = variants.Where(v => v.Item2 >= minWidth).ToList();
            int width = wide.Count > 0 ? wide.Min(v => v.Item2) : variants.Max(v => v.Item2);
            var pick = variants.Where(v => v.Item2 == width).OrderBy(v => v.Item3).First();

            string media = await GetStringAsync(pick.Item1, ct);
            if (!media.Contains("#EXT-X-BYTERANGE")) return null; // separate segment files: MediaElement cannot join them
            var files = media.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => Resolve(pick.Item1, l)).Distinct().ToList();
            return files.Count == 1 ? files[0] : null;
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using (var resp = await _http.GetAsync(url, ct))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync();
            }
        }

        private static string Resolve(string baseUrl, string uri)
        {
            if (uri.StartsWith("http://") || uri.StartsWith("https://")) return uri;
            return new Uri(new Uri(baseUrl), uri).ToString();
        }

        // ── Matching ─────────────────────────────────────────────────────────────────────────────────────────

        private static string PrimaryArtist(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return "";
            string first = Regex.Split(channel, @",| & | x | feat\.? | ft\.? ", RegexOptions.IgnoreCase)[0];
            return Regex.Replace(first, @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase).Trim();
        }

        /// <summary>Title without "(feat. ...)", "[Official Video]", "- Remastered 2011" and the like.</summary>
        private static string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string t = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", "");
            t = Regex.Replace(t, @"\s+-\s+.*$", "");
            return t.Trim();
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = CleanTitle(s).ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        private static bool SameTitle(string candidate, string title)
        {
            string a = Normalize(candidate), b = Normalize(title);
            return a.Length > 0 && a == b;
        }

        private static bool SameArtist(string candidate, string artist)
        {
            string a = Normalize(candidate), b = Normalize(artist);
            if (a.Length == 0 || b.Length == 0) return false;
            return a.Contains(b) || b.Contains(a);
        }

        // ── Token (JWT of the web player; lives ~2 months) ───────────────────────────────────────────────────

        private static async Task<string> GetTokenAsync(CancellationToken ct, bool forceRefresh)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (!forceRefresh)
            {
                if (_token != null) return _token;
                try
                {
                    if (settings.ContainsKey(TokenKey) && settings.ContainsKey(TokenExpiryKey)
                        && (long)settings[TokenExpiryKey] > NowUnixSeconds() + 3600)
                        return _token = (string)settings[TokenKey];
                }
                catch { }
            }

            _token = null;
            string bundlePath = await ScanForAsync("https://music.apple.com", new Regex(@"/assets/index~[^/""']+\.js"), null, ct);
            if (bundlePath == null) return null;
            string token = await ScanForAsync("https://music.apple.com" + bundlePath,
                new Regex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+"), IsWebPlayToken, ct);
            if (token == null) return null;
            try
            {
                settings[TokenKey] = token;
                settings[TokenExpiryKey] = TokenExpiry(token);
            }
            catch { }
            return _token = token;
        }

        /// <summary>
        /// Reads a text response in 32 KB chunks and returns the first match accepted by <paramref name="accept"/>, then
        /// drops the rest of the download (the home page is ~2.3 MB and the bundle ~3 MB; the hits come early).
        /// </summary>
        private static async Task<string> ScanForAsync(string url, Regex pattern, Func<string, bool> accept, CancellationToken ct)
        {
            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            using (var stream = await resp.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                var buffer = new char[32 * 1024];
                string carry = "";
                int read;
                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    string window = carry + new string(buffer, 0, read);
                    foreach (Match m in pattern.Matches(window))
                    {
                        if (m.Index + m.Length >= window.Length) continue; // may be cut off; the next chunk completes it
                        if (accept == null || accept(m.Value)) return m.Value;
                    }
                    carry = window.Length > 4096 ? window.Substring(window.Length - 4096) : window;
                }
                return null;
            }
        }

        private static JObject JwtPayload(string jwt)
        {
            string payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            while (payload.Length % 4 != 0) payload += "=";
            byte[] bytes = Convert.FromBase64String(payload);
            return JObject.Parse(Encoding.UTF8.GetString(bytes, 0, bytes.Length));
        }

        private static bool IsWebPlayToken(string jwt)
        {
            try { return (string)JwtPayload(jwt)["iss"] == "AMPWebPlay"; }
            catch { return false; }
        }

        private static long TokenExpiry(string jwt)
        {
            try { return (long)JwtPayload(jwt)["exp"]; }
            catch { return NowUnixSeconds() + 86400; }
        }

        // ── Cache (videoId -> mp4 url, "" = none) ────────────────────────────────────────────────────────────

        private static async Task LoadCacheAsync()
        {
            if (_cache != null) return;
            await _cacheLock.WaitAsync();
            try
            {
                if (_cache != null) return;
                var cache = new Dictionary<string, string>();
                try
                {
                    var file = await ApplicationData.Current.LocalFolder.GetFileAsync(CacheFile);
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(await FileIO.ReadTextAsync(file));
                    if (loaded != null) cache = loaded;
                }
                catch { }
                _cache = cache;
            }
            finally { _cacheLock.Release(); }
        }

        private static async Task RememberAsync(string videoId, string url)
        {
            if (_cache.Count >= MaxCacheEntries) _cache.Clear(); // crude bound; lookups are cheap to redo
            _cache[videoId] = url;
            await SaveCacheAsync();
        }

        private static async Task SaveCacheAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(CacheFile, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, JsonConvert.SerializeObject(_cache));
            }
            catch { }
            finally { _cacheLock.Release(); }
        }
    }
}
