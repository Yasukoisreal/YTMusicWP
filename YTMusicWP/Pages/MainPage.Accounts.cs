using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;
using YTMusicWP.Services;

namespace YTMusicWP
{
    // ==========================================
    // MULTIPLE YOUTUBE ACCOUNTS (cookie sign-in)
    // Every identity of a signed-in browser session (each Google account, each brand channel) is listed by
    // music.youtube.com/getAccountSwitcherEndpoint and kept in AccountStore with its session cookie. Switching
    // only changes which cookie / authuser / page id InnerTubeClient sends, then re-syncs the library.
    // ==========================================
    public sealed partial class MainPage
    {
        private const string FallbackAccountName = "Google Account";
        private bool _accountSwitching;
        private bool _accountsRefreshed;

        private static readonly SolidColorBrush _accountRowBrush = new SolidColorBrush(Color.FromArgb(255, 0x25, 0x25, 0x25));
        private static readonly SolidColorBrush _accountActiveRowBrush = new SolidColorBrush(Color.FromArgb(255, 0x1B, 0x33, 0x24));
        private static readonly SolidColorBrush _accountSubtitleBrush = new SolidColorBrush(Color.FromArgb(255, 0x99, 0x99, 0x99));
        private static readonly SolidColorBrush _accountMutedBrush = new SolidColorBrush(Color.FromArgb(255, 0x88, 0x88, 0x88));
        private static readonly SolidColorBrush _accountCheckBrush = new SolidColorBrush(Color.FromArgb(255, 0x1D, 0xB9, 0x54));

        /// <summary>
        /// After a cookie sign-in: adds every identity of the new session to the account list and returns the one
        /// the browser had selected (the Google account's own channel).
        /// </summary>
        private async Task<YouTubeAccount> RegisterSessionAccountsAsync(string cookieString, string sapisid)
        {
            await AccountStore.LoadAsync();
            var found = await InnerTubeClient.GetAccountListAsync(cookieString, sapisid);
            if (found.Count == 0)
            {
                // Switcher unavailable: keep the session as a single identity; the name comes from account_menu later
                found.Add(new YouTubeAccount
                {
                    Name = FallbackAccountName,
                    AuthUser = 0,
                    CookieString = cookieString,
                    Sapisid = sapisid,
                    IsSelected = true
                });
            }
            AccountStore.Merge(found);
            await AccountStore.SaveAsync();
            return found.FirstOrDefault(a => a.IsSelected) ?? found[0];
        }

        /// <summary>Makes <paramref name="acc"/> the identity every signed-in request uses (and the one restored at startup).</summary>
        private void ApplyActiveAccount(YouTubeAccount acc)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            InnerTubeClient.SetCookieAuth(acc.CookieString, acc.Sapisid);
            InnerTubeClient.SetCookieIdentity(acc.AuthUser, acc.PageId);

            settings["GoogleCookieString"] = acc.CookieString;
            settings["GoogleSAPISID"] = acc.Sapisid;
            settings["GoogleAuthUser"] = acc.AuthUser;
            if (acc.PageId != null) settings["GooglePageId"] = acc.PageId; else settings.Remove("GooglePageId");
            if (!string.IsNullOrEmpty(acc.Name) && acc.Name != FallbackAccountName) settings["GoogleUserName"] = acc.Name;
            if (!string.IsNullOrEmpty(acc.AvatarUrl)) settings["GoogleAvatarUrl"] = acc.AvatarUrl; else settings.Remove("GoogleAvatarUrl");

            // A TV-code (OAuth) token wins over the cookie in AuthInnerTubePostAsync and belongs to whichever
            // account signed in with it, so it would keep answering as that account
            settings.Remove("GoogleAccessToken");
            settings.Remove("GoogleRefreshToken");
            settings.Remove("GoogleTokenExpiry");

            AccountStore.ActiveKey = acc.Key;

            // The WinRT HTTP client (stream resolution) shares the WebView cookie jar and adds its cookies to every
            // request; the jar holds the last session signed in through the WebView, not necessarily this account.
            // Windows Phone 8.1 cannot turn that off per client, so empty the jar: the account's cookies are stored.
            ClearWebViewGoogleCookies();
        }

