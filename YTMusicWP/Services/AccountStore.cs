using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace YTMusicWP.Services
{
    /// <summary>
    /// The YouTube identities the user has added (<see cref="YouTubeAccount"/>), each with the cookie session it
    /// belongs to. Kept in a file rather than LocalSettings: a cookie string alone is several KB and LocalSettings
    /// values are limited to 8 KB. The active identity is also mirrored into the GoogleCookieString /
    /// GoogleSAPISID / GoogleAuthUser / GooglePageId settings that InnerTubeClient loads at startup.
    /// </summary>
    public static class AccountStore
    {
        private const string FileName = "yt_accounts.json";
        private const string ActiveKeySetting = "ActiveAccountKey";

        private static List<YouTubeAccount> _accounts = new List<YouTubeAccount>();
        private static bool _loaded;

        public static IReadOnlyList<YouTubeAccount> Accounts { get { return _accounts; } }

        public static string ActiveKey
        {
            get
            {
                try
                {
                    var s = ApplicationData.Current.LocalSettings.Values;
                    return s.ContainsKey(ActiveKeySetting) ? s[ActiveKeySetting] as string : null;
                }
                catch { return null; }
            }
            set
            {
                try
                {
                    var s = ApplicationData.Current.LocalSettings.Values;
                    if (value == null) s.Remove(ActiveKeySetting); else s[ActiveKeySetting] = value;
                }
                catch { }
            }
        }

        public static YouTubeAccount Active
        {
            get
            {
                string key = ActiveKey;
                return key == null ? null : _accounts.FirstOrDefault(a => a.Key == key);
            }
        }

        public static async Task LoadAsync()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName);
                string json = await FileIO.ReadTextAsync(file);
                var list = new List<YouTubeAccount>();
                foreach (var t in JArray.Parse(json))
                {
                    var o = t as JObject;
                    if (o == null) continue;
                    var a = new YouTubeAccount
                    {
                        Name = (string)o["name"],
                        Handle = (string)o["handle"],
                        Email = (string)o["email"],
                        AvatarUrl = (string)o["avatar"],
                        AuthUser = (int?)o["authUser"] ?? 0,
                        PageId = (string)o["pageId"],
                        GaiaId = (string)o["gaiaId"],
                        CookieString = (string)o["cookie"],
                        Sapisid = (string)o["sapisid"]
                    };
                    if (!string.IsNullOrEmpty(a.CookieString) && !string.IsNullOrEmpty(a.Sapisid)) list.Add(a);
                }
                _accounts = list;
            }
            catch { }
        }

        public static async Task SaveAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
                // Built by hand: Newtonsoft's reflection-based serialization of app types throws
                // TypeAccessException on Windows Phone 8.1
                var array = new JArray();
                foreach (var a in _accounts)
                {
                    array.Add(new JObject
                    {
                        ["name"] = a.Name,
                        ["handle"] = a.Handle,
                        ["email"] = a.Email,
                        ["avatar"] = a.AvatarUrl,
                        ["authUser"] = a.AuthUser,
                        ["pageId"] = a.PageId,
                        ["gaiaId"] = a.GaiaId,
                        ["cookie"] = a.CookieString,
                        ["sapisid"] = a.Sapisid
                    });
                }
                await FileIO.WriteTextAsync(file, array.ToString(Formatting.None));
            }
            catch { }
        }

        /// <summary>Adds identities, or refreshes the stored copy (name, avatar, cookie) of ones already known.</summary>
        public static void Merge(IEnumerable<YouTubeAccount> accounts)
        {
            foreach (var a in accounts)
            {
                int i = _accounts.FindIndex(x => x.Key == a.Key);
                if (i >= 0) _accounts[i] = a; else _accounts.Add(a);
            }
        }

        public static void Remove(YouTubeAccount account)
        {
            _accounts.RemoveAll(a => a.Key == account.Key);
            if (ActiveKey == account.Key) ActiveKey = null;
        }

        public static async Task ClearAsync()
        {
            _accounts.Clear();
            ActiveKey = null;
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName);
                await file.DeleteAsync();
            }
            catch { }
        }
    }
}
