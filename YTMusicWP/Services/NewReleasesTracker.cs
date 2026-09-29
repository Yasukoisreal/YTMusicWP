using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Finds new albums and singles of followed artists. Each app start looks at a few artists (the ones unchecked the
    /// longest), so the whole list is covered over a few launches without loading a dozen artist pages at once on a
    /// 512 MB phone. The first look at an artist only records what it already has; later looks report what is new.
    /// State lives in LocalFolder\new_releases.json (built by hand: Newtonsoft object mapping throws on WP8.1).
    /// </summary>
    public static class NewReleasesTracker
    {
        private const string StateFile = "new_releases.json";
        private const int ArtistsPerRun = 5;
        private const int MaxKnownPerArtist = 200;

        private static readonly System.Threading.SemaphoreSlim _gate = new System.Threading.SemaphoreSlim(1, 1);

        /// <summary>Releases found in the last 30 days, newest first, without touching the network.</summary>
        public static async Task<List<ReleaseInfo>> GetRecentAsync()
        {
            var state = await LoadAsync();
            return NewReleaseRules.Recent(state.Found, DateTime.Now);
        }

        /// <summary>
        /// Checks the artists that are due. Returns the releases found by this run (empty when nothing is new or the
        /// check was skipped).
        /// </summary>
        public static async Task<List<ReleaseInfo>> CheckAsync(IList<KeyValuePair<string, string>> artists)
        {
            var foundNow = new List<ReleaseInfo>();
            if (artists == null || artists.Count == 0) return foundNow;
            if (!await _gate.WaitAsync(0)) return foundNow;
            try
            {
                var state = await LoadAsync();
                var now = DateTime.Now;
                var lastChecked = state.Artists.ToDictionary(a => a.Key, a => a.Value.Checked);
                var names = artists.GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.First().Value);

                foreach (var channelId in NewReleaseRules.PickArtistsToCheck(artists.Select(a => a.Key), lastChecked, now, ArtistsPerRun))
                {
                    ArtistResult page;
                    try { page = await InnerTubeClient.BrowseArtistAsync(channelId); }
                    catch { continue; }
                    if (page == null || page.Albums == null) continue; // offline: try again next time

                    string artistName = !string.IsNullOrEmpty(page.Name) ? page.Name : names[channelId];
                    var releases = page.Albums
                        .Where(a => NewReleaseRules.IsRelease(a.BrowseId))
                        .Select(a => new ReleaseInfo
                        {
                            BrowseId = a.BrowseId,
                            Title = a.Title,
                            Artist = artistName,
                            ChannelId = channelId,
                            ThumbnailUrl = a.ThumbnailUrl,
                            Subtitle = a.Subtitle,
                            FoundAt = now
                        })
                        .ToList();
                    ArtistState artistState;
                    if (!state.Artists.TryGetValue(channelId, out artistState))
                    {
                        artistState = new ArtistState();
                        state.Artists[channelId] = artistState;
                    }
                    artistState.Checked = now;
                    // An empty page (a channel without albums, or a failed parse) sets no baseline: otherwise its whole
                    // catalogue would look new the next time it loads
                    if (releases.Count == 0) continue;

                    if (artistState.HasBaseline)
                    {
                        var fresh = NewReleaseRules.FindNew(releases, artistState.Known, now.Year);
                        foundNow.AddRange(fresh);
                        state.Found.AddRange(fresh);
                    }

                    artistState.HasBaseline = true;
                    foreach (var r in releases) artistState.Known.Add(r.BrowseId);
                    if (artistState.Known.Count > MaxKnownPerArtist)
                        artistState.Known = new HashSet<string>(releases.Select(r => r.BrowseId));
                }

                // Artists no longer followed, and releases past the Home window, are forgotten
                var followed = new HashSet<string>(artists.Select(a => a.Key));
                foreach (var gone in state.Artists.Keys.Where(k => !followed.Contains(k)).ToList()) state.Artists.Remove(gone);
                state.Found = NewReleaseRules.Recent(state.Found, now);

                await SaveAsync(state);
            }
            catch { }
            finally
            {
                _gate.Release();
            }
            return foundNow;
        }

        private sealed class ArtistState
        {
            public DateTime Checked;
            /// <summary>Its releases have been recorded once, so anything not in <see cref="Known"/> is new.</summary>
            public bool HasBaseline;
            public HashSet<string> Known = new HashSet<string>();
        }

        private sealed class State
        {
            public Dictionary<string, ArtistState> Artists = new Dictionary<string, ArtistState>();
            public List<ReleaseInfo> Found = new List<ReleaseInfo>();
        }

        private static async Task<State> LoadAsync()
        {
            var state = new State();
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(StateFile);
                var root = JObject.Parse(await FileIO.ReadTextAsync(file));

                var artists = root["artists"] as JObject;
                if (artists != null)
                {
                    foreach (var prop in artists.Properties())
                    {
                        var a = new ArtistState
                        {
                            Checked = ReadTime(prop.Value["checked"]),
                            HasBaseline = prop.Value["baseline"] != null && prop.Value["baseline"].Type == JTokenType.Boolean && prop.Value["baseline"].Value<bool>()
                        };
                        var known = prop.Value["known"] as JArray;
                        if (known != null) foreach (var id in known) a.Known.Add(id.ToString());
                        state.Artists[prop.Name] = a;
                    }
                }

                var found = root["found"] as JArray;
                if (found != null)
                {
                    foreach (var f in found)
                    {
                        state.Found.Add(new ReleaseInfo
                        {
                            BrowseId = f["browseId"]?.ToString(),
                            Title = f["title"]?.ToString(),
                            Artist = f["artist"]?.ToString(),
                            ChannelId = f["channelId"]?.ToString(),
                            ThumbnailUrl = f["thumbnail"]?.ToString(),
                            Subtitle = f["subtitle"]?.ToString(),
                            FoundAt = ReadTime(f["foundAt"])
                        });
                    }
                }
            }
            catch { } // no file yet, or unreadable: start fresh
            return state;
        }

        private static async Task SaveAsync(State state)
        {
            var artists = new JObject();
            foreach (var a in state.Artists)
            {
                artists[a.Key] = new JObject
                {
                    ["checked"] = FormatTime(a.Value.Checked),
                    ["baseline"] = a.Value.HasBaseline,
                    ["known"] = new JArray(a.Value.Known.ToArray())
                };
            }
            var found = new JArray();
            foreach (var r in state.Found)
            {
                found.Add(new JObject
                {
                    ["browseId"] = r.BrowseId,
                    ["title"] = r.Title,
                    ["artist"] = r.Artist,
                    ["channelId"] = r.ChannelId,
                    ["thumbnail"] = r.ThumbnailUrl,
                    ["subtitle"] = r.Subtitle,
                    ["foundAt"] = FormatTime(r.FoundAt)
                });
            }
            var root = new JObject { ["artists"] = artists, ["found"] = found };

            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(StateFile, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, root.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static string FormatTime(DateTime t)
        {
            return t.ToString("o", CultureInfo.InvariantCulture);
        }

        /// <summary>JObject.Parse turns ISO strings into Date tokens, whose ToString() is culture-formatted: read both.</summary>
        private static DateTime ReadTime(JToken token)
        {
            if (token == null) return DateTime.MinValue;
            if (token.Type == JTokenType.Date) return token.Value<DateTime>();
            DateTime t;
            return DateTime.TryParse(token.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t) ? t : DateTime.MinValue;
        }
    }
}