        /// <summary>Fills in the active identity's name / avatar from account_menu when the switcher could not.</summary>
        private async Task UpdateActiveAccountProfileAsync()
        {
            var acc = AccountStore.Active;
            if (acc == null) return;
            var settings = ApplicationData.Current.LocalSettings.Values;
            string name = SafeGetString(settings, "GoogleUserName", "");
            string avatar = SafeGetString(settings, "GoogleAvatarUrl", "");
            bool changed = false;
            if (acc.Name == FallbackAccountName && !string.IsNullOrEmpty(name)) { acc.Name = name; changed = true; }
            if (string.IsNullOrEmpty(acc.AvatarUrl) && !string.IsNullOrEmpty(avatar)) { acc.AvatarUrl = avatar; changed = true; }
            if (!changed) return;
            AccountStore.ActiveKey = acc.Key; // the key includes the name when there is no gaia id / email
            await AccountStore.SaveAsync();
        }

        /// <summary>Drops the previous identity's synced library so the new one's can be loaded (downloads and local history stay).</summary>
        private async Task ResetAccountDataAsync()
        {
            _youtubeUserPlaylists.Clear();
            _youtubeSubscriptions.Clear();
            favoriteTracks.Clear();
            try { var f = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_playlists_cache.json"); await f.DeleteAsync(); } catch { }
            try { var f = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_subs_cache.json"); await f.DeleteAsync(); } catch { }
            RefreshLibraryList();
        }

        private async Task ReloadHomeForAccountAsync()
        {
            if (!IsInternetAvailable()) return;
            homeTracks.Clear();
            HomeDynamicSections.ItemsSource = null;
            InnerTubeClient.ClearHomeCache();
            await LoadHomeRecommendations();
        }

        /// <summary>
        /// Settings opened: loads the account list and, once per run, re-reads the active session's identities
        /// (picks up new brand channels, updated names / avatars; also adopts the account of a single-account install).
        /// </summary>
        private async Task RefreshAccountListAsync()
        {
            await AccountStore.LoadAsync();
            RenderAccountList();
            if (!InnerTubeClient.HasCookieAuth || _accountsRefreshed) return;
            _accountsRefreshed = true;

            var found = await InnerTubeClient.GetAccountListAsync();
            if (found.Count == 0) return;
            AccountStore.Merge(found);
            if (AccountStore.Active == null)
            {
                var current = found.FirstOrDefault(a => a.AuthUser == InnerTubeClient.CookieAuthUser && a.PageId == InnerTubeClient.CookiePageId)
                    ?? found.FirstOrDefault(a => a.IsSelected)
                    ?? found[0];
                AccountStore.ActiveKey = current.Key;
            }
            await AccountStore.SaveAsync();
            RenderAccountList();
        }

        private async Task SwitchToAccountAsync(YouTubeAccount acc)
        {
            if (acc == null || _accountSwitching || acc.Key == AccountStore.ActiveKey) return;
            _accountSwitching = true;
            try
            {
                ApplyActiveAccount(acc);
                UpdateAccountPanel(true, "Switching account...");
                SettingsPanel.LoginStatusText.Foreground = _authOrangeBrush;
                LoadHomeAvatar();
                RenderAccountList();
                ShowToast("Switched to " + acc.Name);

                await ResetAccountDataAsync();
                await SyncAllAsync();
                UpdateAccountPanel(true, "Logged in (Cookie)");
                SettingsPanel.LoginStatusText.Foreground = _greenBrush;
                await ReloadHomeForAccountAsync();
            }
            catch (Exception ex)
            {
                SettingsPanel.LoginStatusText.Text = "Switch error: " + ex.Message;
                SettingsPanel.LoginStatusText.Foreground = _authRedBrush;
            }
            finally
            {
                _accountSwitching = false;
            }
        }

        private async Task RemoveAccountAsync(YouTubeAccount acc)
        {
            if (acc == null || _accountSwitching) return;
            var dialog = new MessageDialog("Remove " + acc.Name + " from this device? You can add it again later.", "Remove account");
            dialog.Commands.Add(new UICommand("Remove"));
            dialog.Commands.Add(new UICommand("Cancel"));
            dialog.DefaultCommandIndex = 1;
            dialog.CancelCommandIndex = 1;
            var choice = await dialog.ShowAsync();
            if (choice == null || choice.Label != "Remove") return;

            bool wasActive = acc.Key == AccountStore.ActiveKey;
            AccountStore.Remove(acc);
            await AccountStore.SaveAsync();

            if (!wasActive)
            {
                RenderAccountList();
                return;
            }
            var next = AccountStore.Accounts.FirstOrDefault();
            if (next == null)
            {
                LogoutGoogle_Click(null, null);
                return;
            }
            await SwitchToAccountAsync(next);
        }

        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            // A fresh Google sign-in: the browser session is still signed in, so the login page would just hand the
            // same session back. Sessions already added keep working from their stored cookies.
            ClearWebViewGoogleCookies();
            LoginCookie_Click(sender, e);
        }

        private static void ClearWebViewGoogleCookies()
        {
            try
            {
                var cookieManager = new Windows.Web.Http.Filters.HttpBaseProtocolFilter().CookieManager;
                foreach (var host in new[] { "https://google.com", "https://accounts.google.com", "https://youtube.com", "https://www.youtube.com", "https://music.youtube.com" })
                {
                    foreach (var cookie in cookieManager.GetCookies(new Uri(host)))
                        cookieManager.DeleteCookie(cookie);
                }
            }
            catch { }
        }

        // ── Account list UI (built in code: a handful of rows, each with a remove button) ──

        private void RenderAccountList()
        {
            var panel = SettingsPanel.AccountListPanel;
            if (panel == null) return;
            panel.Children.Clear();

            var accounts = AccountStore.Accounts;
            bool many = accounts.Count > 1;
            SettingsPanel.AccountListSection.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.SignOutText.Text = many ? "Sign out of all accounts" : "Sign Out";
            if (!many) return;

            string activeKey = AccountStore.ActiveKey;
            foreach (var acc in accounts)
                panel.Children.Add(BuildAccountRow(acc, acc.Key == activeKey));
        }

        private FrameworkElement BuildAccountRow(YouTubeAccount acc, bool isActive)
        {
            var semiBold = (FontFamily)Application.Current.Resources["MontserratSemiBold"];
            var regular = (FontFamily)Application.Current.Resources["MontserratRegular"];

            // Avatar (initial letter until / unless the picture loads)
            var avatar = new Grid { Width = 36, Height = 36, Margin = new Thickness(0, 0, 12, 0) };
            avatar.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(255, 0x44, 0x44, 0x44)) });
            avatar.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(acc.Name) ? "?" : acc.Name.Substring(0, 1).ToUpper(),
                FontFamily = semiBold,
                FontSize = 15,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrEmpty(acc.AvatarUrl))
            {
                try
                {
                    var bmp = new BitmapImage { DecodePixelWidth = 72, UriSource = new Uri(acc.AvatarUrl, UriKind.Absolute) };
                    avatar.Children.Add(new Ellipse { Fill = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill } });
                }
                catch { }
            }

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = acc.Name ?? FallbackAccountName,
                FontFamily = semiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            string subtitle = acc.Handle ?? acc.Email;
            if (!string.IsNullOrEmpty(subtitle))
            {
                texts.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontFamily = regular,
                    FontSize = 11,
                    Foreground = _accountSubtitleBrush,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            var check = new TextBlock
            {
                Text = "✓",
                FontSize = 16,
                Foreground = _accountCheckBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                Visibility = isActive ? Visibility.Visible : Visibility.Collapsed
            };

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.Children.Add(avatar);
            Grid.SetColumn(texts, 1);
            inner.Children.Add(texts);
            Grid.SetColumn(check, 2);
            inner.Children.Add(check);

            var selectButton = new Button
            {
                Style = (Style)Application.Current.Resources["CategoryButtonStyle"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new Border
                {
                    Background = isActive ? _accountActiveRowBrush : _accountRowBrush,
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12, 10, 12, 10),
                    Child = inner
                }
            };
            selectButton.Click += async (s, e) => await SwitchToAccountAsync(acc);

            var removeButton = new Button
            {
                Style = (Style)Application.Current.Resources["IconButtonStyle"],
                Width = 44,
                MinHeight = 0,
                Content = new TextBlock { Text = "✕", FontSize = 14, Foreground = _accountMutedBrush }
            };
            removeButton.Click += async (s, e) => await RemoveAccountAsync(acc);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(selectButton);
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);
            return row;
        }
    }
}
