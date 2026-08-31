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

        /// <summary>
        /// Toggle Account section between signed-out and signed-in panels
        /// </summary>
        private void UpdateAccountPanel(bool isLoggedIn, string statusText = null)
        {
            AccountSignedOutPanel.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
            AccountSignedInPanel.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;

            if (isLoggedIn)
            {
                var settings = ApplicationData.Current.LocalSettings.Values;
                string userName = settings.ContainsKey("GoogleUserName") ? settings["GoogleUserName"]?.ToString() : null;
                AccountUserName.Text = !string.IsNullOrEmpty(userName) ? userName : "Google Account";
                if (statusText != null)
                {
                    LoginStatusText.Text = statusText;
                }
            }
        }

        // Built-in OAuth credentials (YouTube TV public client � used by NewPipe, yt-dlp, etc.)
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

                if (settings.ContainsKey("GoogleAccessToken") || YTMusicWP.InnerTubeClient.HasCookieAuth)
                {
                    string status = settings.ContainsKey("GoogleCookieString") ? "Logged in (Cookie)" : "Synced";
                    UpdateAccountPanel(true, status);
                    LoginStatusText.Foreground = _greenBrush;
                    // Load cached avatar
                    LoadHomeAvatar();
                }

                bool isShuffle = SafeGetBool(settings, "ShuffleMode", false);
                ShuffleIcon.Foreground = isShuffle ? _greenBrush : _whiteBrush;
                int repeatMode = SafeGetInt(settings, "RepeatMode", 0);
                UpdateRepeatUI(repeatMode);

                // Playback settings � set values BEFORE attaching handlers to avoid triggering saves on load
                // Quality setting removed since only itag 18 is available

                AutoplayToggle.IsOn = SafeGetBool(settings, "Autoplay", true);
                GaplessToggle.IsOn = SafeGetBool(settings, "GaplessPlayback", true);
                NormalizeVolumeToggle.IsOn = SafeGetBool(settings, "NormalizeVolume", false);

                LiveTileToggle.IsOn = YTMusicWP.Services.TileService.IsLiveTileEnabled;
                int tileMode = YTMusicWP.Services.TileService.LiveTileMode;
                if (tileMode >= 0 && tileMode < LiveTileModeComboBox.Items.Count)
                    LiveTileModeComboBox.SelectedIndex = tileMode;
                int tileSpeed = YTMusicWP.Services.TileService.LiveTileSpeed;
                if (tileSpeed >= 0 && tileSpeed < LiveTileSpeedComboBox.Items.Count)
                    LiveTileSpeedComboBox.SelectedIndex = tileSpeed;

                // Now attach handlers � changes will save & apply immediately
                // Quality handler removed

                AutoplayToggle.Toggled += AutoplayToggle_Toggled;
                GaplessToggle.Toggled += GaplessToggle_Toggled;
                NormalizeVolumeToggle.Toggled += NormalizeVolumeToggle_Toggled;
                RegionComboBox.SelectionChanged += RegionComboBox_SelectionChanged;
                LiveTileToggle.Toggled += LiveTileToggle_Toggled;
                LiveTileModeComboBox.SelectionChanged += LiveTileModeComboBox_SelectionChanged;
                LiveTileSpeedComboBox.SelectionChanged += LiveTileSpeedComboBox_SelectionChanged;
            }
            catch { }
        }

        private async void RegionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedRegion = RegionComboBox.SelectedItem as ComboBoxItem;
            if (selectedRegion == null || selectedRegion.Tag == null) return;

            string regionTag = selectedRegion.Tag.ToString();
            if (regionTag == "AUTO")
                regionTag = DetectOsRegion();

            var settings = ApplicationData.Current.LocalSettings.Values;
            string oldRegion = settings.ContainsKey("TrendingRegion") ? settings["TrendingRegion"].ToString() : "";

            // Only reload if region actually changed
            if (regionTag == oldRegion) return;

            settings["TrendingRegion"] = regionTag;
            InnerTubeClient.SetRegion(regionTag);
            ShowToast("Region changed!");

            if (IsInternetAvailable())
            {
                homeTracks.Clear();
                HomeDynamicSections.ItemsSource = null;
                InnerTubeClient.ClearHomeCache();
                await LoadHomeRecommendations();
            }
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

        private void LiveTileToggle_Toggled(object sender, RoutedEventArgs e)
        {
            YTMusicWP.Services.TileService.IsLiveTileEnabled = LiveTileToggle.IsOn;
            if (LiveTileToggle.IsOn && homeTracks != null && homeTracks.Count > 0)
            {
                YTMusicWP.Services.TileService.UpdateRecommendations(homeTracks, favoriteTracks, historyTracks);
            }
        }

        private void LiveTileModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            YTMusicWP.Services.TileService.LiveTileMode = LiveTileModeComboBox.SelectedIndex;
            if (LiveTileModeComboBox.SelectedIndex == 0 && homeTracks != null && homeTracks.Count > 0)
            {
                YTMusicWP.Services.TileService.UpdateRecommendations(homeTracks, favoriteTracks, historyTracks, 5, true);
            }
            else if (LiveTileModeComboBox.SelectedIndex == 1)
            {
                YTMusicWP.Services.TileService.ClearLiveTile();
                if (currentTrack != null)
                {
                    YTMusicWP.Services.TileService.UpdateNowPlayingWithQueue(currentTrack.Title, currentTrack.ChannelName, currentTrack.ThumbnailUrl, currentQueueTracks);
                }
            }
            else if (LiveTileModeComboBox.SelectedIndex == 2)
            {
                YTMusicWP.Services.TileService.ClearLiveTile();
            }
        }

        private void LiveTileSpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            YTMusicWP.Services.TileService.LiveTileSpeed = LiveTileSpeedComboBox.SelectedIndex;
            if (LiveTileToggle.IsOn && homeTracks != null && homeTracks.Count > 0)
            {
                YTMusicWP.Services.TileService.UpdateRecommendations(homeTracks, favoriteTracks, historyTracks, 5, true);
            }
        }

        private async void RefreshStorageStats_Click(object sender, RoutedEventArgs e)
        {
            await UpdateStorageDisplayAsync();
            ShowToast("Storage refreshed");
        }

        private async void CleanAllCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int count = await CleanAllCacheInternalAsync();
                await UpdateStorageDisplayAsync();
                ShowToast("Cleaned " + count + " cache items");
            }
            catch
            {
                ShowToast("Error cleaning cache");
            }
        }

        private async void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int count = await CleanImageCacheInternalAsync();
                await UpdateStorageDisplayAsync();
                ShowToast("Cleared " + count + " cached images");
            }
            catch
            {
                ShowToast("Error clearing image cache");
            }
        }

        private async void ClearTempStreams_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int count = await CleanTempStreamsInternalAsync();
                await UpdateStorageDisplayAsync();
                ShowToast("Cleared " + count + " temp stream files");
            }
            catch
            {
                ShowToast("Error clearing temp streams");
            }
        }

        private async void LogoutGoogle_Click(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings.Remove("GoogleAccessToken");
            settings.Remove("GoogleRefreshToken");
            settings.Remove("GoogleClientId");
            settings.Remove("GoogleClientSecret");
            settings.Remove("GoogleTokenExpiry");

            // Clear Cookie Auth
            settings.Remove("GoogleCookieString");
            settings.Remove("GoogleSAPISID");
            InnerTubeClient.ClearCookieAuth();

            // Clear WebView cookies physically
            try
            {
                var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter();
                var cookieManager = filter.CookieManager;
                var cookies = cookieManager.GetCookies(new Uri("https://google.com"));
                foreach (var cookie in cookies) cookieManager.DeleteCookie(cookie);
                cookies = cookieManager.GetCookies(new Uri("https://youtube.com"));
                foreach (var cookie in cookies) cookieManager.DeleteCookie(cookie);
                cookies = cookieManager.GetCookies(new Uri("https://accounts.google.com"));
                foreach (var cookie in cookies) cookieManager.DeleteCookie(cookie);
            }
            catch { }

            _youtubeUserPlaylists.Clear();
            _youtubeSubscriptions.Clear();
            favoriteTracks.Clear();
            historyTracks.Clear();

            // Clear all cached YouTube data
            try { var f = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_playlists_cache.json"); await f.DeleteAsync(); } catch { }
            try { var f = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_subs_cache.json"); await f.DeleteAsync(); } catch { }
            try { var f = await ApplicationData.Current.LocalFolder.GetFileAsync("favorites.json"); await f.DeleteAsync(); } catch { }
            try { var f = await ApplicationData.Current.LocalFolder.GetFileAsync("history.json"); await f.DeleteAsync(); } catch { }

            LoginStatusText.Text = "Not logged in";
            LoginStatusText.Foreground = _authGrayBrush;
            ClientIdTextBox.Text = "";
            ClientSecretTextBox.Text = "";
            UpdateAccountPanel(false);

            // Reset avatar to default
            HomeAvatarImage.Visibility = Visibility.Collapsed;
            HomeAvatarFallback.Visibility = Visibility.Visible;
            HomeAvatarLetter.Text = "Y";
            LibAvatarImage.Visibility = Visibility.Collapsed;
            LibAvatarFallback.Visibility = Visibility.Visible;
            LibAvatarLetter.Text = "Y";
            settings.Remove("GoogleAvatarUrl");
            settings.Remove("GoogleUserName");

            RefreshLibraryList();
            
            // Clear home screen UI and force a reload for guest content
            homeTracks.Clear();
            HomeDynamicSections.ItemsSource = null;
            if (IsInternetAvailable())
            {
                InnerTubeClient.ClearHomeCache();
                var _ = LoadHomeRecommendations();
            }

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
            DeviceCodeQrImage.Source = null;
            DeviceCodeQrLoading.Visibility = Visibility.Visible;
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

        private async void CloseLoginWeb_Click(object sender, RoutedEventArgs e)
        {
            if (_cookieLoginActive)
            {
                // Try to extract cookies one last time before closing
                await ExtractAndSaveCookiesAsync("");
            }
            
            LoginWebContainer.Visibility = Visibility.Collapsed;
            _deviceCodePolling = false;
            DeviceCodeQrImage.Source = null;
        }

        private async Task StartDeviceCodeFlow()
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", _builtInClientId),
                    new KeyValuePair<string, string>("scope", "https://www.googleapis.com/auth/youtube openid profile")
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

                    // Generate QR Code bitmap with auto-fill URL
                    string qrUrl = !string.IsNullOrEmpty(userCode)
                        ? ("https://www.google.com/device?user_code=" + userCode)
                        : verificationUrl;

                    var qrBitmap = Services.QrCodeGenerator.GenerateQrBitmap(qrUrl, 4, 3);
                    if (qrBitmap != null)
                    {
                        DeviceCodeQrImage.Source = qrBitmap;
                    }
                    else
                    {
                        DeviceCodeQrImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri("https://api.qrserver.com/v1/create-qr-code/?size=250x250&data=" + Uri.EscapeDataString(qrUrl)));
                    }
                    DeviceCodeQrLoading.Visibility = Visibility.Collapsed;

                    // Start polling for user authorization
                    _deviceCodePolling = true;
                    await PollDeviceCodeAsync(deviceCode, interval, expiresIn);
                }
                else
                {
                    DeviceCodeText.Text = "ERROR";
                    DeviceCodeStatus.Text = "Failed to get code. Try again.";
                    DeviceCodeProgress.Visibility = Visibility.Collapsed;
                    DeviceCodeQrLoading.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                DeviceCodeText.Text = "ERROR";
                DeviceCodeStatus.Text = "Network error. Check your connection.";
                DeviceCodeProgress.Visibility = Visibility.Collapsed;
                DeviceCodeQrLoading.Visibility = Visibility.Collapsed;
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
                        SyncNowBtn.Visibility = Visibility.Visible;
                        settings["GoogleRefreshToken"] = refreshToken;
                        long expiresInSec = json["expires_in"]?.Value<long>() ?? 3600;
                        settings["GoogleTokenExpiry"] = DateTimeOffset.UtcNow.AddSeconds(expiresInSec - 60).UtcDateTime.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                        SyncNowBtn.Visibility = Visibility.Visible;

                        DeviceCodeStatus.Text = "Success! Syncing...";
                        DeviceCodeProgress.Visibility = Visibility.Collapsed;

                        UpdateAccountPanel(true, "Logged In & Syncing...");
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

        // ==========================================
        // COOKIE-BASED LOGIN (WebView → Google → extract cookies)
        // ==========================================
        private bool _cookieLoginActive = false;

        private void LoginCookie_Click(object sender, RoutedEventArgs e)
        {
            _cookieLoginActive = true;
            LoginWebContainer.Visibility = Visibility.Visible;

            // Hide the Device Code UI, show WebView instead
            LoginWebView.Visibility = Visibility.Visible;
            LoginWebLoading.Visibility = Visibility.Visible;

            // Hide device code elements
            DeviceCodeStatus.Text = "Signing in via browser...";
            DeviceCodeProgress.Visibility = Visibility.Collapsed;

            // Navigate to Google login → redirect to YouTube Music
            LoginWebView.Navigate(new Uri("https://accounts.google.com/ServiceLogin?continue=https%3A%2F%2Fmusic.youtube.com%2F&hl=en"));
        }

        private void LoginWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            if (_cookieLoginActive)
                LoginWebLoading.Visibility = Visibility.Visible;
        }

        private void LoginWebView_NavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            if (_cookieLoginActive)
            {
                LoginWebLoading.Visibility = Visibility.Collapsed;
                DeviceCodeStatus.Text = "Navigation failed. Check your connection.";
            }
        }

        private async void LoginWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            LoginWebLoading.Visibility = Visibility.Collapsed;

            if (!_cookieLoginActive) return;

            string currentUrl = sender.Source?.ToString() ?? "";

            // Check if we've landed on YouTube Music (login complete) or if Google threw an "unsupported browser" error
            // Actually, let's just aggressively check for the SAPISID cookie on EVERY navigation complete.
            // If the user logs in but gets stuck on the "browser not supported" page, the cookie is usually already set!
            await ExtractAndSaveCookiesAsync(currentUrl);
        }

        private async Task ExtractAndSaveCookiesAsync(string currentUrl)
        {
            try
            {
                var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter();
                var cookieManager = filter.CookieManager;
                var cookies = cookieManager.GetCookies(new Uri("https://music.youtube.com"));

                string sapisid = null;
                var cookieParts = new System.Collections.Generic.List<string>();

                foreach (var cookie in cookies)
                {
                    cookieParts.Add(cookie.Name + "=" + cookie.Value);

                    if (cookie.Name == "SAPISID" || cookie.Name == "__Secure-3PAPISID")
                    {
                        if (sapisid == null) // Prefer SAPISID over __Secure-3PAPISID
                            sapisid = cookie.Value;
                    }
                }

                // If we don't have the cookie yet, we just wait for the user to finish logging in.
                if (string.IsNullOrEmpty(sapisid) || cookieParts.Count < 3)
                {
                    // If they reached YouTube Music but still no cookie, maybe show an error
                    if (currentUrl.Contains("music.youtube.com") && !currentUrl.Contains("accounts.google.com"))
                    {
                        DeviceCodeStatus.Text = "Login incomplete. Please try again.";
                    }
                    return;
                }

                string cookieString = string.Join("; ", cookieParts);

                // Save to settings
                var settings = ApplicationData.Current.LocalSettings.Values;
                settings["GoogleCookieString"] = cookieString;
                settings["GoogleSAPISID"] = sapisid;

                // Activate cookie auth in InnerTubeClient
                InnerTubeClient.SetCookieAuth(cookieString, sapisid);

                // Hide WebView
                _cookieLoginActive = false;
                LoginWebView.Visibility = Visibility.Collapsed;
                LoginWebLoading.Visibility = Visibility.Collapsed;
                LoginWebContainer.Visibility = Visibility.Collapsed;

                UpdateAccountPanel(true, "Logged in (Cookie)");
                LoginStatusText.Foreground = _greenBrush;
                SyncNowBtn.Visibility = Visibility.Visible;
                ShowToast("Login successful!");

                // Fetch user info from cookie session
                await FetchCookieUserInfoAsync();
                LoadHomeAvatar();

                // Run full sync (Library, Liked Songs, etc.)
                await SyncAllAsync();

                // Reload home with personalized content
                if (IsInternetAvailable())
                {
                    homeTracks.Clear();
                    HomeDynamicSections.ItemsSource = null;
                    InnerTubeClient.ClearHomeCache();
                    await LoadHomeRecommendations();
                }
            }
            catch
            {
                DeviceCodeStatus.Text = "Failed to extract cookies. Try again.";
            }
        }

        private async Task FetchCookieUserInfoAsync()
        {
            try
            {
                // Use cookie auth to get account menu (contains user name + avatar)
                var extraParams = new JObject();
                var data = await InnerTubeClient.CookieInnerTubePostAsync("account/account_menu", extraParams);
                if (data != null && data["_error"] == null)
                {
                    var header = data["actions"]?[0]?["openPopupAction"]?["popup"]?["multiPageMenuRenderer"]?["header"]?["activeAccountHeaderRenderer"];
                    if (header != null)
                    {
                        string name = header["accountName"]?["runs"]?[0]?["text"]?.ToString();
                        string avatarUrl = header["accountPhoto"]?["thumbnails"]?[0]?["url"]?.ToString();

                        var settings = ApplicationData.Current.LocalSettings.Values;
                        if (!string.IsNullOrEmpty(name))
                        {
                            settings["GoogleUserName"] = name;
                            AccountUserName.Text = name;
                        }
                        if (!string.IsNullOrEmpty(avatarUrl))
                        {
                            settings["GoogleAvatarUrl"] = avatarUrl;
                        }
                    }
                }
            }
            catch { }
        }

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
                    SyncNowBtn.Visibility = Visibility.Visible;
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

        // ------------------------------------------
        // AUTHENTICATED INNERTUBE HELPER
        // ------------------------------------------
        

        

        // ------------------------------------------
        // SYNC LIKED VIDEOS
        // ------------------------------------------
        // SYNC LIKED VIDEOS (InnerTube browse VLLL)
        // ------------------------------------------
        private string _likedSongsContinuation = null;
        private bool _isLoadingMoreLiked = false;

        private async Task SyncLikedVideosAsync(string accessToken)
        {
            try
            {
                LoginStatusText.Text = "Status: Syncing Liked Songs...";
                favoriteTracks.Clear();
                _likedSongsContinuation = null;

                // Browse "VLLL" = user's Liked Videos playlist via TVHTML5 client
                var json = await InnerTubeClient.AuthInnerTubePostAsync("browse", new JObject { ["browseId"] = "VLLL" }, accessToken);

                if (json["_error"] != null)
                {
                    LoginStatusText.Text = "Sync Error: " + json["_error"];
                    LoginStatusText.Foreground = _authOrangeBrush;
                    return;
                }

                // Save continuation token for lazy loading
                _likedSongsContinuation = json.SelectToken("$..nextContinuationData.continuation")?.ToString()
                    ?? json.SelectToken("$..continuations[0]..continuation")?.ToString();

                ProcessLikedPlaylistResponse(json);
            }
            catch (Exception ex)
            {
                LoginStatusText.Text = "Sync Error: " + ex.Message;
                LoginStatusText.Foreground = _authRedBrush;
            }
        }

        /// <summary>
        /// Load more liked songs when user scrolls to bottom
        /// </summary>
        public async Task LoadMoreLikedSongsAsync()
        {
            if (_isLoadingMoreLiked || string.IsNullOrEmpty(_likedSongsContinuation)) return;

            _isLoadingMoreLiked = true;
            try
            {
                string token = await GetAccessTokenAsync();
                if (token == null && !InnerTubeClient.HasCookieAuth) return;

                var json = await InnerTubeClient.AuthInnerTubePostAsync("browse", new JObject { ["continuation"] = _likedSongsContinuation }, token);
                if (json["_error"] != null) { _likedSongsContinuation = null; return; }

                _likedSongsContinuation = json.SelectToken("$..nextContinuationData.continuation")?.ToString()
                    ?? json.SelectToken("$..continuations[0]..continuation")?.ToString();

                ProcessLikedPlaylistResponse(json);
            }
            catch { _likedSongsContinuation = null; }
            finally { _isLoadingMoreLiked = false; }
        }

        public bool HasMoreLikedSongs => !string.IsNullOrEmpty(_likedSongsContinuation);

        /// <summary>
        /// Parse InnerTube TVHTML5 playlist browse response (VLLL) to extract liked video metadata.
        /// TV client uses tileRenderer inside playlistVideoListRenderer.contents[]
        /// </summary>
        private void ProcessLikedPlaylistResponse(JObject json)
        {
            bool hasNew = false;
            
            // TVHTML5 returns: tvBrowseRenderer ? tvSurfaceContentRenderer ? twoColumnRenderer
            //   ? rightColumn ? playlistVideoListRenderer ? contents[] ? tileRenderer
            var renderers = json.SelectTokens("$..tileRenderer").ToList();
            
            // Also try other known renderer types as fallback
            if (renderers.Count == 0)
            {
                renderers = json.SelectTokens("$..playlistVideoRenderer")
                    .Union(json.SelectTokens("$..gridVideoRenderer"))
                    .Union(json.SelectTokens("$..playlistPanelVideoRenderer"))
                    .ToList();
            }

            System.Diagnostics.Debug.WriteLine("[LikedSync] Found " + renderers.Count + " renderers");

            foreach (var renderer in renderers)
            {
                try
                {

                    // Extract videoId
                    string videoId = null;
                    try { videoId = renderer.SelectToken("onSelectCommand.watchEndpoint.videoId")?.ToString(); } catch { }
                    if (videoId == null) try { videoId = renderer.SelectToken("navigationEndpoint.watchEndpoint.videoId")?.ToString(); } catch { }
                    if (videoId == null) try { videoId = renderer["videoId"]?.ToString(); } catch { }

                    if (string.IsNullOrEmpty(videoId) || favoriteTracks.Any(t => t.VideoId == videoId)) continue;

                    // Title: metadata ? tileMetadataRenderer ? title ? simpleText
                    string title = null;
                    try { title = renderer.SelectToken("metadata.tileMetadataRenderer.title.simpleText")?.ToString(); } catch { }
                    if (title == null) try { title = renderer.SelectToken("metadata.tileMetadataRenderer.title.runs[0].text")?.ToString(); } catch { }
                    if (title == null) try { title = renderer.SelectToken("title.simpleText")?.ToString(); } catch { }
                    if (title == null) try { title = renderer.SelectToken("title.runs[0].text")?.ToString(); } catch { }

                    // Channel: metadata ? tileMetadataRenderer ? lines[0] ? lineRenderer ? items[0] ? lineItemRenderer ? text ? runs[0] ? text
                    string channel = "";
                    try { channel = renderer.SelectToken("metadata.tileMetadataRenderer.lines[0].lineRenderer.items[0].lineItemRenderer.text.runs[0].text")?.ToString(); } catch { }
                    if (string.IsNullOrEmpty(channel)) try { channel = renderer.SelectToken("shortBylineText.runs[0].text")?.ToString(); } catch { }
                    if (string.IsNullOrEmpty(channel)) try { channel = renderer.SelectToken("longBylineText.runs[0].text")?.ToString(); } catch { }

                    // Channel ID
                    string chId = "";
                    try { chId = renderer.SelectToken("metadata.tileMetadataRenderer.lines[0].lineRenderer.items[0].lineItemRenderer.text.runs[0].navigationEndpoint.browseEndpoint.browseId")?.ToString() ?? ""; } catch { }

                    // Thumbnail: header ? tileHeaderRenderer ? thumbnail ? thumbnails
                    string thumbUrl = "";
                    try
                    {
                        var thumbArr = renderer.SelectToken("header.tileHeaderRenderer.thumbnail.thumbnails");
                        if (thumbArr != null)
                        {
                            foreach (var t in thumbArr)
                            {
                                string u = t["url"]?.ToString();
                                if (!string.IsNullOrEmpty(u)) thumbUrl = u;
                            }
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(title)) title = "Video " + videoId;
                    if (title == "[Deleted video]" || title == "Deleted video" || title == "Private video") continue;
                    channel = CleanChannelName(channel ?? "");

                    favoriteTracks.Add(new YouTubeTrack
                    {
                        VideoId = videoId,
                        Title = title,
                        ChannelName = channel,
                        ChannelId = chId,
                        ThumbnailUrl = GetSquareThumbnail(thumbUrl)
                    });
                    hasNew = true;
                }
                catch { continue; }
            }

            if (hasNew) SaveFavoritesAsync();

            LoginStatusText.Text = "Synced! " + favoriteTracks.Count + (HasMoreLikedSongs ? "+" : "") + " liked songs";
            LoginStatusText.Foreground = _greenBrush;

            // Update track count display if viewing liked songs
            try { PlaylistDetailsTrackCount.Text = favoriteTracks.Count + (HasMoreLikedSongs ? "+" : "") + " songs"; } catch { }
        }

        // ------------------------------------------
        // SYNC ALL � Called after login and on app resume
        // ------------------------------------------
        private async Task SyncAllAsync(string accessToken = null)
        {
            await SyncLikedVideosAsync(accessToken);
            await LoadYouTubePlaylistsCacheAsync();
            await SyncPlaylistsAsync(accessToken);
            await SyncSubscriptionsAsync(accessToken);
            // Fetch YouTube profile avatar
            await FetchAndCacheAvatarAsync(accessToken);
            // Refresh Library UI to show synced playlists/subs
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => RefreshLibraryList());
        }

        // ------------------------------------------
        // SYNC USER PLAYLISTS  Fetch from YouTube
        // ------------------------------------------
        private async Task SyncPlaylistsAsync(string accessToken)
        {
            try
            {
                if (!InnerTubeClient.HasCookieAuth) return;

                var extra = new JObject { ["browseId"] = "FEmusic_liked_playlists" };
                var json = await InnerTubeClient.CookieInnerTubePostAsync("browse", extra, "WEB_REMIX", "1.20260304.03.00");

                if (json["_error"] != null)
                {
                    System.Diagnostics.Debug.WriteLine("[PlaylistSync] FEmusic_liked_playlists error: " + json["_error"]);
                    return;
                }

                bool hasNew = false;
                var items = json.SelectTokens("$..musicTwoRowItemRenderer").ToList();
                
                foreach (var renderer in items)
                {
                    try
                    {
                        string playlistId = renderer.SelectToken("navigationEndpoint.browseEndpoint.browseId")?.ToString();
                        if (string.IsNullOrEmpty(playlistId)) continue;
                        
                        if (playlistId.StartsWith("VL")) playlistId = playlistId.Substring(2);
                        if (!playlistId.StartsWith("PL") && !playlistId.StartsWith("UC") && !playlistId.StartsWith("RD") && !playlistId.StartsWith("LM")) continue;

                        if (_youtubeUserPlaylists.Any(p => p.PlaylistId == playlistId)) continue;

                        string title = renderer.SelectToken("title.runs[0].text")?.ToString() 
                                    ?? renderer.SelectToken("title.simpleText")?.ToString();
                        if (string.IsNullOrEmpty(title)) continue;

                        string thumbUrl = "";
                        var thumbArr = renderer.SelectToken("thumbnailRenderer.musicThumbnailRenderer.thumbnail.thumbnails");
                        if (thumbArr != null && thumbArr.Count() > 0)
                        {
                            thumbUrl = thumbArr.Last()["url"]?.ToString() ?? "";
                        }

                        int trackCount = 0;
                        string subtitle = renderer.SelectToken("subtitle.runs[0].text")?.ToString() 
                                       ?? renderer.SelectToken("subtitle.simpleText")?.ToString() ?? "";
                        var match = System.Text.RegularExpressions.Regex.Match(subtitle, @"(\d+)");
                        if (match.Success) trackCount = int.Parse(match.Groups[1].Value);

                        _youtubeUserPlaylists.Add(new YouTubePlaylistInfo
                        {
                            PlaylistId = playlistId,
                            Title = title,
                            TrackCount = trackCount,
                            ThumbnailUrl = GetSquareThumbnail(thumbUrl)
                        });
                        hasNew = true;
                    }
                    catch { continue; }
                }

                if (hasNew) SaveYouTubePlaylistsCacheAsync();
                System.Diagnostics.Debug.WriteLine("[PlaylistSync] Total playlists: " + _youtubeUserPlaylists.Count);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[PlaylistSync] Exception: " + ex.Message);
            }
        }

        // ------------------------------------------
        // GET ACCESS TOKEN  Auto-refresh if expired
        // ------------------------------------------
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
                    // Token expired ? refresh
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

        // ------------------------------------------
        // USER PLAYLISTS � Local only (no YouTube sync)
        // ------------------------------------------
        private ObservableCollection<YouTubePlaylistInfo> _youtubeUserPlaylists = new ObservableCollection<YouTubePlaylistInfo>();


        private async void SaveYouTubePlaylistsCacheAsync()
        {
            try
            {
                var arr = new JArray();
                foreach (var pl in _youtubeUserPlaylists)
                {
                    arr.Add(new JObject
                    {
                        ["PlaylistId"] = pl.PlaylistId,
                        ["Title"] = pl.Title,
                        ["TrackCount"] = pl.TrackCount,
                        ["ThumbnailUrl"] = pl.ThumbnailUrl ?? ""
                    });
                }
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("yt_playlists_cache.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, arr.ToString());
            }
            catch { }
        }

        private async Task LoadYouTubePlaylistsCacheAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_playlists_cache.json");
                string json = await FileIO.ReadTextAsync(file);
                var arr = JArray.Parse(json);
                _youtubeUserPlaylists.Clear();
                foreach (var item in arr)
                {
                    _youtubeUserPlaylists.Add(new YouTubePlaylistInfo
                    {
                        PlaylistId = item["PlaylistId"]?.ToString() ?? "",
                        Title = item["Title"]?.ToString() ?? "",
                        TrackCount = item["TrackCount"]?.Value<int>() ?? 0,
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString() ?? ""
                    });
                }
            }
            catch { }
        }
        // ------------------------------------------
        // LOCAL PLAYLIST TRACK STORAGE
        // ------------------------------------------
        private string GetLocalPlaylistFileName(string playlistId)
        {
            return "pl_tracks_" + playlistId.Replace("LOCAL_", "") + ".json";
        }

        private async Task AddTrackToLocalPlaylistAsync(string playlistId, YouTubeTrack track)
        {
            try
            {
                var tracks = await LoadLocalPlaylistTracksAsync(playlistId);
                if (tracks.Any(t => t.VideoId == track.VideoId)) return;
                tracks.Add(track);
                await SaveLocalPlaylistTracksAsync(playlistId, tracks);
            }
            catch { }
        }

        private async Task<List<YouTubeTrack>> LoadLocalPlaylistTracksAsync(string playlistId)
        {
            var result = new List<YouTubeTrack>();
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(GetLocalPlaylistFileName(playlistId));
                string json = await FileIO.ReadTextAsync(file);
                var arr = JArray.Parse(json);
                foreach (var item in arr)
                {
                    result.Add(new YouTubeTrack
                    {
                        VideoId = item["VideoId"]?.ToString() ?? "",
                        Title = item["Title"]?.ToString() ?? "",
                        ChannelName = item["ChannelName"]?.ToString() ?? "",
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString() ?? ""
                    });
                }
            }
            catch { }
            return result;
        }

        private async Task SaveLocalPlaylistTracksAsync(string playlistId, List<YouTubeTrack> tracks)
        {
            try
            {
                var arr = new JArray();
                foreach (var t in tracks)
                {
                    arr.Add(new JObject
                    {
                        ["VideoId"] = t.VideoId,
                        ["Title"] = t.Title,
                        ["ChannelName"] = t.ChannelName,
                        ["ThumbnailUrl"] = t.ThumbnailUrl ?? ""
                    });
                }
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    GetLocalPlaylistFileName(playlistId), CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, arr.ToString());
            }
            catch { }
        }

        // ------------------------------------------
        // SYNC SUBSCRIPTIONS
        // ------------------------------------------
        private ObservableCollection<YouTubeSubscription> _youtubeSubscriptions = new ObservableCollection<YouTubeSubscription>();

        private async Task SyncSubscriptionsAsync(string accessToken)
        {
            try
            {
                _youtubeSubscriptions.Clear();

                // Delete old cache to prevent stale unfiltered data
                try { var f = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_subs_cache.json"); await f.DeleteAsync(); } catch { }

                // Fetch YouTube Music library artists directly
                var json = await InnerTubeClient.AuthInnerTubePostAsync("browse", new JObject { ["browseId"] = "FEmusic_library_corpus_artists" }, accessToken, "WEB_REMIX", "1.20231214.01.00");
                if (json["_error"] != null) return;

                var renderers = json.SelectTokens("$..musicTwoRowItemRenderer").ToList();
                if (renderers.Count == 0)
                {
                    renderers = json.SelectTokens("$..musicListItemRenderer").ToList();
                }
                if (renderers.Count == 0)
                {
                    renderers = json.SelectTokens("$..musicResponsiveListItemRenderer").ToList();
                }

                foreach (var renderer in renderers)
                {
                    string title = renderer.SelectToken("title.runs[0].text")?.ToString() 
                        ?? renderer.SelectToken("flexColumns[0].musicResponsiveListItemFlexColumnRenderer.text.runs[0].text")?.ToString() 
                        ?? "";
                    if (string.IsNullOrEmpty(title)) continue;

                    string browseId = renderer.SelectToken("navigationEndpoint.browseEndpoint.browseId")?.ToString() 
                        ?? renderer.SelectToken("flexColumns[0].musicResponsiveListItemFlexColumnRenderer.text.runs[0].navigationEndpoint.browseEndpoint.browseId")?.ToString();
                    if (string.IsNullOrEmpty(browseId)) continue;

                    string avatarUrl = renderer.SelectToken("thumbnailRenderer.musicThumbnailRenderer.thumbnail.thumbnails[0].url")?.ToString() 
                        ?? renderer.SelectToken("thumbnail.musicThumbnailRenderer.thumbnail.thumbnails[0].url")?.ToString() 
                        ?? "";
                        
                    if (avatarUrl.StartsWith("//")) avatarUrl = "https:" + avatarUrl;

                    var sub = new YouTubeSubscription
                    {
                        ChannelId = browseId,
                        Title = title,
                        ThumbnailUrl = avatarUrl
                    };
                    _youtubeSubscriptions.Add(sub);
                }
            }
            catch { }

            // Cache subscriptions locally
            SaveYouTubeSubscriptionsCacheAsync();
        }

        private async void SaveYouTubeSubscriptionsCacheAsync()
        {
            try
            {
                var arr = new JArray();
                foreach (var sub in _youtubeSubscriptions)
                {
                    arr.Add(new JObject
                    {
                        ["ChannelId"] = sub.ChannelId,
                        ["Title"] = sub.Title,
                        ["ThumbnailUrl"] = sub.ThumbnailUrl ?? ""
                    });
                }
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("yt_subs_cache.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, arr.ToString());
            }
            catch { }
        }

        private async Task LoadYouTubeSubscriptionsCacheAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_subs_cache.json");
                string json = await FileIO.ReadTextAsync(file);
                var arr = JArray.Parse(json);
                _youtubeSubscriptions.Clear();
                foreach (var item in arr)
                {
                    _youtubeSubscriptions.Add(new YouTubeSubscription
                    {
                        ChannelId = item["ChannelId"]?.ToString() ?? "",
                        Title = item["Title"]?.ToString() ?? "",
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString() ?? ""
                    });
                }
            }
            catch { }
        }

        // ------------------------------------------
        // LIKE / DISLIKE VIDEO
        // ------------------------------------------
        private async Task<bool> RateVideoAsync(string videoId, string rating)
        {
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth) return false;

            try
            {
                string endpoint = rating == "like" ? "like/like" : (rating == "dislike" ? "like/dislike" : "like/removelike");
                var json = await InnerTubeClient.AuthInnerTubePostAsync(endpoint, new JObject { ["target"] = new JObject { ["videoId"] = videoId } }, token);
                return json["_error"] == null;
            }
            catch { return false; }
        }

        // ------------------------------------------
        // WATCH LATER
        // ------------------------------------------
        private async Task<bool> AddToWatchLaterAsync(string videoId)
        {
            return (await AddToYouTubePlaylistAsync("WL", videoId)) != null;
        }

        private async Task<string> AddToYouTubePlaylistAsync(string playlistId, string videoId)
        {
            if (playlistId.StartsWith("LOCAL_")) return "SUCCESS";

            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth) return null;

            try
            {
                return await InnerTubeClient.AddToYouTubePlaylistAsync(playlistId, videoId, token);
            }
            catch { return null; }
        }

        private async Task<string> CreateYouTubePlaylistAsync(string title)
        {
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth)
            {
                // Fallback to local if not logged in
                return "LOCAL_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            }

            try
            {
                string plId = await InnerTubeClient.CreateYouTubePlaylistAsync(title, token);
                if (string.IsNullOrEmpty(plId))
                    return "LOCAL_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                return plId;
            }
            catch { return "LOCAL_" + Guid.NewGuid().ToString("N").Substring(0, 12); }
        }

        private async Task<bool> DeleteYouTubePlaylistAsync(string playlistId)
        {
            if (playlistId.StartsWith("LOCAL_")) return true;

            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth) return false;

            try
            {
                return await InnerTubeClient.DeleteYouTubePlaylistAsync(playlistId, token);
            }
            catch { return false; }
        }

        private async Task<bool> RemoveFromYouTubePlaylistAsync(string playlistId, string videoId, string setVideoId = "")
        {
            if (playlistId.StartsWith("LOCAL_")) return true;

            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth) return false;

            try
            {
                return await InnerTubeClient.RemoveFromYouTubePlaylistAsync(playlistId, videoId, setVideoId, token);
            }
            catch { return false; }
        }

        private async Task RefreshGoogleTokenAndSyncAsync()
        {
            string token = await RefreshGoogleTokenAsync();
            if (!string.IsNullOrEmpty(token))
                await SyncAllAsync(token);
        }

        private async void SyncNow_Click(object sender, RoutedEventArgs e)
        {
            SyncNowBtn.IsEnabled = false;
            try
            {
                string accessToken = await GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(accessToken) || InnerTubeClient.HasCookieAuth)
                {
                    LoginStatusText.Text = "Syncing...";
                    LoginStatusText.Foreground = _authOrangeBrush;
                    await SyncAllAsync(accessToken);
                }
                else
                {
                    LoginStatusText.Text = "Not logged in";
                    LoginStatusText.Foreground = _authRedBrush;
                }
            }
            catch (Exception ex)
            {
                LoginStatusText.Text = "Sync error: " + ex.Message;
                LoginStatusText.Foreground = _authRedBrush;
            }
            finally
            {
                SyncNowBtn.IsEnabled = true;
            }
        }

        // ------------------------------------------
        // YOUTUBE PROFILE AVATAR
        // ------------------------------------------
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
                    bmp.DecodePixelWidth = 64; // 32dp � 2 for sharp rendering
                    bmp.UriSource = new Uri(avatarUrl, UriKind.Absolute);

                    // Home avatar
                    HomeAvatarBrush.ImageSource = bmp;
                    HomeAvatarImage.Visibility = Visibility.Visible;
                    HomeAvatarFallback.Visibility = Visibility.Collapsed;

                    // Library avatar
                    var bmp2 = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    bmp2.DecodePixelWidth = 64;
                    bmp2.UriSource = new Uri(avatarUrl, UriKind.Absolute);
                    LibAvatarBrush.ImageSource = bmp2;
                    LibAvatarImage.Visibility = Visibility.Visible;
                    LibAvatarFallback.Visibility = Visibility.Collapsed;
                }

                // Show user's first initial instead of "Y"
                if (!string.IsNullOrEmpty(userName))
                {
                    string initial = userName.Substring(0, 1).ToUpper();
                    HomeAvatarLetter.Text = initial;
                    LibAvatarLetter.Text = initial;
                }
            }
            catch { }
        }

        private async Task FetchAndCacheAvatarAsync(string accessToken)
        {
            try
            {
                // Method 0: Google userinfo (works if openid+profile scope is available)
                var userinfoReq = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
                userinfoReq.Headers.Add("Authorization", "Bearer " + accessToken);
                var userinfoResp = await _apiClient.SendAsync(userinfoReq);
                if (userinfoResp.IsSuccessStatusCode)
                {
                    string uiJson = await userinfoResp.Content.ReadAsStringAsync();
                    var uiData = JObject.Parse(uiJson);
                    string name = uiData["name"]?.ToString() ?? "";
                    string pic = uiData["picture"]?.ToString() ?? "";
                    // Request higher res
                    if (!string.IsNullOrEmpty(pic) && pic.Contains("=s96-c"))
                        pic = pic.Replace("=s96-c", "=s128-c");
                    if (SaveAvatarData(name, pic)) return;
                }

                // Method 1: YouTube Data API channels?mine=true
                var request = new HttpRequestMessage(HttpMethod.Get,
                    "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true&fields=items(snippet(title,thumbnails))");
                request.Headers.Add("Authorization", "Bearer " + accessToken);
                var response = await _apiClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string resultJson = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(resultJson);
                    var items = json["items"] as JArray;
                    if (items != null && items.Count > 0)
                    {
                        var snippet = items[0]["snippet"];
                        string name = snippet?["title"]?.ToString() ?? "";
                        string avatarUrl = snippet?.SelectToken("thumbnails.high.url")?.ToString()
                            ?? snippet?.SelectToken("thumbnails.medium.url")?.ToString()
                            ?? snippet?.SelectToken("thumbnails.default.url")?.ToString() ?? "";

                        if (SaveAvatarData(name, avatarUrl)) return;
                    }
                }

                // Method 2: InnerTube account_menu with WEB client
                var body = new JObject
                {
                    ["context"] = new JObject
                    {
                        ["client"] = new JObject
                        {
                            ["clientName"] = "WEB",
                            ["clientVersion"] = "2.20241016.00.00",
                            ["hl"] = InnerTubeClient.CurrentLanguage,
                            ["gl"] = InnerTubeClient.CurrentRegion
                        }
                    }
                };

                string menuUrl = "https://www.youtube.com/youtubei/v1/account/account_menu?prettyPrint=false";
                var menuReq = new HttpRequestMessage(HttpMethod.Post, menuUrl);
                menuReq.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
                menuReq.Headers.Add("Authorization", "Bearer " + accessToken);

                var menuResp = await _apiClient.SendAsync(menuReq);
                if (menuResp.IsSuccessStatusCode)
                {
                    string menuJson = await menuResp.Content.ReadAsStringAsync();
                    var menuData = JObject.Parse(menuJson);

                    string name = menuData.SelectToken("$..accountName..text")?.ToString() ?? "";

                    // Iterate thumbnails to get largest
                    string avatarUrl = "";
                    var thumbs = menuData.SelectTokens("$..accountPhoto..thumbnails[*]");
                    foreach (var t in thumbs)
                    {
                        string u = t["url"]?.ToString();
                        if (!string.IsNullOrEmpty(u)) avatarUrl = u;
                    }

                    // Also try header renderer
                    if (string.IsNullOrEmpty(avatarUrl))
                    {
                        thumbs = menuData.SelectTokens("$..thumbnail..thumbnails[*]");
                        foreach (var t in thumbs)
                        {
                            string u = t["url"]?.ToString();
                            if (!string.IsNullOrEmpty(u)) avatarUrl = u;
                        }
                    }

                    SaveAvatarData(name, avatarUrl);
                }
            }
            catch { }
        }

        private bool SaveAvatarData(string name, string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl)) return false;

            // Ensure https
            if (avatarUrl.StartsWith("//"))
                avatarUrl = "https:" + avatarUrl;

            var settings = ApplicationData.Current.LocalSettings.Values;
            settings["GoogleAvatarUrl"] = avatarUrl;
            if (!string.IsNullOrEmpty(name))
                settings["GoogleUserName"] = name;

            LoadHomeAvatar();
            return true;
        }

    }

    // ------------------------------------------
    // MODEL CLASSES
    // ------------------------------------------
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


