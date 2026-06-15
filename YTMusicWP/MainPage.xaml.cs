using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Playback;
using Windows.Networking.Connectivity;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading;
using Windows.Networking.BackgroundTransfer;
using Windows.Phone.UI.Input;
using Windows.ApplicationModel.DataTransfer;

namespace YTMusicWP
{
    public sealed partial class MainPage : Page
    {
        private void SetPlayPauseIcon(bool isPlaying)
        {
            Symbol sym = isPlaying ? Symbol.Pause : Symbol.Play;
            MiniPlayIcon.Symbol = sym;
            BigPlayIcon.Symbol = sym;
        }

        private static readonly HttpClient _apiClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        private ObservableCollection<YouTubeTrack> searchResults = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> homeTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> favoriteTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> downloadedTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> historyTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<string> searchSuggestions = new ObservableCollection<string>();

        private ObservableCollection<YouTubeTrack> historyQuickGridTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> homeHistoryCarouselTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> popTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> lofiTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> workoutTracks = new ObservableCollection<YouTubeTrack>();

        private ObservableCollection<UserPlaylist> userPlaylists = new ObservableCollection<UserPlaylist>();
        private UserPlaylist _currentViewingPlaylist = null;
        private YouTubeTrack _trackPendingForPlaylist = null;

        private string ProxyBaseUrl
        {
            get
            {
                if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.ContainsKey("CustomProxyUrl"))
                {
                    string url = Windows.Storage.ApplicationData.Current.LocalSettings.Values["CustomProxyUrl"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        if (url.EndsWith("/")) url = url.TrimEnd('/');
                        return url;
                    }
                }
                return "https://summer-fire-6e3f.adianhseng.workers.dev";
            }
        }

        private ObservableCollection<YouTubeTrack> currentQueueTracks = new ObservableCollection<YouTubeTrack>();

        private ObservableCollection<LyricLine> currentLyrics = new ObservableCollection<LyricLine>();
        private int currentLyricIndex = -1;

        private int _baseLyricSize = 18;
        private int _highlightLyricSize = 24;

        private DispatcherTimer _typingTimer = new DispatcherTimer();
        private YouTubeTrack currentTrack = null;
        private bool _isSliderManipulating = false;
        private Timer _bgTimer;
        private CancellationTokenSource _toastCts;
        // [OPT-C2] Token riêng cho lyrics — dừng Task cũ khi bài đổi, tránh race condition
        private CancellationTokenSource _lyricsCts;

        private ScrollViewer _cachedLyricsScrollViewer = null;

        private string _nextSearchToken = "";
        private bool _isLoadingMoreSearch = false;
        // [OPT-Q4] Tách biệt query search và query home — tránh overwrite nhau
        private string _currentSearchQuery = "";
        private string _currentHomeQuery = "";

        private bool _isAuthProcessing = false;

        private DispatcherTimer _sleepTimer;
        private int _sleepMinutesLeft = 0;
        private int _sleepTimerMode = 0;

        private MediaPlayer _appMediaPlayer;

        // [OPT-M6] Cache static brushes — avoid creating new objects every second/click (512MB RAM)
        private static readonly SolidColorBrush _lyricActiveBrush   = new SolidColorBrush(Windows.UI.Colors.White);
        private static readonly SolidColorBrush _lyricInactiveBrush = new SolidColorBrush(Windows.UI.Colors.Gray);
        private static readonly SolidColorBrush _greenBrush = new SolidColorBrush(Windows.UI.Colors.Green);
        private static readonly SolidColorBrush _whiteBrush = new SolidColorBrush(Windows.UI.Colors.White);

        // Secret key cho Render server API authentication
        private const string _apiSecretKey = "LumiaWP81-An";

