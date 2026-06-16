using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
        private void LoadSettings()
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (settings.ContainsKey("YouTubeApiKey")) ApiKeyTextBox.Text = settings["YouTubeApiKey"].ToString();
            if (settings.ContainsKey("GoogleClientId")) ClientIdTextBox.Text = settings["GoogleClientId"].ToString();
            if (settings.ContainsKey("GoogleClientSecret")) ClientSecretTextBox.Text = settings["GoogleClientSecret"].ToString();
            if (settings.ContainsKey("CustomProxyUrl")) ProxyUrlTextBox.Text = settings["CustomProxyUrl"].ToString();

            if (settings.ContainsKey("TrendingRegion"))
            {
                string r = settings["TrendingRegion"].ToString();
                for (int i = 0; i < RegionComboBox.Items.Count; i++)
                {
                    if (((ComboBoxItem)RegionComboBox.Items[i]).Tag.ToString() == r)
                    {
                        RegionComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }
            else RegionComboBox.SelectedIndex = 0;

            if (settings.ContainsKey("GoogleAccessToken"))
            {
                LoginStatusText.Text = "Status: Logged In & Synced!";
                LoginStatusText.Foreground = _greenBrush;
            }
            bool isShuffle = settings.ContainsKey("ShuffleMode") ? (bool)settings["ShuffleMode"] : false;
            ShuffleIcon.Foreground = isShuffle ? _greenBrush : _whiteBrush;
            int repeatMode = settings.ContainsKey("RepeatMode") ? (int)settings["RepeatMode"] : 0;
            UpdateRepeatUI(repeatMode);
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            string newKey = ApiKeyTextBox.Text.Trim();
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["YouTubeApiKey"] = newKey;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["GoogleClientId"] = ClientIdTextBox.Text.Trim();
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["GoogleClientSecret"] = ClientSecretTextBox.Text.Trim();
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["CustomProxyUrl"] = ProxyUrlTextBox.Text.Trim();

            var selectedRegion = RegionComboBox.SelectedItem as ComboBoxItem;
            if (selectedRegion != null && selectedRegion.Tag != null)
            {
                ApplicationData.Current.LocalSettings.Values["TrendingRegion"] = selectedRegion.Tag.ToString();
            }

            ShowToast("Settings Saved!");

            if (IsInternetAvailable())
            {
                homeTracks.Clear();
                popTracks.Clear();
                lofiTracks.Clear();
                workoutTracks.Clear();
                await LoadHomeRecommendations();
            }
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

        private void LoginGoogle_Click(object sender, RoutedEventArgs e)
        {
            string clientId = ClientIdTextBox.Text.Trim();
            string clientSecret = ClientSecretTextBox.Text.Trim();
            string inputCode = AuthCodeTextBox.Text.Trim();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                ShowToast("Please enter Client ID and Secret first!");
                return;
            }

            if (!string.IsNullOrEmpty(inputCode))
            {
                ExtractAndProcessCode(inputCode);
                return;
            }

            string authUrl = "https://accounts.google.com/o/oauth2/v2/auth?" +
                             "client_id=" + Uri.EscapeDataString(clientId) +
                             "&redirect_uri=http://localhost" +
                             "&response_type=code" +
                             "&scope=https://www.googleapis.com/auth/youtube" +
                             "&access_type=offline";

            _isAuthProcessing = false;
            LoginWebContainer.Visibility = Visibility.Visible;
            try { LoginWebView.Navigate(new Uri(authUrl)); } catch { }
        }

        private void CloseLoginWeb_Click(object sender, RoutedEventArgs e)
        {
            LoginWebContainer.Visibility = Visibility.Collapsed;
            _isAuthProcessing = false;
            try { LoginWebView.Navigate(new Uri("about:blank")); } catch { }
        }

        private async void ExtractAndProcessCode(string url)
        {
            if (_isAuthProcessing) return;
            _isAuthProcessing = true;

            LoginWebContainer.Visibility = Visibility.Collapsed;
            LoginWebLoading.Visibility = Visibility.Collapsed;
            try { LoginWebView.Navigate(new Uri("about:blank")); } catch { }

            string authCode = "";
            try
            {
                int codeIndex = url.IndexOf("code=");
                if (codeIndex > -1)
                {
                    int startIndex = codeIndex + 5;
                    int endIndex = url.IndexOf("&", startIndex);
                    if (endIndex > -1) authCode = url.Substring(startIndex, endIndex - startIndex);
                    else authCode = url.Substring(startIndex);
                }
                if (!string.IsNullOrEmpty(authCode)) authCode = Uri.UnescapeDataString(authCode);
            }
            catch { }

            if (!string.IsNullOrEmpty(authCode))
            {
                await ProcessGoogleAuthCode(authCode);
            }
            else
            {
                ShowToast("Invalid link. Please try again.");
            }
            _isAuthProcessing = false;
        }

        private void LoginWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            if (args.Uri == null) return;
            string url = args.Uri.ToString();

            if (url.Contains("localhost") && url.Contains("code="))
            {
                args.Cancel = true;
                ExtractAndProcessCode(url);
            }
            else if (url.Contains("localhost") && url.Contains("error="))
            {
                args.Cancel = true;
                LoginWebContainer.Visibility = Visibility.Collapsed;
                ShowToast("Access denied by user.");
            }
            else
            {
                LoginWebLoading.Visibility = Visibility.Visible;
            }
        }

        private void LoginWebView_NavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            LoginWebLoading.Visibility = Visibility.Collapsed;
            if (e.Uri != null)
            {
                string url = e.Uri.ToString();
                if (url.Contains("localhost") && url.Contains("code=")) ExtractAndProcessCode(url);
            }
        }

        private void LoginWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            LoginWebLoading.Visibility = Visibility.Collapsed;
        }

        private async Task ProcessGoogleAuthCode(string authCode)
        {
            string clientId = ClientIdTextBox.Text.Trim();
            string clientSecret = ClientSecretTextBox.Text.Trim();

            LoginStatusText.Text = "Status: Authenticating...";
            LoginStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);

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
                    settings["GoogleTokenExpiry"] = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToUnixTimeSeconds();

                    ShowToast("Login successful! Syncing...");
                    await SyncAllAsync(accessToken);
                }
                else
                {
                    LoginStatusText.Text = "Status: Auth Failed";
                    LoginStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);
                    ShowToast("Auth Error! Please try again.");
                }
            }
            catch
            {
                LoginStatusText.Text = "Status: Network Error";
                LoginStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);
                ShowToast("Network error. Please try again.");
            }
        }

        // ══════════════════════════════════════════
        // SYNC LIKED VIDEOS
        // ══════════════════════════════════════════
        private async Task SyncLikedVideosAsync(string accessToken)
        {
            try
            {
                LoginStatusText.Text = "Status: Syncing Liked Songs...";
                string url = "https://www.googleapis.com/youtube/v3/videos?myRating=like&part=snippet&maxResults=50";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "Bearer " + accessToken);

                var response = await _apiClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string resultJson = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(resultJson);

                    bool hasNew = false;
                    var itemsToken = json["items"];
                    if (itemsToken == null) { LoginStatusText.Text = "Status: API Quota Exceeded"; return; }
                    var items = itemsToken.Reverse();

                    foreach (var item in items)
                    {
                        try
                        {
                            string vidId = item["id"]?.ToString();
                            if (string.IsNullOrEmpty(vidId)) continue;
                            if (favoriteTracks.Any(t => t.VideoId == vidId)) continue;

                            string title = System.Net.WebUtility.HtmlDecode(item["snippet"]?["title"]?.ToString() ?? "");
                            string channel = CleanChannelName(System.Net.WebUtility.HtmlDecode(item["snippet"]?["channelTitle"]?.ToString() ?? ""));

                            var thumbs = item["snippet"]?["thumbnails"];
                            string thumbUrl = thumbs?["maxres"]?["url"]?.ToString() ?? thumbs?["standard"]?["url"]?.ToString() ?? thumbs?["high"]?["url"]?.ToString() ?? thumbs?["medium"]?["url"]?.ToString();

                            favoriteTracks.Insert(0, new YouTubeTrack { VideoId = vidId, Title = title, ChannelName = channel, ThumbnailUrl = thumbUrl });
                            hasNew = true;
                        }
                        catch { continue; }
                    }

                    if (hasNew) SaveFavoritesAsync();

                    LoginStatusText.Text = "Status: Logged In & Synced!";
                    LoginStatusText.Foreground = _greenBrush;
                }
                else
                {
                    LoginStatusText.Text = "Status: Sync Failed";
                    LoginStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                }
            }
            catch
            {
                LoginStatusText.Text = "Status: Sync Error";
                LoginStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);
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
                long expiry = (long)settings["GoogleTokenExpiry"];
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiry)
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
            if (!settings.ContainsKey("GoogleRefreshToken") || !settings.ContainsKey("GoogleClientId") || !settings.ContainsKey("GoogleClientSecret")) return null;

            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", settings["GoogleClientId"].ToString()),
                    new KeyValuePair<string, string>("client_secret", settings["GoogleClientSecret"].ToString()),
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
                    settings["GoogleTokenExpiry"] = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToUnixTimeSeconds();
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
                string url = "https://www.googleapis.com/youtube/v3/playlists?part=snippet,contentDetails&mine=true&maxResults=50";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "Bearer " + accessToken);

                var response = await _apiClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return;

                string resultJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(resultJson);
                var items = json["items"];
                if (items == null) return;

                _youtubeUserPlaylists.Clear();
                foreach (var item in items)
                {
                    try
                    {
                        var snippet = item["snippet"];
                        string plId = item["id"]?.ToString();
                        string title = snippet?["title"]?.ToString() ?? "";
                        int count = item["contentDetails"]?["itemCount"]?.Value<int>() ?? 0;
                        var thumbs = snippet?["thumbnails"];
                        string thumbUrl = thumbs?["high"]?["url"]?.ToString() ?? thumbs?["medium"]?["url"]?.ToString() ?? thumbs?["default"]?["url"]?.ToString();

                        _youtubeUserPlaylists.Add(new YouTubePlaylistInfo
                        {
                            PlaylistId = plId,
                            Title = title,
                            TrackCount = count,
                            ThumbnailUrl = thumbUrl
                        });
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
                string url = "https://www.googleapis.com/youtube/v3/subscriptions?part=snippet&mine=true&maxResults=50&order=alphabetical";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "Bearer " + accessToken);

                var response = await _apiClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return;

                string resultJson = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(resultJson);
                var items = json["items"];
                if (items == null) return;

                _youtubeSubscriptions.Clear();
                foreach (var item in items)
                {
                    try
                    {
                        var snippet = item["snippet"];
                        string channelId = snippet?["resourceId"]?["channelId"]?.ToString();
                        string title = snippet?["title"]?.ToString() ?? "";
                        var thumbs = snippet?["thumbnails"];
                        string thumbUrl = thumbs?["high"]?["url"]?.ToString() ?? thumbs?["default"]?["url"]?.ToString();

                        _youtubeSubscriptions.Add(new YouTubeSubscription
                        {
                            ChannelId = channelId,
                            Title = title,
                            ThumbnailUrl = thumbUrl
                        });
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
                string url = "https://www.googleapis.com/youtube/v3/videos/rate?id=" + videoId + "&rating=" + rating;
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Authorization", "Bearer " + token);
                request.Content = new StringContent("");

                var response = await _apiClient.SendAsync(request);
                return response.IsSuccessStatusCode || (int)response.StatusCode == 204;
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
                string url = "https://www.googleapis.com/youtube/v3/playlistItems?part=snippet";
                var body = new JObject
                {
                    ["snippet"] = new JObject
                    {
                        ["playlistId"] = "WL",
                        ["resourceId"] = new JObject
                        {
                            ["kind"] = "youtube#video",
                            ["videoId"] = videoId
                        }
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Authorization", "Bearer " + token);
                request.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");

                var response = await _apiClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private async Task RefreshGoogleTokenAndSyncAsync()
        {
            string token = await RefreshGoogleTokenAsync();
            if (!string.IsNullOrEmpty(token))
                await SyncAllAsync(token);
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
