using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        // [OPT] Cached brushes for login status text
        private static readonly SolidColorBrush _authGrayBrush = new SolidColorBrush(Windows.UI.Colors.Gray);
        private static readonly SolidColorBrush _authOrangeBrush = new SolidColorBrush(Windows.UI.Colors.Orange);
        private static readonly SolidColorBrush _authRedBrush = new SolidColorBrush(Windows.UI.Colors.Red);

        // Built-in OAuth credentials (YouTube TV public client — used by NewPipe, yt-dlp, etc.)
        private const string _builtInClientId = "861556708454-d6dlm3lh05idd8npek18k6be8ba3oc68.apps.googleusercontent.com";
        private const string _builtInClientSecret = "SboVhoG9s0rNafixCSGGKXAT";
        private string DetectOsRegion()
        {
            try
            {
                var region = new Windows.Globalization.GeographicRegion();
                string code = region.CodeTwoLetter.ToUpper();
                // Validate against supported list
                var supported = new System.Collections.Generic.HashSet<string>
                {
                    "DZ","AR","AU","AT","AZ","BH","BD","BY","BE","BO","BA","BR","BG","CA","CL",
                    "CO","CR","HR","CZ","DK","DO","EC","EG","SV","EE","FI","FR","GE","DE","GH",
                    "GR","GT","HN","HK","HU","IS","IN","ID","IQ","IE","IL","IT","JP","JO","KE",
                    "KW","LV","LB","LT","MK","MY","MX","ME","MA","NL","NZ","NG","NO","OM","PE",
                    "PH","PL","PT","PR","QA","RO","RU","SA","SN","RS","SG","SK","SI","ZA","KR",
                    "ES","SE","CH","TW","TH","TN","TR","UG","UA","AE","GB","US","VN","YE"
                };
                return supported.Contains(code) ? code : "US";
            }
            catch { return "US"; }
        }

        // Safe helpers to prevent crash when upgrading with stale LocalSettings data
        private static int SafeGetInt(Windows.Foundation.Collections.IPropertySet s, string key, int def)
        {
            try { if (s.ContainsKey(key)) return System.Convert.ToInt32(s[key]); } catch { }
            return def;
        }
        private static bool SafeGetBool(Windows.Foundation.Collections.IPropertySet s, string key, bool def)
        {
            try { if (s.ContainsKey(key)) return System.Convert.ToBoolean(s[key]); } catch { }
            return def;
        }
        private static string SafeGetString(Windows.Foundation.Collections.IPropertySet s, string key, string def)
        {
            try { if (s.ContainsKey(key)) return s[key].ToString(); } catch { }
            return def;
        }

        private void LoadSettings()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                ClientIdTextBox.Text = SafeGetString(settings, "GoogleClientId", "");
                ClientSecretTextBox.Text = SafeGetString(settings, "GoogleClientSecret", "");

                if (settings.ContainsKey("TrendingRegion"))
                {
                    string r = settings["TrendingRegion"].ToString();
                    bool found = false;
                    for (int i = 0; i < RegionComboBox.Items.Count; i++)
                    {
                        var tag = ((ComboBoxItem)RegionComboBox.Items[i]).Tag;
                        if (tag != null && tag.ToString() == r)
                        {
                            RegionComboBox.SelectedIndex = i;
                            found = true;
                            break;
                        }
                    }
                    if (!found) RegionComboBox.SelectedIndex = 0; // Fallback: Auto-detect
                }
                else
                {
                    string detected = DetectOsRegion();
                    settings["TrendingRegion"] = detected;
                    RegionComboBox.SelectedIndex = 0;
                }

                if (settings.ContainsKey("GoogleAccessToken"))
                {
                    LoginStatusText.Text = "Status: Logged In & Synced!";
                    LoginStatusText.Foreground = _greenBrush;
                    // Load cached avatar
                    LoadHomeAvatar();
                }

                bool isShuffle = SafeGetBool(settings, "ShuffleMode", false);
                ShuffleIcon.Foreground = isShuffle ? _greenBrush : _whiteBrush;
                int repeatMode = SafeGetInt(settings, "RepeatMode", 0);
                UpdateRepeatUI(repeatMode);

                // Playback settings — set values BEFORE attaching handlers to avoid triggering saves on load
                int quality = SafeGetInt(settings, "AudioQuality", 1);
                if (quality >= 0 && quality < AudioQualityComboBox.Items.Count)
                    AudioQualityComboBox.SelectedIndex = quality;
                int crossfade = SafeGetInt(settings, "CrossfadeSeconds", 0);
                CrossfadeSlider.Value = crossfade;
                CrossfadeValueText.Text = crossfade + "s";
                AutoplayToggle.IsOn = SafeGetBool(settings, "Autoplay", true);
                GaplessToggle.IsOn = SafeGetBool(settings, "GaplessPlayback", true);
                NormalizeVolumeToggle.IsOn = SafeGetBool(settings, "NormalizeVolume", false);

                // Now attach handlers — changes will save & apply immediately
                AudioQualityComboBox.SelectionChanged += AudioQualityComboBox_SelectionChanged;
                CrossfadeSlider.ValueChanged += CrossfadeSlider_ValueChanged;
                AutoplayToggle.Toggled += AutoplayToggle_Toggled;
                GaplessToggle.Toggled += GaplessToggle_Toggled;
                NormalizeVolumeToggle.Toggled += NormalizeVolumeToggle_Toggled;
            }
            catch { }
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Save Settings only handles OAuth + Region
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["GoogleClientId"] = ClientIdTextBox.Text.Trim();
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["GoogleClientSecret"] = ClientSecretTextBox.Text.Trim();

            var selectedRegion = RegionComboBox.SelectedItem as ComboBoxItem;
            if (selectedRegion != null && selectedRegion.Tag != null)
            {
                string regionTag = selectedRegion.Tag.ToString();
                if (regionTag == "AUTO")
                    regionTag = DetectOsRegion();
                ApplicationData.Current.LocalSettings.Values["TrendingRegion"] = regionTag;

                // Update InnerTube region immediately
                InnerTubeClient.SetRegion(regionTag);
            }

            ShowToast("Settings Saved!");

            if (IsInternetAvailable())
            {
                // Clear all home sections
                homeTracks.Clear();
                popTracks.Clear();
                lofiTracks.Clear();
                workoutTracks.Clear();
                genre5Tracks.Clear();
                genre6Tracks.Clear();
                genre7Tracks.Clear();
                genre8Tracks.Clear();
                await LoadHomeRecommendations();
            }
        }

        private void CrossfadeSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (CrossfadeValueText != null)
                CrossfadeValueText.Text = (int)e.NewValue + "s";
            ApplicationData.Current.LocalSettings.Values["CrossfadeSeconds"] = (int)e.NewValue;
        }

        private void AudioQualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var qualityItem = AudioQualityComboBox.SelectedItem as ComboBoxItem;
            ApplicationData.Current.LocalSettings.Values["AudioQuality"] = AudioQualityComboBox.SelectedIndex;
            ApplicationData.Current.LocalSettings.Values["AudioQualityKbps"] = (qualityItem != null && qualityItem.Tag != null) ? qualityItem.Tag.ToString() : "128";
        }

        private void AutoplayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["Autoplay"] = AutoplayToggle.IsOn;
        }

        private void GaplessToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["GaplessPlayback"] = GaplessToggle.IsOn;
        }

        private void NormalizeVolumeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["NormalizeVolume"] = NormalizeVolumeToggle.IsOn;
            try { _appMediaPlayer.Volume = NormalizeVolumeToggle.IsOn ? 0.75 : 1.0; } catch { }
        }

        private async void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var files = await folder.GetFilesAsync();
                int count = 0;
                foreach (var file in files)
                {
                    if (file.Name.EndsWith(".jpg") || file.Name.EndsWith(".png") || file.Name.EndsWith(".webp"))
                    {
                        await file.DeleteAsync();
                        count++;
                    }
                }
                ShowToast("Cleared " + count + " cached files");
            }
            catch { ShowToast("Error clearing cache"); }
        }

        private void LogoutGoogle_Click(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings.Remove("GoogleAccessToken");
            settings.Remove("GoogleRefreshToken");
            settings.Remove("GoogleClientId");
            settings.Remove("GoogleClientSecret");
            settings.Remove("GoogleTokenExpiry");

            _youtubeUserPlaylists.Clear();
            _youtubeSubscriptions.Clear();

            LoginStatusText.Text = "Status: Not Logged In";
            LoginStatusText.Foreground = _authGrayBrush;
            ClientIdTextBox.Text = "";
            ClientSecretTextBox.Text = "";

            // Reset avatar to default
            HomeAvatarImage.Visibility = Visibility.Collapsed;
            HomeAvatarFallback.Visibility = Visibility.Visible;
            HomeAvatarLetter.Text = "Y";
            settings.Remove("GoogleAvatarUrl");
            settings.Remove("GoogleUserName");

            ShowToast("Logged out successfully");
        }

        private async void CopyAuthLink_Click(object sender, RoutedEventArgs e)
        {
            string clientId = ClientIdTextBox.Text.Trim();
            if (string.IsNullOrEmpty(clientId))
            {
                ShowToast("Please enter Client ID first!");
                return;
            }

            string authUrl = "https://accounts.google.com/o/oauth2/v2/auth?" +
                             "client_id=" + Uri.EscapeDataString(clientId) +
                             "&redirect_uri=http://localhost" +
                             "&response_type=code" +
                             "&scope=https://www.googleapis.com/auth/youtube" +
                             "&access_type=offline";

            await Windows.System.Launcher.LaunchUriAsync(new Uri(authUrl));
            ShowToast("Opening browser! After approving on PC, return here.");
        }
        private string _deviceVerificationUrl = "";
        private string _deviceUserCode = "";
        private bool _deviceCodePolling = false;

        private async void LoginGoogle_Click(object sender, RoutedEventArgs e)
        {
            LoginWebContainer.Visibility = Visibility.Visible;
            DeviceCodeText.Text = "----";
            DeviceCodeStatus.Text = "Requesting code...";
            DeviceCodeProgress.Visibility = Visibility.Visible;

            await StartDeviceCodeFlow();
        }

        private async void OpenDeviceBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_deviceVerificationUrl))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(_deviceVerificationUrl));
            }
            else
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.google.com/device"));
            }
        }

        private void CloseLoginWeb_Click(object sender, RoutedEventArgs e)
        {
            LoginWebContainer.Visibility = Visibility.Collapsed;
            _deviceCodePolling = false;
        }

        private async Task StartDeviceCodeFlow()
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", _builtInClientId),
                    new KeyValuePair<string, string>("scope", "https://www.googleapis.com/auth/youtube")
                });

                var response = await _apiClient.PostAsync("https://oauth2.googleapis.com/device/code", content);
                string resultJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(resultJson);
                    string deviceCode = json["device_code"]?.ToString();
                    string userCode = json["user_code"]?.ToString();
                    string verificationUrl = json["verification_url"]?.ToString() ?? "https://www.google.com/device";
                    int expiresIn = json["expires_in"]?.Value<int>() ?? 1800;
                    int interval = json["interval"]?.Value<int>() ?? 5;

                    _deviceUserCode = userCode;
                    _deviceVerificationUrl = verificationUrl;

                    DeviceCodeText.Text = userCode ?? "ERROR";
                    DeviceCodeStatus.Text = "Waiting for you to sign in...";

                    // Start polling for user authorization
                    _deviceCodePolling = true;
                    await PollDeviceCodeAsync(deviceCode, interval, expiresIn);
                }
                else
                {
                    DeviceCodeText.Text = "ERROR";
                    DeviceCodeStatus.Text = "Failed to get code. Try again.";
                    DeviceCodeProgress.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                DeviceCodeText.Text = "ERROR";
                DeviceCodeStatus.Text = "Network error. Check your connection.";
                DeviceCodeProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async Task PollDeviceCodeAsync(string deviceCode, int interval, int expiresIn)
        {
            int elapsed = 0;
            while (_deviceCodePolling && elapsed < expiresIn)
            {
                await Task.Delay(interval * 1000);
                if (!_deviceCodePolling) return;
                elapsed += interval;

                try
                {
                    var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("client_id", _builtInClientId),
                        new KeyValuePair<string, string>("client_secret", _builtInClientSecret),
                        new KeyValuePair<string, string>("device_code", deviceCode),
                        new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
                    });

                    var response = await _apiClient.PostAsync("https://oauth2.googleapis.com/token", content);
                    string resultJson = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(resultJson);

                    if (response.IsSuccessStatusCode)
                    {
                        // Success! Got tokens
                        _deviceCodePolling = false;
                        string accessToken = json["access_token"]?.ToString();
                        string refreshToken = json["refresh_token"]?.ToString() ?? "";

                        var settings = ApplicationData.Current.LocalSettings.Values;
                        settings["GoogleAccessToken"] = accessToken;
                        settings["GoogleRefreshToken"] = refreshToken;
                        long expiresInSec = json["expires_in"]?.Value<long>() ?? 3600;
                        settings["GoogleTokenExpiry"] = DateTimeOffset.UtcNow.AddSeconds(expiresInSec - 60).UtcDateTime.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

                        DeviceCodeStatus.Text = "Success! Syncing...";
                        DeviceCodeProgress.Visibility = Visibility.Collapsed;

                        LoginStatusText.Text = "Status: Logged In & Synced!";
                        LoginStatusText.Foreground = _greenBrush;
                        ShowToast("Login successful! Syncing...");

                        await SyncAllAsync(accessToken);

                        LoginWebContainer.Visibility = Visibility.Collapsed;
                        return;
                    }
                    else
                    {
                        string error = json["error"]?.ToString() ?? "";
                        if (error == "authorization_pending")
                        {
                            // User hasn't approved yet, keep polling
                            continue;
                        }
                        else if (error == "slow_down")
                        {
                            interval += 2; // Increase polling interval
                            continue;
                        }
                        else
                        {
                            // access_denied, expired_token, etc.
                            _deviceCodePolling = false;
                            DeviceCodeStatus.Text = "Login failed: " + error;
                            DeviceCodeProgress.Visibility = Visibility.Collapsed;
                            return;
                        }
                    }
                }
                catch
                {
                    // Network error, retry
                    DeviceCodeStatus.Text = "Network issue, retrying...";
                }
            }

            // Expired
            if (_deviceCodePolling)
            {
                _deviceCodePolling = false;
                DeviceCodeStatus.Text = "Code expired. Please try again.";
                DeviceCodeProgress.Visibility = Visibility.Collapsed;
            }
        }

        // Old WebView handlers (kept for XAML compatibility, no longer used)
        private void LoginWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args) { }
        private void LoginWebView_NavigationFailed(object sender, WebViewNavigationFailedEventArgs e) { }
        private void LoginWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args) { }

        private async Task ProcessGoogleAuthCode(string authCode)
        {
            string clientId = _builtInClientId;
            string clientSecret = _builtInClientSecret;

            LoginStatusText.Text = "Status: Authenticating...";
            LoginStatusText.Foreground = _authOrangeBrush;

            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("code", authCode),
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                    new KeyValuePair<string, string>("redirect_uri", "http://localhost"),
                    new KeyValuePair<string, string>("grant_type", "authorization_code")
                });

                var response = await _apiClient.PostAsync("https://oauth2.googleapis.com/token", content);
                string resultJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(resultJson);
                    string accessToken = json["access_token"]?.ToString();
                    string refreshToken = json["refresh_token"]?.ToString() ?? "";

                    var settings = ApplicationData.Current.LocalSettings.Values;
                    settings["GoogleAccessToken"] = accessToken;
                    settings["GoogleRefreshToken"] = refreshToken;
                    settings["GoogleClientId"] = clientId;
                    settings["GoogleClientSecret"] = clientSecret;
                    // Token expiry: expires_in is seconds (typically 3600)
                    long expiresIn = json["expires_in"]?.Value<long>() ?? 3600;
                    settings["GoogleTokenExpiry"] = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).UtcDateTime.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

                    ShowToast("Login successful! Syncing...");
                    await SyncAllAsync(accessToken);
                }
                else
                {
                    LoginStatusText.Text = "Status: Auth Failed";
                    LoginStatusText.Foreground = _authRedBrush;
                    ShowToast("Auth Error! Please try again.");
                }
            }
            catch
            {
                LoginStatusText.Text = "Status: Network Error";
                LoginStatusText.Foreground = _authRedBrush;
                ShowToast("Network error. Please try again.");
            }
        }

        // ══════════════════════════════════════════
        // AUTHENTICATED INNERTUBE HELPER
        // ══════════════════════════════════════════
        private async Task<JObject> AuthInnerTubePostAsync(string endpoint, JObject extraParams, string accessToken)
        {
            string visitorData = await InnerTubeClient.GetVisitorDataAsync();
            var clientObj = new JObject
            {
                ["clientName"] = "TVHTML5",
                ["clientVersion"] = "7.20241016.00.00",
                ["hl"] = InnerTubeClient.CurrentLanguage,
                ["gl"] = InnerTubeClient.CurrentRegion
            };
            if (!string.IsNullOrEmpty(visitorData))
                clientObj["visitorData"] = visitorData;

            var body = new JObject
            {
                ["context"] = new JObject { ["client"] = clientObj }
            };
            // Merge extra parameters
            foreach (var prop in extraParams.Properties())
                body[prop.Name] = prop.Value;

            // YouTube TV API key — matches the YouTube TV OAuth client
            string url = "https://www.youtube.com/youtubei/v1/" + endpoint + "?key=AIzaSyDCU8hByM-4DrUqRUYnGn-3llEO78bcxq8&prettyPrint=false";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (ChromiumStylePlatform) Cobalt/Version");
            request.Headers.Add("Authorization", "Bearer " + accessToken);

            var response = await _apiClient.SendAsync(request);
            string resultJson = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
                return new JObject { ["_error"] = (int)response.StatusCode, ["_body"] = resultJson.Length > 100 ? resultJson.Substring(0, 100) : resultJson };
            
            return JObject.Parse(resultJson);
        }

        // ══════════════════════════════════════════
        // SYNC LIKED VIDEOS
        // ══════════════════════════════════════════
        private async Task SyncLikedVideosAsync(string accessToken)
        {
            try
            {
                LoginStatusText.Text = "Status: Syncing Liked Songs...";

                var json = await AuthInnerTubePostAsync("browse", new JObject { ["browseId"] = "VLLL" }, accessToken);

                if (json["_error"] != null)
                {
                    LoginStatusText.Text = "Sync " + json["_error"] + ": " + (json["_body"]?.ToString() ?? "");
                    LoginStatusText.Foreground = _authOrangeBrush;
                    return;
                }

                bool hasNew = false;
                // Parse playlist items from InnerTube response
                var contents = json.SelectTokens("$..playlistVideoRenderer");
                foreach (var item in contents)
                {
                    try
                    {
                        string vidId = item["videoId"]?.ToString();
                        if (string.IsNullOrEmpty(vidId)) continue;
                        if (favoriteTracks.Any(t => t.VideoId == vidId)) continue;

                        string title = item.SelectToken("title..text")?.ToString() ?? item["title"]?["simpleText"]?.ToString() ?? "";
                        string channel = item.SelectToken("shortBylineText..text")?.ToString() ?? "";
                        channel = CleanChannelName(System.Net.WebUtility.HtmlDecode(channel));

                        string thumbUrl = item.SelectToken("thumbnail.thumbnails[-1:].url")?.ToString()
                            ?? item.SelectToken("thumbnail.thumbnails[0].url")?.ToString();

                        if (!string.IsNullOrEmpty(title))
                        {
                            favoriteTracks.Insert(0, new YouTubeTrack
                            {
                                VideoId = vidId,
                                Title = System.Net.WebUtility.HtmlDecode(title),
                                ChannelName = channel,
                                ThumbnailUrl = thumbUrl
                            });
                            hasNew = true;
                        }
                    }
                    catch { continue; }
                }

                if (hasNew) SaveFavoritesAsync();

                LoginStatusText.Text = "Status: Logged In & Synced!";
                LoginStatusText.Foreground = _greenBrush;
            }
            catch (Exception ex)
            {
                LoginStatusText.Text = "Sync Error: " + ex.Message;
                LoginStatusText.Foreground = _authRedBrush;
            }
        }

        // ══════════════════════════════════════════
        // SYNC ALL — Called after login and on app resume
        // ══════════════════════════════════════════
        private async Task SyncAllAsync(string accessToken)
        {
            await SyncLikedVideosAsync(accessToken);
            await SyncUserPlaylistsAsync(accessToken);
            await SyncSubscriptionsAsync(accessToken);
            // Fetch YouTube profile avatar
            await FetchAndCacheAvatarAsync(accessToken);
        }

        // ══════════════════════════════════════════
        // GET ACCESS TOKEN — Auto-refresh if expired
        // ══════════════════════════════════════════
        private async Task<string> GetAccessTokenAsync()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (!settings.ContainsKey("GoogleAccessToken")) return null;

            // Check expiry
            if (settings.ContainsKey("GoogleTokenExpiry"))
            {
                double expiry = (double)settings["GoogleTokenExpiry"];
                double now = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                if (now >= expiry)
                {
                    // Token expired → refresh
                    string newToken = await RefreshGoogleTokenAsync();
                    return newToken;
                }
            }
            return settings["GoogleAccessToken"].ToString();
        }

        private async Task<string> RefreshGoogleTokenAsync()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (!settings.ContainsKey("GoogleRefreshToken")) return null;

            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", _builtInClientId),
                    new KeyValuePair<string, string>("client_secret", _builtInClientSecret),
                    new KeyValuePair<string, string>("refresh_token", settings["GoogleRefreshToken"].ToString()),
                    new KeyValuePair<string, string>("grant_type", "refresh_token")
                });

                var response = await _apiClient.PostAsync("https://oauth2.googleapis.com/token", content);
                if (response.IsSuccessStatusCode)
                {
                    string resultJson = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(resultJson);
                    string newToken = json["access_token"]?.ToString();
                    long expiresIn = json["expires_in"]?.Value<long>() ?? 3600;
                    settings["GoogleAccessToken"] = newToken;
                    settings["GoogleTokenExpiry"] = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).UtcDateTime.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    return newToken;
                }
            }
            catch { }
            return null;
        }

        // ══════════════════════════════════════════
        // SYNC USER PLAYLISTS — Import from YouTube
        // ══════════════════════════════════════════
        private ObservableCollection<YouTubePlaylistInfo> _youtubeUserPlaylists = new ObservableCollection<YouTubePlaylistInfo>();

        private async Task SyncUserPlaylistsAsync(string accessToken)
        {
            try
            {
                _youtubeUserPlaylists.Clear();
                var json = await AuthInnerTubePostAsync("browse", new JObject { ["browseId"] = "FElibrary" }, accessToken);
                if (json["_error"] != null) return;

                // Try to find playlists in the library response
                var items = json.SelectTokens("$..gridPlaylistRenderer");
                foreach (var item in items)
                {
                    try
                    {
                        string plId = item["playlistId"]?.ToString();
                        if (string.IsNullOrEmpty(plId)) continue;

                        string title = item.SelectToken("title..text")?.ToString() ?? item["title"]?["simpleText"]?.ToString() ?? "";
                        string countText = item.SelectToken("videoCountShortText..text")?.ToString()
                            ?? item.SelectToken("videoCountText..text")?.ToString() ?? "0";
                        int count = 0;
                        var match = System.Text.RegularExpressions.Regex.Match(countText, @"(\d+)");
                        if (match.Success) int.TryParse(match.Value, out count);

                        string thumbUrl = item.SelectToken("thumbnail.thumbnails[-1:].url")?.ToString()
                            ?? item.SelectToken("thumbnail.thumbnails[0].url")?.ToString();

                        _youtubeUserPlaylists.Add(new YouTubePlaylistInfo
                        {
                            PlaylistId = plId,
                            Title = title,
                            TrackCount = count,
                            ThumbnailUrl = thumbUrl
                        });

                        if (_youtubeUserPlaylists.Count >= 200) break;
                    }
                    catch { continue; }
                }
            }
            catch { }
        }

        // ══════════════════════════════════════════
        // SYNC SUBSCRIPTIONS
        // ══════════════════════════════════════════
        private ObservableCollection<YouTubeSubscription> _youtubeSubscriptions = new ObservableCollection<YouTubeSubscription>();

        private async Task SyncSubscriptionsAsync(string accessToken)
        {
            try
            {
                _youtubeSubscriptions.Clear();
                var json = await AuthInnerTubePostAsync("browse", new JObject { ["browseId"] = "FEchannels" }, accessToken);
                if (json["_error"] != null) return;

                var items = json.SelectTokens("$..gridChannelRenderer");
                foreach (var item in items)
                {
                    try
                    {
                        string channelId = item["channelId"]?.ToString();
                        if (string.IsNullOrEmpty(channelId)) continue;

                        string title = item.SelectToken("title..text")?.ToString() ?? item["title"]?["simpleText"]?.ToString() ?? "";
                        string thumbUrl = item.SelectToken("thumbnail.thumbnails[-1:].url")?.ToString()
                            ?? item.SelectToken("thumbnail.thumbnails[0].url")?.ToString();

                        _youtubeSubscriptions.Add(new YouTubeSubscription
                        {
                            ChannelId = channelId,
                            Title = title,
                            ThumbnailUrl = thumbUrl
                        });

                        if (_youtubeSubscriptions.Count >= 500) break;
                    }
                    catch { continue; }
                }
            }
            catch { }
        }

        // ══════════════════════════════════════════
        // LIKE / DISLIKE VIDEO
        // ══════════════════════════════════════════
        private async Task<bool> RateVideoAsync(string videoId, string rating)
        {
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return false;

            try
            {
                string endpoint = rating == "like" ? "like/like" : (rating == "dislike" ? "like/dislike" : "like/removelike");
                var json = await AuthInnerTubePostAsync(endpoint, new JObject { ["target"] = new JObject { ["videoId"] = videoId } }, token);
                return json["_error"] == null;
            }
            catch { return false; }
        }

        // ══════════════════════════════════════════
        // WATCH LATER
        // ══════════════════════════════════════════
        private async Task<bool> AddToWatchLaterAsync(string videoId)
        {
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return false;

            try
            {
                var extra = new JObject
                {
                    ["playlistId"] = "WL",
                    ["actions"] = new JArray
                    {
                        new JObject
                        {
                            ["addedVideoId"] = videoId,
                            ["action"] = "ACTION_ADD_VIDEO"
                        }
                    }
                };
                var json = await AuthInnerTubePostAsync("browse/edit_playlist", extra, token);
                return json["_error"] == null;
            }
            catch { return false; }
        }

        private async Task RefreshGoogleTokenAndSyncAsync()
        {
            string token = await RefreshGoogleTokenAsync();
            if (!string.IsNullOrEmpty(token))
                await SyncAllAsync(token);
        }

        // ══════════════════════════════════════════
        // YOUTUBE PROFILE AVATAR
        // ══════════════════════════════════════════
        private void LoadHomeAvatar()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings.Values;
                string avatarUrl = SafeGetString(settings, "GoogleAvatarUrl", "");
                string userName = SafeGetString(settings, "GoogleUserName", "");

                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    bmp.DecodePixelWidth = 64; // 32dp × 2 for sharp rendering
                    bmp.UriSource = new Uri(avatarUrl, UriKind.Absolute);
                    HomeAvatarBrush.ImageSource = bmp;
                    HomeAvatarImage.Visibility = Visibility.Visible;
                    HomeAvatarFallback.Visibility = Visibility.Collapsed;
                }

                // Show user's first initial instead of "Y"
                if (!string.IsNullOrEmpty(userName))
                {
                    HomeAvatarLetter.Text = userName.Substring(0, 1).ToUpper();
                }
            }
            catch { }
        }

        private async Task FetchAndCacheAvatarAsync(string accessToken)
        {
            try
            {
                var json = await AuthInnerTubePostAsync("account/account_menu", new JObject(), accessToken);
                if (json["_error"] != null) return;

                // Extract name and avatar from account menu response
                string name = json.SelectToken("$..accountName..text")?.ToString()
                    ?? json.SelectToken("$..channelHandle..text")?.ToString() ?? "";
                string avatarUrl = json.SelectToken("$..accountPhoto..thumbnails[-1:].url")?.ToString()
                    ?? json.SelectToken("$..accountPhoto..thumbnails[0].url")?.ToString() ?? "";

                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    var settings = ApplicationData.Current.LocalSettings.Values;
                    settings["GoogleAvatarUrl"] = avatarUrl;
                    if (!string.IsNullOrEmpty(name))
                        settings["GoogleUserName"] = name;

                    LoadHomeAvatar();
                }
            }
            catch { }
        }

    }

    // ══════════════════════════════════════════
    // MODEL CLASSES
    // ══════════════════════════════════════════
    public class YouTubePlaylistInfo
    {
        public string PlaylistId { get; set; }
        public string Title { get; set; }
        public int TrackCount { get; set; }
        public string ThumbnailUrl { get; set; }
    }

    public class YouTubeSubscription
    {
        public string ChannelId { get; set; }
        public string Title { get; set; }
        public string ThumbnailUrl { get; set; }
    }
}