        public MainPage()
        {
            this.InitializeComponent();

            _appMediaPlayer = BackgroundMediaPlayer.Current;

            SearchSongList.ItemsSource = searchResults;

            HomeTrendingCarousel.ItemsSource = homeTracks;
            HomeHistoryCarousel.ItemsSource = homeHistoryCarouselTracks;
            HomeQuickGrid.ItemsSource = historyQuickGridTracks;
            HomePopCarousel.ItemsSource = popTracks;
            HomeLofiCarousel.ItemsSource = lofiTracks;
            HomeWorkoutCarousel.ItemsSource = workoutTracks;

            FavoriteSongList.ItemsSource = favoriteTracks;
            DownloadedSongList.ItemsSource = downloadedTracks;
            SuggestionList.ItemsSource = searchSuggestions;
            HistorySongList.ItemsSource = historyTracks;
            LyricsListView.ItemsSource = currentLyrics;
            QueueListView.ItemsSource = currentQueueTracks;
            PlaylistsListView.ItemsSource = userPlaylists;
            DialogPlaylistList.ItemsSource = userPlaylists;

            _typingTimer.Interval = TimeSpan.FromMilliseconds(400);
            _typingTimer.Tick += TypingTimer_Tick;

            _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _sleepTimer.Tick += SleepTimer_Tick;

            LoadSettings();
            SetupTimer();
            UpdateGreetingText();

            BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            Application.Current.Suspending += Current_Suspending;
            Application.Current.Resuming += Current_Resuming;
        }

        private void HomeQuickGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var wrapGrid = HomeQuickGrid.ItemsPanelRoot as ItemsWrapGrid;
            if (wrapGrid != null && e.NewSize.Width > 0)
            {
                wrapGrid.ItemWidth = Math.Max(0, e.NewSize.Width / 2);
            }
        }

        private void UpdateGreetingText()
        {
            int hour = DateTime.Now.Hour;
            if (hour < 12) GreetingText.Text = "Good morning";
            else if (hour < 18) GreetingText.Text = "Good afternoon";
            else GreetingText.Text = "Good evening";
        }

        private void Current_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            try
            {
                if (_bgTimer != null) _bgTimer.Change(Timeout.Infinite, Timeout.Infinite);
                BackgroundMediaPlayer.MessageReceivedFromBackground -= BackgroundMediaPlayer_MessageReceivedFromBackground;
                _appMediaPlayer.CurrentStateChanged -= BackgroundMediaPlayer_CurrentStateChanged;
            }
            catch { }
        }

        private void Current_Resuming(object sender, object e)
        {
            try
            {
                UpdateGreetingText();

                // FIX: Làm mới lại liên kết với OS Player khi bị ngắt kết nối do ngủ đông
                _appMediaPlayer = BackgroundMediaPlayer.Current;
                _isSliderManipulating = false; // Mở khóa thanh tua nhạc nếu bị kẹt

                if (_bgTimer != null) _bgTimer.Change(0, 1000);
                // FIX #5: Unsubscribe trước để tránh double subscription khi fast resume
                BackgroundMediaPlayer.MessageReceivedFromBackground -= BackgroundMediaPlayer_MessageReceivedFromBackground;
                BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
                _appMediaPlayer.CurrentStateChanged -= BackgroundMediaPlayer_CurrentStateChanged;
                _appMediaPlayer.CurrentStateChanged += BackgroundMediaPlayer_CurrentStateChanged;
                SyncBackgroundPlayer();
            }
            catch { }
        }

        private ScrollViewer GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer) return depObj as ScrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            if (CustomBottomSheet.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseBottomSheet_Click(null, null);
            }
            else if (SettingsPanel.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseSettings_Click(null, null);
            }
            else if (ArtistProfileView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseArtistProfile_Click(null, null);
            }
            else if (LoginWebContainer.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseLoginWeb_Click(null, null);
            }
            else if (CreatePlaylistDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CreatePlaylistDialog.Visibility = Visibility.Collapsed;
            }
            else if (AddToPlaylistDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                AddToPlaylistDialog.Visibility = Visibility.Collapsed;
            }
            else if (NowPlayingMenuDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseNowPlayingMenu_Click(null, null);
            }
            else if (PlaylistDetailsView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                ClosePlaylistDetails_Click(null, null);
            }
            else if (NowPlayingView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseNowPlaying_Click(null, null);
            }
            else if (SuggestionPopup.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                SuggestionPopup.Visibility = Visibility.Collapsed;
            }
        }

        // ── Bottom Navigation ──
        private int _currentTab = 0; // 0=Home, 1=Search, 2=Library

        private void NavHome_Click(object sender, RoutedEventArgs e) { SwitchTab(0); }
        private void NavSearch_Click(object sender, RoutedEventArgs e) { SwitchTab(1); }
        private void NavLibrary_Click(object sender, RoutedEventArgs e) { SwitchTab(2); }

        private void SwitchTab(int tab)
        {
            _currentTab = tab;
            HomePanel.Visibility = (tab == 0) ? Visibility.Visible : Visibility.Collapsed;
            SearchPanel.Visibility = (tab == 1) ? Visibility.Visible : Visibility.Collapsed;
            LibraryPanel.Visibility = (tab == 2) ? Visibility.Visible : Visibility.Collapsed;

            // Active tab = White, inactive = #B3B3B3
            var activeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            var inactiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 179, 179, 179));

            NavHomeIcon.Fill = (tab == 0) ? activeBrush : inactiveBrush;
            NavHomeText.Foreground = (tab == 0) ? activeBrush : inactiveBrush;
            NavHomeText.FontWeight = (tab == 0) ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.Normal;

            // NavSearchIcon is a Canvas with Path children
            var searchBrush = (tab == 1) ? activeBrush : inactiveBrush;
            foreach (var child in NavSearchIcon.Children)
            {
                var p = child as Windows.UI.Xaml.Shapes.Path;
                if (p != null) p.Fill = searchBrush;
            }
            NavSearchText.Foreground = (tab == 1) ? activeBrush : inactiveBrush;
            NavSearchText.FontWeight = (tab == 1) ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.Normal;

            NavLibraryIcon.Fill = (tab == 2) ? activeBrush : inactiveBrush;
            NavLibraryText.Foreground = (tab == 2) ? activeBrush : inactiveBrush;
            NavLibraryText.FontWeight = (tab == 2) ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.Normal;
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Visible;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
        }

        private async void ShowToast(string message, int durationMs = 2500)
        {
            // Dispose CTS cũ trước để tránh memory leak, rồi cancel
            var oldCts = _toastCts;
            _toastCts = new CancellationTokenSource();
            if (oldCts != null) { oldCts.Cancel(); oldCts.Dispose(); }
            var token = _toastCts.Token;

            ToastText.Text = message;
            ToastNotification.Visibility = Visibility.Visible;
            ToastFadeInStoryboard.Begin();
            try
            {
                await Task.Delay(durationMs, token);
                ToastFadeOutStoryboard.Begin();
            }
            catch (OperationCanceledException) { /* Toast mới đã thay thế */ }
        }

        private bool IsInternetAvailable()
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return (profile != null && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);
        }

        private void SyncBackgroundPlayer()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings.Values;

                if (_appMediaPlayer.CurrentState == MediaPlayerState.Playing || _appMediaPlayer.CurrentState == MediaPlayerState.Paused)
                {
                    bool isPlaying = (_appMediaPlayer.CurrentState == MediaPlayerState.Playing);
                    SetPlayPauseIcon(isPlaying);

                    string title = localSettings.ContainsKey("CurrentTitle") ? localSettings["CurrentTitle"].ToString() : "Unknown";
                    string artist = localSettings.ContainsKey("CurrentArtist") ? localSettings["CurrentArtist"].ToString() : "Unknown";
                    string vid = localSettings.ContainsKey("CurrentVideoId") ? localSettings["CurrentVideoId"].ToString() : "";
                    string thumb = localSettings.ContainsKey("CurrentThumbnail") ? localSettings["CurrentThumbnail"].ToString() : "";

                    // [OPT-M8] Skip nếu track đã sync — tránh tạo BitmapImage mới khi resume
                    if (currentTrack != null && currentTrack.VideoId == vid)
                    {
                        return;
                    }

                    MiniTitle.Text = title; BigTitle.Text = title;
                    MiniArtist.Text = artist; BigArtist.Text = artist;

                    MenuTitle.Text = title;
                    MenuArtist.Text = artist;

                    if (!string.IsNullOrEmpty(thumb))
                    {
                        try
                        {
                            var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            bigBmp.DecodePixelWidth = 360;
                            bigBmp.UriSource = new Uri(GetHighResThumbnail(thumb), UriKind.Absolute);
                            BigCoverImage.ImageSource = bigBmp;
                            AlbumArtEntranceStoryboard.Begin();
                            MenuCoverImage.ImageSource = bigBmp;

                            var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            miniBmp.DecodePixelWidth = 100;
                            miniBmp.UriSource = new Uri(thumb, UriKind.Absolute);
                            MiniCoverImage.ImageSource = miniBmp;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(vid))
                    {
                        currentTrack = new YouTubeTrack { VideoId = vid, Title = title, ChannelName = artist, ThumbnailUrl = thumb };
                        bool isFav = favoriteTracks.Any(t => t.VideoId == vid);
                        BigHeartBtn.Content = isFav ? "♥" : "♡";
                        BigHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;

                        var ignored = UpdateLyricsAsync(title, artist);
                    }
                }
            }
            catch { }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            DataTransferManager.GetForCurrentView().DataRequested += MainPage_DataRequested;

            await LoadFavoritesAsync();
            await LoadDownloadsAsync();
            await LoadHistoryAsync();
            await LoadPlaylistsAsync();

            SyncBackgroundPlayer();

            if (!IsInternetAvailable())
            {
                ShowToast("Offline Mode. Play downloads in Library.");
            }
            else
            {
                if (string.IsNullOrEmpty(ApiKeyTextBox.Text.Trim()))
                {
                    ShowToast("Swipe to Settings and add API Key.");
                }
                
                if (homeTracks.Count == 0) 
                {
                    await LoadHomeRecommendations();
                }
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DataTransferManager.GetForCurrentView().DataRequested -= MainPage_DataRequested;
        }

        private async Task LoadFavoritesAsync()
        {
            if (favoriteTracks.Count > 0) return;
            try
            {
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                StorageFile file = await folder.GetFileAsync("favorites.json");
                string json = await FileIO.ReadTextAsync(file);
                JArray array = JArray.Parse(json);
                favoriteTracks.Clear();
                foreach (var item in array)
                {
                    favoriteTracks.Add(new YouTubeTrack
                    {
                        VideoId = item["VideoId"]?.ToString(),
                        Title = item["Title"]?.ToString(),
                        ChannelName = item["ChannelName"]?.ToString(),
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString()
                    });
                }
            }
            catch { }
        }

        private async Task LoadHistoryAsync()
        {
            if (historyTracks.Count > 0) return;
            try
            {
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                StorageFile file = await folder.GetFileAsync("history.json");
                string json = await FileIO.ReadTextAsync(file);
                JArray array = JArray.Parse(json);
                historyTracks.Clear();
                foreach (var item in array)
                {
                    historyTracks.Add(new YouTubeTrack
                    {
                        VideoId = item["VideoId"]?.ToString(),
                        Title = item["Title"]?.ToString(),
                        ChannelName = item["ChannelName"]?.ToString(),
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString()
                    });
                }
                RefreshHomeHistorySections();
            }
            catch { }
        }

        private async Task LoadDownloadsAsync()
        {
            try
            {
                var files = await ApplicationData.Current.LocalFolder.GetFilesAsync();

                // FIX #4: Smart diff — chỉ thêm/xóa item thay đổi, tránh UI flicker
                var currentFileNames = new HashSet<string>();
                foreach (var file in files)
                {
                    if (file.Name.EndsWith(".m4a") && !file.Name.StartsWith("temp_play_"))
                    {
                        currentFileNames.Add(file.Name);
                        string localId = "LOCAL:" + file.Name;
                        if (!downloadedTracks.Any(t => t.VideoId == localId))
                        {
                            downloadedTracks.Add(new YouTubeTrack
                            {
                                VideoId = localId,
                                Title = file.Name.Replace(".m4a", ""),
                                ChannelName = "Offline Track",
                                ThumbnailUrl = "ms-appx:///Assets/Square71x71Logo.scale-240.png"
                            });
                        }
                    }
                }

                // Xóa các track đã bị xóa khỏi ổ đĩa
                for (int i = downloadedTracks.Count - 1; i >= 0; i--)
                {
                    string fileName = downloadedTracks[i].VideoId.Replace("LOCAL:", "");
                    if (!currentFileNames.Contains(fileName))
                    {
                        downloadedTracks.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        private async Task LoadPlaylistsAsync()
        {
            if (userPlaylists.Count > 0) return;
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync("playlists.json");
                string json = await FileIO.ReadTextAsync(file);
                JArray pArray = JArray.Parse(json);
                userPlaylists.Clear();
                foreach (var pObj in pArray)
                {
                    UserPlaylist up = new UserPlaylist { Name = pObj["Name"]?.ToString() };
                    var tArray = pObj["Tracks"] as JArray;
                    if (tArray != null)
                    {
                        foreach (var item in tArray)
                        {
                            up.Tracks.Add(new YouTubeTrack
                            {
                                VideoId = item["VideoId"]?.ToString(),
                                Title = item["Title"]?.ToString(),
                                ChannelName = item["ChannelName"]?.ToString(),
                                ThumbnailUrl = item["ThumbnailUrl"]?.ToString()
                            });
                        }
                    }
                    userPlaylists.Add(up);
                }
            }
            catch { }
        }

        private async void SavePlaylistsAsync()
        {
            try
            {
                JArray pArray = new JArray();
                foreach (var p in userPlaylists)
                {
                    JObject pObj = new JObject();
                    pObj["Name"] = p.Name;
                    JArray tArray = new JArray();
                    foreach (var t in p.Tracks)
                    {
                        JObject tObj = new JObject();
                        tObj["VideoId"] = t.VideoId; tObj["Title"] = t.Title;
                        tObj["ChannelName"] = t.ChannelName; tObj["ThumbnailUrl"] = t.ThumbnailUrl;
                        tArray.Add(tObj);
                    }
                    pObj["Tracks"] = tArray;
                    pArray.Add(pObj);
                }
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("playlists.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, pArray.ToString());
            }
            catch { }
        }

        private void OpenCreatePlaylistDialog_Click(object sender, RoutedEventArgs e)
        {
            NewPlaylistNameTextBox.Text = "";
            CreatePlaylistDialog.Visibility = Visibility.Visible;
        }

        private YouTubeTrack _trackToShare;

        private async void BackgroundMediaPlayer_MessageReceivedFromBackground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            if (e.Data.ContainsKey("ToastMessage"))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ShowToast(e.Data["ToastMessage"].ToString());
                });
            }

            if (e.Data.ContainsKey("TrackChanged"))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    string title = e.Data["NewTitle"]?.ToString() ?? "";
                    string artist = e.Data["NewArtist"]?.ToString() ?? "";
                    string vid = e.Data.ContainsKey("NewVideoId") ? e.Data["NewVideoId"].ToString() : "";
                    string thumb = e.Data.ContainsKey("NewThumbnail") ? e.Data["NewThumbnail"].ToString() : "";

                    MiniTitle.Text = title; BigTitle.Text = title;
                    MiniArtist.Text = artist; BigArtist.Text = artist;

                    MenuTitle.Text = title;
                    MenuArtist.Text = artist;

                    if (!string.IsNullOrEmpty(thumb))
                    {
                        try
                        {
                            var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            bigBmp.DecodePixelWidth = 360;
                            bigBmp.UriSource = new Uri(GetHighResThumbnail(thumb), UriKind.Absolute);
                            BigCoverImage.ImageSource = bigBmp;
                            AlbumArtEntranceStoryboard.Begin();
                            MenuCoverImage.ImageSource = bigBmp;

                            var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            miniBmp.DecodePixelWidth = 100;
                            miniBmp.UriSource = new Uri(thumb, UriKind.Absolute);
                            MiniCoverImage.ImageSource = miniBmp;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(vid))
                    {
                        currentTrack = new YouTubeTrack { VideoId = vid, Title = title, ChannelName = artist, ThumbnailUrl = thumb };

                        bool isFav = favoriteTracks.Any(t => t.VideoId == vid);
                        BigHeartBtn.Content = isFav ? "♥" : "♡";
                        BigHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;

                        var ignored = UpdateLyricsAsync(title, artist);
                    }
                });
            }
        }

        private static YouTubeTrack ParseTrackItem(JToken item)
        {
            string vidId = item["id"]?["videoId"]?.ToString();
            if (string.IsNullOrEmpty(vidId)) vidId = item["id"]?["channelId"]?.ToString() != null ? "CHANNEL:" + item["id"]["channelId"].ToString() : null;
            if (string.IsNullOrEmpty(vidId)) vidId = item["id"]?["playlistId"]?.ToString() != null ? "PLAYLIST:" + item["id"]["playlistId"].ToString() : null;
            if (string.IsNullOrEmpty(vidId)) return null;

            string title   = System.Net.WebUtility.HtmlDecode(item["snippet"]?["title"]?.ToString() ?? "");
            string channel = CleanChannelName(System.Net.WebUtility.HtmlDecode(item["snippet"]?["channelTitle"]?.ToString() ?? ""));
            string channelId = item["snippet"]?["channelId"]?.ToString() ?? item["id"]?["channelId"]?.ToString();
            var    thumbs  = item["snippet"]?["thumbnails"];
            string thumbUrl = thumbs?["maxres"]?["url"]?.ToString()
                           ?? thumbs?["standard"]?["url"]?.ToString()
                           ?? thumbs?["high"]?["url"]?.ToString()
                           ?? thumbs?["medium"]?["url"]?.ToString()
                           ?? thumbs?["default"]?["url"]?.ToString();
            return new YouTubeTrack { VideoId = vidId, Title = title, ChannelName = channel, ChannelId = channelId, ThumbnailUrl = thumbUrl };
        }

        private static string CleanChannelName(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return channel;
            if (channel == "Nghệ sĩ") return "Artist";
            if (channel.EndsWith(" - Topic")) return channel.Substring(0, channel.Length - 8);
            if (channel.EndsWith(" - Chủ đề")) return channel.Substring(0, channel.Length - 9);
            return channel;
        }

        private static string GetHighResThumbnail(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            // [OPT-M7] Giảm 1080→480 cho ảnh bìa — tiết kiệm ~4x RAM khi decode trên thiết bị 512MB
            if (url.Contains("=w120-h120"))
                return url.Replace("=w120-h120", "=w480-h480");
            if (url.Contains("=w60-h60"))
                return url.Replace("=w60-h60", "=w480-h480");
            if (url.Contains("=w226-h226"))
                return url.Replace("=w226-h226", "=w480-h480");
            if (url.Contains("hqdefault.jpg"))
                return url.Replace("hqdefault.jpg", "hqdefault.jpg"); // giữ hqdefault (480x360), ko dùng maxresdefault (1280x720)
            if (url.Contains("mqdefault.jpg"))
                return url.Replace("mqdefault.jpg", "hqdefault.jpg");
            if (url.Contains("-s120-"))
                return url.Replace("-s120-", "-s480-");
            if (url.Contains("-s68-"))
                return url.Replace("-s68-", "-s480-");
            return url;
        }

        public async Task<List<YouTubeTrack>> FetchMusicList(string query, string pageToken = "")
        {
            var list = new List<YouTubeTrack>(8);

            // --- LỚP 1 (ƯU TIÊN): INNERTUBE TRỰC TIẾP (không cần proxy) ---
            try
            {
                var innerResults = await InnerTubeClient.SearchAsync(query, 12);
                if (innerResults != null && innerResults.Count > 0)
                {
                    list.AddRange(innerResults);
                    return list;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine("FetchMusicList InnerTube lỗi, chuyển sang Lớp 2."); }

            // --- LỚP 2: YOUTUBE API V3 (CHANNELS/PLAYLISTS & FALLBACK) ---
            string apiKey = ApiKeyTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(apiKey))
            {
                // Nếu Lớp 1 đã có dữ liệu (có pageToken hay không), lấy thêm channel/playlist ở trang đầu
                if (list.Count > 0 && !string.IsNullOrEmpty(pageToken)) return list; 

                string ytUrl = "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=" + (list.Count > 0 ? "3" : "8") + "&q=" + Uri.EscapeDataString(query) + "&type=" + (list.Count > 0 ? "channel,playlist" : "video,channel,playlist") + "&key=" + apiKey;

                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
                {
                    string region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();
                    if (region != "US") ytUrl += "&regionCode=" + region;
                }
                if (!string.IsNullOrEmpty(pageToken) && list.Count == 0) ytUrl += "&pageToken=" + pageToken;

                try
                {
                    var response = await _apiClient.GetStringAsync(ytUrl);
                    var json = JObject.Parse(response);
                    var items = json["items"];
                    if (items != null)
                    {
                        var ytList = new List<YouTubeTrack>();
                        foreach (var item in items)
                        {
                            try { var track = ParseTrackItem(item); if (track != null) ytList.Add(track); }
                            catch { continue; }
                        }
                        
                        if (list.Count > 0)
                        {
                            ytList.AddRange(list);
                            return ytList;
                        }
                        else
                        {
                            return ytList;
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        private static readonly SolidColorBrush _filterActiveBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 185, 84)); // #1DB954
        private static readonly SolidColorBrush _filterInactiveBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)); // #232323

        // [REMOVED] SearchYouTubeMusic() — dead code, tất cả search paths đều dùng FetchMusicList()

        private void SongList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var track = e.ClickedItem as YouTubeTrack;
            if (track != null) PlayTrack(track);
        }

        private TimeSpan ParseLrcTime(string timeStr)
        {
            try
            {
                int colonIdx = timeStr.IndexOf(':');
                int dotIdx = timeStr.IndexOf('.');

                if (colonIdx > 0)
                {
                    int min = int.Parse(timeStr.Substring(0, colonIdx));
                    int sec = 0;
                    int ms = 0;

                    if (dotIdx > 0)
                    {
                        sec = int.Parse(timeStr.Substring(colonIdx + 1, dotIdx - colonIdx - 1));
                        string msStr = timeStr.Substring(dotIdx + 1).PadRight(3, '0');
                        if (msStr.Length > 3) msStr = msStr.Substring(0, 3);
                        ms = int.Parse(msStr);
                    }
                    else
                    {
                        sec = int.Parse(timeStr.Substring(colonIdx + 1));
                    }
                    return new TimeSpan(0, 0, min, sec, ms);
                }
            }
            catch { }
            return TimeSpan.Zero;
        }

        private async Task UpdateLyricsAsync(string title, string artist)
        {
            // [OPT-C2] Hủy Task lyrics cũ trước khi bắt đầu Task mới — tránh race condition khi skip bài nhanh
            var oldLyricsCts = _lyricsCts;
            _lyricsCts = new CancellationTokenSource();
            if (oldLyricsCts != null) { oldLyricsCts.Cancel(); oldLyricsCts.Dispose(); }
            var token = _lyricsCts.Token;

            currentLyrics.Clear();
            currentLyricIndex = -1;
            _cachedLyricsScrollViewer = null;

            LyricsFallbackScrollViewer.Visibility = Visibility.Collapsed;
            LyricsListView.Visibility = Visibility.Visible;
            LyricsLoadingBar.Visibility = Visibility.Visible;

            try
            {
                if (title.StartsWith("LOCAL:"))
                {
                    LyricsFallbackText.Text = "No lyrics for offline files.";
                    LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                    LyricsLoadingBar.Visibility = Visibility.Collapsed;
                    return;
                }

                string cleanTitle = title
                    .Replace(" (Official Video)", "").Replace(" (Lyric Video)", "")
                    .Replace(" (Official Audio)", "").Replace(" (Audio)", "")
                    .Replace(" (Official MV)", "").Trim();
                string cleanArtist = CleanChannelName(artist);

                token.ThrowIfCancellationRequested();

                string syncedLyrics = null;
                string plainLyrics = null;

                // If artist is a type label ("Song", "Video", etc.), try to extract from title
                string[] typeLabels = { "Song", "Video", "Artist", "Playlist", "Album", "EP", "Single", "" };
                if (Array.IndexOf(typeLabels, cleanArtist) >= 0)
                {
                    // Title often has "ArtistName - SongName" format from YouTube  
                    if (title.Contains(" - "))
                    {
                        var parts = title.Split(new[] { " - " }, StringSplitOptions.None);
                        if (parts.Length >= 2)
                        {
                            cleanArtist = parts[0].Trim();
                            cleanTitle = parts[1].Replace(" (Official Video)", "").Replace(" (Lyric Video)", "")
                                .Replace(" (Official Audio)", "").Replace(" (Audio)", "")
                                .Replace(" (Official MV)", "").Trim();
                        }
                    }
                }
                else if (cleanTitle.Contains(" - "))
                {
                    // Artist is known, title might be "ArtistName - SongName" → take song part
                    var titleParts = cleanTitle.Split(new[] { " - " }, StringSplitOptions.None);
                    if (titleParts.Length >= 2)
                    {
                        // Check if first part matches the artist → take second part as song title
                        if (titleParts[0].Trim().Equals(cleanArtist, StringComparison.OrdinalIgnoreCase))
                        {
                            cleanTitle = titleParts[1].Trim();
                        }
                    }
                }

                // --- LỚP 1 (ƯU TIÊN): LRCLIB.NET exact match ---
                try
                {
                    string lrcUrl = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) + "&artist_name=" + Uri.EscapeDataString(cleanArtist);
                    var lrcResp = await _apiClient.GetStringAsync(lrcUrl);
                    token.ThrowIfCancellationRequested();
                    var lrcArr = JArray.Parse(lrcResp);
                    if (lrcArr.Count > 0)
                    {
                        // Prefer result with synced lyrics
                        foreach (var item in lrcArr)
                        {
                            string s = item["syncedLyrics"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(s))
                            {
                                syncedLyrics = s;
                                plainLyrics = item["plainLyrics"]?.ToString();
                                break;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(syncedLyrics))
                        {
                            syncedLyrics = lrcArr[0]["syncedLyrics"]?.ToString();
                            plainLyrics = lrcArr[0]["plainLyrics"]?.ToString();
                        }
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine("Lyrics Lớp 1 lỗi, chuyển sang Lớp 2."); }

                // --- LỚP 2 (DỰ PHÒNG): LRCLIB.NET q= broad search ---
                if (string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    try
                    {
                        string url2 = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(cleanTitle + " " + cleanArtist);
                        var response2 = await _apiClient.GetStringAsync(url2);
                        token.ThrowIfCancellationRequested();
                        var jsonArray2 = JArray.Parse(response2);
                        if (jsonArray2.Count > 0)
                        {
                            // Prefer result with synced lyrics
                            foreach (var item in jsonArray2)
                            {
                                string s = item["syncedLyrics"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(s))
                                {
                                    syncedLyrics = s;
                                    if (string.IsNullOrWhiteSpace(plainLyrics))
                                        plainLyrics = item["plainLyrics"]?.ToString();
                                    break;
                                }
                            }
                            if (string.IsNullOrWhiteSpace(syncedLyrics) && string.IsNullOrWhiteSpace(plainLyrics))
                            {
                                plainLyrics = jsonArray2[0]["plainLyrics"]?.ToString();
                            }
                        }
                    }
                    catch { }
                }

                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    var lines = syncedLyrics.Split('\n');
                    var parsedLines = new List<LyricLine>(lines.Length);

                    foreach (var line in lines)
                    {
                        string tempLine = line.Trim();
                        var times = new List<TimeSpan>(2);

                        while (tempLine.StartsWith("[") && tempLine.IndexOf(']') > 0)
                        {
                            int bracketEnd = tempLine.IndexOf(']');
                            string timeStr = tempLine.Substring(1, bracketEnd - 1);
                            times.Add(ParseLrcTime(timeStr));
                            tempLine = tempLine.Substring(bracketEnd + 1).Trim();
                        }

                        if (times.Count > 0)
                        {
                            string text = string.IsNullOrWhiteSpace(tempLine) ? "♪" : tempLine;
                            foreach (var t in times)
                                parsedLines.Add(new LyricLine { Time = t, Text = text });
                        }
                    }

                    parsedLines.Sort((a, b) => a.Time.CompareTo(b.Time));
                    foreach (var p in parsedLines) currentLyrics.Add(p);
                    currentLyrics.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "" });
                }
                else if (!string.IsNullOrWhiteSpace(plainLyrics))
                {
                    LyricsFallbackText.Text = plainLyrics;
                    LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LyricsFallbackText.Text = "Lyrics not found for this track.";
                    LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
                // Bài hát đã đổi — bỏ qua, Task mới sẽ lo
                return;
            }
            catch
            {
                LyricsFallbackText.Text = "Failed to load lyrics. Please check your connection.";
                LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                LyricsListView.Visibility = Visibility.Collapsed;
            }
            finally
            {
                LyricsLoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private void LyricsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var line = e.ClickedItem as LyricLine;
            if (line != null && line.Text != "♪" && line.Text != "")
            {
                try
                {
                    if (_appMediaPlayer.CurrentState != MediaPlayerState.Closed)
                    {
                        _appMediaPlayer.Position = line.Time;
                        if (_appMediaPlayer.CurrentState == MediaPlayerState.Paused) _appMediaPlayer.Play();
                    }
                }
                catch { }
            }
        }

        private YouTubeTrack _bottomSheetTrack;

        private void ToastFadeOutStoryboard_Completed(object sender, object e)
        {
            ToastNotification.Visibility = Visibility.Collapsed;
        }

    }
}
