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

        private void RefreshHomeHistorySections()
        {
            if (historyTracks.Count > 0)
            {
                HomeHistorySection.Visibility = Visibility.Visible;
                historyQuickGridTracks.Clear();
                int countGrid = Math.Min(6, historyTracks.Count);
                for (int i = 0; i < countGrid; i++)
                {
                    historyQuickGridTracks.Add(historyTracks[i]);
                }

                homeHistoryCarouselTracks.Clear();
                int countCarousel = Math.Min(10, historyTracks.Count);
                for (int i = 0; i < countCarousel; i++)
                {
                    homeHistoryCarouselTracks.Add(historyTracks[i]);
                }
            }
            else
            {
                HomeHistorySection.Visibility = Visibility.Collapsed;
            }
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

        private void CancelCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            CreatePlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private void ConfirmCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            string name = NewPlaylistNameTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                userPlaylists.Insert(0, new UserPlaylist { Name = name });
                SavePlaylistsAsync();
                ShowToast("Playlist created.");
            }
            CreatePlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private void PlaylistItem_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                var el = sender as FrameworkElement;
                if (el != null)
                {
                    var flyout = FlyoutBase.GetAttachedFlyout(el);
                    if (flyout != null) flyout.ShowAt(el);
                }
            }
        }

        private void MenuDeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var playlist = (sender as MenuFlyoutItem)?.DataContext as UserPlaylist;
            if (playlist != null)
            {
                userPlaylists.Remove(playlist);
                SavePlaylistsAsync();
                ShowToast("Playlist deleted.");
            }
        }

        private void MenuDeletePlaylistInside_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewingPlaylist != null)
            {
                userPlaylists.Remove(_currentViewingPlaylist);
                SavePlaylistsAsync();
                ShowToast("Playlist deleted.");
                PlaylistSlideOutStoryboard.Begin();
            }
        }

        private void MenuRemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null && _currentViewingPlaylist != null)
            {
                _currentViewingPlaylist.Tracks.Remove(track);
                SavePlaylistsAsync();
                ShowToast("Removed from playlist");
            }
        }

        private void PlaylistsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            _currentViewingPlaylist = e.ClickedItem as UserPlaylist;
            if (_currentViewingPlaylist != null)
            {
                PlaylistDetailsTitle.Text = _currentViewingPlaylist.Name;
                if (_currentViewingPlaylist.Tracks != null && _currentViewingPlaylist.Tracks.Count > 0 && !string.IsNullOrEmpty(_currentViewingPlaylist.Tracks[0].ThumbnailUrl))
                {
                    PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(_currentViewingPlaylist.Tracks[0].ThumbnailUrl), UriKind.Absolute)) { DecodePixelWidth = 150 };
                    PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                }
                else
                {
                    PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                }
                PlaylistSongsList.ItemsSource = _currentViewingPlaylist.Tracks;
                PlaylistDetailsTrackCount.Text = (_currentViewingPlaylist.Tracks != null ? _currentViewingPlaylist.Tracks.Count : 0) + " tracks";
                PlaylistDetailsView.Visibility = Visibility.Visible;
                PlaylistSlideInStoryboard.Begin();
            }
        }

        private void ClosePlaylistDetails_Click(object sender, RoutedEventArgs e)
        {
            PlaylistSlideOutStoryboard.Begin();
        }

        private void PlaylistSlideOutStoryboard_Completed(object sender, object e)
        {
            PlaylistDetailsView.Visibility = Visibility.Collapsed;
            PlaylistDetailsCoverBrush.ImageSource = null;
            PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
        }

        private void PlayAllPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewingPlaylist != null && _currentViewingPlaylist.Tracks.Count > 0)
            {
                PlayTrack(_currentViewingPlaylist.Tracks[0]);
            }
            else
            {
                ShowToast("Playlist is empty!");
            }
        }

        private void MenuAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            _trackPendingForPlaylist = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (_trackPendingForPlaylist != null)
            {
                AddToPlaylistDialog.Visibility = Visibility.Visible;
            }
        }

        private void MenuAddToPlaylistNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack != null)
            {
                _trackPendingForPlaylist = currentTrack;
                NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
                AddToPlaylistDialog.Visibility = Visibility.Visible;
            }
        }

        private void CancelAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            AddToPlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private void DialogPlaylistList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var playlist = e.ClickedItem as UserPlaylist;
            if (playlist != null && _trackPendingForPlaylist != null)
            {
                if (!playlist.Tracks.Any(t => t.VideoId == _trackPendingForPlaylist.VideoId))
                {
                    playlist.Tracks.Insert(0, _trackPendingForPlaylist);
                    SavePlaylistsAsync();
                    ShowToast("Added to " + playlist.Name);
                }
                else
                {
                    ShowToast("Song already in playlist.");
                }
            }
            AddToPlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private YouTubeTrack _trackToShare;
        private void MenuShare_Click(object sender, RoutedEventArgs e)
        {
            _trackToShare = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (_trackToShare != null)
            {
                DataTransferManager.ShowShareUI();
            }
        }

        private void MenuShareNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack != null)
            {
                _trackToShare = currentTrack;
                NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
                DataTransferManager.ShowShareUI();
            }
        }

        private void MainPage_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (_trackToShare != null)
            {
                args.Request.Data.Properties.Title = "Youtify - Share Music";
                args.Request.Data.Properties.Description = "Listen to " + _trackToShare.Title;
                string url = "https://www.youtube.com/watch?v=" + _trackToShare.VideoId;
                args.Request.Data.SetWebLink(new Uri(url));
            }
        }

        private void SleepTimer_Tick(object sender, object e)
        {
            _sleepMinutesLeft--;
            if (_sleepMinutesLeft <= 0)
            {
                _sleepTimer.Stop();
                _sleepTimerMode = 0;
                MenuSleepTimerStatus.Text = "Off";
                try { _appMediaPlayer.Pause(); } catch { }
                ShowToast("Sleep Timer: Music paused.");
            }
            else
            {
                MenuSleepTimerStatus.Text = _sleepMinutesLeft + " min left";
            }
        }

        private void SleepTimer_Click(object sender, RoutedEventArgs e)
        {
            _sleepTimerMode++;
            if (_sleepTimerMode > 3) _sleepTimerMode = 0;

            if (_sleepTimerMode == 0)
            {
                _sleepTimer.Stop();
                MenuSleepTimerStatus.Text = "Off";
                ShowToast("Sleep Timer: Off");
            }
            else
            {
                if (_sleepTimerMode == 1) _sleepMinutesLeft = 15;
                else if (_sleepTimerMode == 2) _sleepMinutesLeft = 30;
                else if (_sleepTimerMode == 3) _sleepMinutesLeft = 60;

                MenuSleepTimerStatus.Text = _sleepMinutesLeft + " min left";
                _sleepTimer.Start();
                ShowToast("Sleep Timer set for " + _sleepMinutesLeft + " minutes");
            }
        }

        private void MenuSleepTimerNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            SleepTimer_Click(null, null);
        }

        private void IncreaseLyricsSize_Click(object sender, RoutedEventArgs e)
        {
            if (_baseLyricSize < 30)
            {
                _baseLyricSize += 2;
                _highlightLyricSize += 2;
                RefreshLyricsSize();
                ShowToast("Lyrics size increased");
            }
        }

        private void DecreaseLyricsSize_Click(object sender, RoutedEventArgs e)
        {
            if (_baseLyricSize > 12)
            {
                _baseLyricSize -= 2;
                _highlightLyricSize -= 2;
                RefreshLyricsSize();
                ShowToast("Lyrics size decreased");
            }
        }

        private void RefreshLyricsSize()
        {
            for (int i = 0; i < currentLyrics.Count; i++)
            {
                currentLyrics[i].FontSize = (i == currentLyricIndex) ? _highlightLyricSize : _baseLyricSize;
            }
        }

        private void ClearRecentHistory_Click(object sender, RoutedEventArgs e)
        {
            historyTracks.Clear();
            historyQuickGridTracks.Clear();
            homeHistoryCarouselTracks.Clear();
            HomeHistorySection.Visibility = Visibility.Collapsed;
            var ignored = SaveHistoryAsyncTask();
            ShowToast("Recent history cleared!");
        }

        private async Task SaveHistoryAsyncTask()
        {
            try
            {
                JArray array = new JArray();
                foreach (var t in historyTracks)
                {
                    JObject obj = new JObject();
                    obj["VideoId"] = t.VideoId; obj["Title"] = t.Title;
                    obj["ChannelName"] = t.ChannelName; obj["ThumbnailUrl"] = t.ThumbnailUrl;
                    array.Add(obj);
                }
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("history.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, array.ToString());
            }
            catch { }
        }


        private async void SaveFavoritesAsync()
        {
            try
            {
                JArray array = new JArray();
                foreach (var t in favoriteTracks)
                {
                    JObject obj = new JObject();
                    obj["VideoId"] = t.VideoId; obj["Title"] = t.Title;
                    obj["ChannelName"] = t.ChannelName; obj["ThumbnailUrl"] = t.ThumbnailUrl;
                    array.Add(obj);
                }
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("favorites.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, array.ToString());
            }
            catch { }
        }

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
                             "&scope=https://www.googleapis.com/auth/youtube.readonly" +
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
                             "&scope=https://www.googleapis.com/auth/youtube.readonly" +
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

                    ShowToast("Login successful! Fetching music...");
                    await SyncLikedVideosAsync(accessToken);
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

        private async Task SyncLikedVideosAsync(string accessToken)
        {
            try
            {
                LoginStatusText.Text = "Status: Syncing Liked Songs...";
                string url = "https://www.googleapis.com/youtube/v3/videos?myRating=like&part=snippet&maxResults=20";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "Bearer " + accessToken);

                var response = await _apiClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string resultJson = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(resultJson);

                    bool hasNew = false;
                    // [OPT-C3] Guard null — items có thể null nếu quota exceeded
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
                    LoginStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Green);
                    ShowToast("Successfully loaded Liked Songs from YouTube!");
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

        private async Task RefreshGoogleTokenAndSyncAsync()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (!settings.ContainsKey("GoogleRefreshToken") || !settings.ContainsKey("GoogleClientId") || !settings.ContainsKey("GoogleClientSecret")) return;

            string refreshToken = settings["GoogleRefreshToken"].ToString();
            string clientId = settings["GoogleClientId"].ToString();
            string clientSecret = settings["GoogleClientSecret"].ToString();

            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                    new KeyValuePair<string, string>("refresh_token", refreshToken),
                    new KeyValuePair<string, string>("grant_type", "refresh_token")
                });

                var response = await _apiClient.PostAsync("https://oauth2.googleapis.com/token", content);
                if (response.IsSuccessStatusCode)
                {
                    string resultJson = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(resultJson);
                    string newAccessToken = json["access_token"]?.ToString();

                    settings["GoogleAccessToken"] = newAccessToken;

                    await SyncLikedVideosAsync(newAccessToken);
                }
            }
            catch { }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsInternetAvailable()) { ShowToast("No Internet"); return; }
            _typingTimer.Stop(); SuggestionPopup.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchLoading.Visibility = Visibility.Visible;
                DefaultSearchUI.Visibility = Visibility.Collapsed;
                SearchSongList.Visibility = Visibility.Collapsed;
                _nextSearchToken = "";
                _currentSearchQuery = SearchBox.Text.Trim();
                _isLoadingMoreSearch = false;
                ExecuteSearch(_currentSearchQuery);
            }
        }

        private async void ExecuteSearch(string query)
        {
            SearchLoading.Visibility = Visibility.Visible;
            DefaultSearchUI.Visibility = Visibility.Collapsed;

            SearchSongList.Visibility = Visibility.Visible;
            SearchSongList.ItemsSource = searchResults;
            SearchSongList.UpdateLayout();

            var sv = GetScrollViewer(SearchSongList);
            if (sv != null)
            {
                sv.ViewChanged -= SearchScrollViewer_ViewChanged;
                sv.ViewChanged += SearchScrollViewer_ViewChanged;
            }

            searchResults.Clear();
            var tracks = await FetchMusicList(query);
            if (tracks != null && tracks.Count > 0)
            {
                var filteredTracks = tracks.AsEnumerable();
                var songBg = ((Windows.UI.Xaml.Media.SolidColorBrush)FilterSongsBtn.Background).Color;
                var playlistBg = ((Windows.UI.Xaml.Media.SolidColorBrush)FilterPlaylistsBtn.Background).Color;
                var artistBg = ((Windows.UI.Xaml.Media.SolidColorBrush)FilterArtistsBtn.Background).Color;
                var activeColor = Windows.UI.Color.FromArgb(255, 29, 185, 84); // #1DB954
                
                if (songBg == activeColor) filteredTracks = filteredTracks.Where(t => !t.VideoId.StartsWith("PLAYLIST:") && !t.VideoId.StartsWith("CHANNEL:"));
                else if (playlistBg == activeColor) filteredTracks = filteredTracks.Where(t => t.VideoId.StartsWith("PLAYLIST:"));
                else if (artistBg == activeColor) filteredTracks = filteredTracks.Where(t => t.VideoId.StartsWith("CHANNEL:"));

                foreach (var t in filteredTracks) searchResults.Add(t);
            }
            else
            {
                ShowToast("No results found.");
            }
            SearchLoading.Visibility = Visibility.Collapsed;
        }

        private async void SearchScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv != null && sv.VerticalOffset >= sv.ScrollableHeight - 200 && !_isLoadingMoreSearch && !string.IsNullOrEmpty(_nextSearchToken))
            {
                _isLoadingMoreSearch = true;
                SearchLoading.Visibility = Visibility.Visible;

                var tracks = await FetchMusicList(_currentSearchQuery, _nextSearchToken);
                if (tracks != null) foreach (var t in tracks) searchResults.Add(t);

                SearchLoading.Visibility = Visibility.Collapsed;
                _isLoadingMoreSearch = false;
            }
        }

        private async Task LoadHomeRecommendations()
        {
            HomeLoading.Visibility = Visibility.Visible;

            string year = DateTime.Now.Year.ToString();
            string region = "US";
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
            {
                region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();
            }

            string trendingQuery = "top hits " + year + " \"Topic\"";
            string popQuery = "pop music hits \"Topic\"";
            string lofiQuery = "lofi chill beats \"Topic\"";
            string workoutQuery = "workout gym music \"Topic\"";

            switch (region)
            {
                case "VN":
                    trendingQuery = "V-pop top hits " + year + " \"Topic\"";
                    popQuery = "V-pop remix \"Topic\"";
                    lofiQuery = "V-pop lofi chill \"Topic\"";
                    workoutQuery = "V-pop EDM \"Topic\"";
                    break;
                case "KR":
                    trendingQuery = "K-pop trending hits " + year + " \"Topic\"";
                    popQuery = "K-drama OST \"Topic\"";
                    lofiQuery = "K-pop aesthetic vibes \"Topic\"";
                    workoutQuery = "K-pop gym workout \"Topic\"";
                    break;
                case "JP":
                    trendingQuery = "J-pop trending hits " + year + " \"Topic\"";
                    popQuery = "Anime OST \"Topic\"";
                    lofiQuery = "J-pop chill vibes \"Topic\"";
                    workoutQuery = "J-rock hits \"Topic\"";
                    break;
                case "GB":
                    trendingQuery = "UK top hits " + year + " \"Topic\"";
                    popQuery = "UK drill rap \"Topic\"";
                    lofiQuery = "UK indie rock \"Topic\"";
                    workoutQuery = "UK dance hits \"Topic\"";
                    break;
            }

            // [OPT-Q4] Lưu vào biến riêng, KHÔNG ghi đè _currentSearchQuery của Search
            _currentHomeQuery = trendingQuery;
            var trending = await FetchMusicList(trendingQuery);
            if (trending != null) foreach (var t in trending) { if (t.VideoId != null && !t.VideoId.StartsWith("CHANNEL:")) homeTracks.Add(t); }

            var pop = await FetchMusicList(popQuery);
            if (pop != null) foreach (var t in pop) { if (t.VideoId != null && !t.VideoId.StartsWith("CHANNEL:")) popTracks.Add(t); }

            var lofi = await FetchMusicList(lofiQuery);
            if (lofi != null) foreach (var t in lofi) { if (t.VideoId != null && !t.VideoId.StartsWith("CHANNEL:")) lofiTracks.Add(t); }

            var workout = await FetchMusicList(workoutQuery);
            if (workout != null) foreach (var t in workout) { if (t.VideoId != null && !t.VideoId.StartsWith("CHANNEL:")) workoutTracks.Add(t); }

            HomeLoading.Visibility = Visibility.Collapsed;
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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _typingTimer.Stop();

            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SuggestionPopup.Visibility = Visibility.Collapsed;
                DefaultSearchUI.Visibility = Visibility.Visible;
                SearchSongList.Visibility = Visibility.Collapsed;
            }
            else
            {
                _typingTimer.Start();
                DefaultSearchUI.Visibility = Visibility.Collapsed;
            }
        }

        private void Category_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag != null)
            {
                string categoryName = btn.Tag.ToString();

                SearchBox.TextChanged -= SearchBox_TextChanged;
                SearchBox.Text = categoryName;
                SearchBox.TextChanged += SearchBox_TextChanged;

                _typingTimer.Stop();
                SuggestionPopup.Visibility = Visibility.Collapsed;

                SearchButton_Click(null, null);
            }
        }

        private async void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();
            string query = SearchBox.Text.Trim();
            if (!string.IsNullOrEmpty(query) && IsInternetAvailable()) await LoadSuggestions(query);
        }

        private async Task LoadSuggestions(string query)
        {
            try
            {
                string url = "http://suggestqueries.google.com/complete/search?client=firefox&ds=yt&q=" + Uri.EscapeDataString(query);
                var response = await _apiClient.GetStringAsync(url);
                var jsonArray = JArray.Parse(response);
                if (jsonArray.Count > 1)
                {
                    var suggestions = jsonArray[1] as JArray;
                    searchSuggestions.Clear();
                    if (suggestions != null && suggestions.Count > 0)
                    {
                        foreach (var item in suggestions.Take(5)) searchSuggestions.Add(item.ToString());
                        SuggestionPopup.Visibility = Visibility.Visible;
                    }
                    else SuggestionPopup.Visibility = Visibility.Collapsed;
                }
            }
            catch { SuggestionPopup.Visibility = Visibility.Collapsed; }
        }

        private void SuggestionList_ItemClick(object sender, ItemClickEventArgs e)
        {
            _typingTimer.Stop();
            SearchBox.TextChanged -= SearchBox_TextChanged;
            SearchBox.Text = e.ClickedItem.ToString();
            SearchBox.TextChanged += SearchBox_TextChanged;
            SuggestionPopup.Visibility = Visibility.Collapsed;
            SearchButton_Click(null, null);
        }

        private static readonly SolidColorBrush _filterActiveBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 185, 84)); // #1DB954
        private static readonly SolidColorBrush _filterInactiveBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)); // #232323

        private void ResetFilters()
        {
            FilterAllBtn.Background = _filterInactiveBg;
            FilterSongsBtn.Background = _filterInactiveBg;
            FilterPlaylistsBtn.Background = _filterInactiveBg;
            FilterArtistsBtn.Background = _filterInactiveBg;
        }

        private void FilterAllBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterAllBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

        private void FilterSongsBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterSongsBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

        private void FilterPlaylistsBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterPlaylistsBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

        private void FilterArtistsBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterArtistsBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

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

        private void SyncMinus_Click(object sender, RoutedEventArgs e)
        {
            foreach (var line in currentLyrics)
            {
                if (line.Text != "") line.Time = line.Time.Add(TimeSpan.FromSeconds(0.5));
            }
            currentLyricIndex = -1;
            ShowToast("Lyrics delayed by 0.5s");
        }

        private void SyncPlus_Click(object sender, RoutedEventArgs e)
        {
            foreach (var line in currentLyrics)
            {
                if (line.Text != "" && line.Time.TotalSeconds >= 0.5)
                    line.Time = line.Time.Subtract(TimeSpan.FromSeconds(0.5));
            }
            currentLyricIndex = -1;
            ShowToast("Lyrics advanced by 0.5s");
        }

        private async void PlayTrack(YouTubeTrack track)
        {
            if (track == null || string.IsNullOrEmpty(track.VideoId)) return;
            if (track.VideoId.StartsWith("CHANNEL:"))
            {
                OpenArtistProfile(track.VideoId.Substring(8), track.Title ?? track.ChannelName);
                return;
            }
            if (track.VideoId.StartsWith("PLAYLIST:"))
            {
                OpenYouTubePlaylist(track.VideoId.Substring(9), track.Title, track.ThumbnailUrl);
                return;
            }

            try { _appMediaPlayer.Pause(); } catch { }

            if (!track.VideoId.StartsWith("LOCAL:") && !IsInternetAvailable()) { ShowToast("No Internet connection"); return; }

            currentTrack = track;
            MiniTitle.Text = track.Title; BigTitle.Text = track.Title;
            MiniArtist.Text = track.ChannelName; BigArtist.Text = track.ChannelName;
            SetPlayPauseIcon(true);
            MenuTitle.Text = track.Title;
            MenuArtist.Text = track.ChannelName;

            if (!string.IsNullOrEmpty(track.ThumbnailUrl))
            {
                // [OPT-M3] Dùng chung 1 BitmapImage cho BigCover + MenuCover (cùng src, cùng DecodePixelWidth)
                var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(track.ThumbnailUrl), UriKind.Absolute));
                bigBmp.DecodePixelWidth = 360;
                BigCoverImage.ImageSource  = bigBmp;
                AlbumArtEntranceStoryboard.Begin();
                MenuCoverImage.ImageSource = bigBmp;

                var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(track.ThumbnailUrl, UriKind.Absolute));
                miniBmp.DecodePixelWidth = 100;
                MiniCoverImage.ImageSource = miniBmp;
            }

            bool isFav = favoriteTracks.Any(t => t.VideoId == track.VideoId);
            BigHeartBtn.Content = isFav ? "♥" : "♡";
            BigHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;

            var ignored = UpdateLyricsAsync(track.Title, track.ChannelName);

            // BUG FIX: Xác định activeList TRƯỚC khi insert vào history.
            // Nếu detect sau khi insert, historyTracks.Contains(track) sẽ luôn true,
            // gây mất nguồn context thực sự (search, playlist, favorites...).
            ObservableCollection<YouTubeTrack> activeList = homeTracks;
            if (searchResults.Contains(track)) activeList = searchResults;
            else if (favoriteTracks.Contains(track)) activeList = favoriteTracks;
            else if (downloadedTracks.Contains(track)) activeList = downloadedTracks;
            else if (homeHistoryCarouselTracks.Contains(track)) activeList = homeHistoryCarouselTracks;
            else if (historyQuickGridTracks.Contains(track)) activeList = historyQuickGridTracks;
            else if (popTracks.Contains(track)) activeList = popTracks;
            else if (lofiTracks.Contains(track)) activeList = lofiTracks;
            else if (workoutTracks.Contains(track)) activeList = workoutTracks;
            else if (historyTracks.Contains(track)) activeList = historyTracks;
            else if (_currentViewingPlaylist != null && _currentViewingPlaylist.Tracks.Contains(track)) activeList = _currentViewingPlaylist.Tracks;
            else if (ArtistSongsList.ItemsSource != null) { var artistList = ArtistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>; if (artistList != null && artistList.Contains(track)) activeList = artistList; }

            // Cập nhật lịch sử SAU khi đã chọn activeList
            var existingHistory = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
            if (existingHistory != null) historyTracks.Remove(existingHistory);
            historyTracks.Insert(0, track);
            if (historyTracks.Count > 20) historyTracks.RemoveAt(historyTracks.Count - 1);
            var ignoredHistory = SaveHistoryAsyncTask();
            RefreshHomeHistorySections();

            // Resolve URL cho bài hiện tại từ foreground (HttpClient mạnh hơn AudioTask)
            string resolvedUrl = "";
            if (!track.VideoId.StartsWith("LOCAL:"))
            {
                try
                {
                    resolvedUrl = await InnerTubeClient.ResolveStreamUrlAsync(track.VideoId) ?? "";
                }
                catch { resolvedUrl = ""; }
            }

            // OPTIMIZATION: Gộp 2 vòng lặp activeList thành 1 duy nhất
            int count = activeList.Count;
            string[] urls = new string[count];
            string[] titles = new string[count];
            string[] artists = new string[count];
            string[] videoIds = new string[count];
            string[] thumbnails = new string[count];

            int startIndex = Math.Max(0, activeList.IndexOf(track));

            currentQueueTracks.Clear();
            for (int i = 0; i < count; i++)
            {
                var t = activeList[i];
                currentQueueTracks.Add(t);
                if (i == startIndex && !string.IsNullOrEmpty(resolvedUrl))
                    urls[i] = resolvedUrl; // Dùng URL đã resolve
                else
                    urls[i] = t.VideoId.StartsWith("LOCAL:")
                        ? "ms-appdata:///local/" + t.VideoId.Substring(6)
                        : ""; // AudioTask sẽ tự resolve cho các bài khác khi đến lượt
                titles[i] = t.Title;
                artists[i] = t.ChannelName;
                videoIds[i] = t.VideoId;
                thumbnails[i] = t.ThumbnailUrl;
            }

            var message = new ValueSet {
                { "UpdatePlaylist", "" }, { "Urls", urls }, { "Titles", titles }, { "Artists", artists },
                { "VideoIds", videoIds }, { "Thumbnails", thumbnails }, { "StartIndex", startIndex }, { "FastUrl", urls[startIndex] }
            };
            try { BackgroundMediaPlayer.SendMessageToBackground(message); } catch { }
        }

        private void QueueListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var track = e.ClickedItem as YouTubeTrack;
            if (track != null)
            {
                PlayTrack(track);
            }
        }

        private void MiniPlayer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            NowPlayingView.Visibility = Visibility.Visible;
            if (this.Resources.ContainsKey("SlideUpStoryboard"))
            {
                var storyboard = (Windows.UI.Xaml.Media.Animation.Storyboard)this.Resources["SlideUpStoryboard"];
                storyboard.Begin();
            }
        }

        private void CloseNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (this.Resources.ContainsKey("SlideDownStoryboard"))
            {
                var storyboard = (Windows.UI.Xaml.Media.Animation.Storyboard)this.Resources["SlideDownStoryboard"];
                storyboard.Begin();
            }
            else
            {
                NowPlayingView.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenNowPlayingMenu_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack != null)
            {
                NowPlayingDownloadBtn.Visibility = currentTrack.VideoId.StartsWith("LOCAL:") ? Visibility.Collapsed : Visibility.Visible;
            }

            NowPlayingMenuDialog.Visibility = Visibility.Visible;
            if (this.Resources.ContainsKey("MenuSlideUpStoryboard"))
            {
                var storyboard = (Windows.UI.Xaml.Media.Animation.Storyboard)this.Resources["MenuSlideUpStoryboard"];
                storyboard.Begin();
            }
        }

        private void CloseNowPlayingMenu_Click(object sender, RoutedEventArgs e)
        {
            if (this.Resources.ContainsKey("MenuSlideDownStoryboard"))
            {
                var storyboard = (Windows.UI.Xaml.Media.Animation.Storyboard)this.Resources["MenuSlideDownStoryboard"];
                storyboard.Begin();
            }
            else
            {
                NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
            }
        }

        private void MenuSlideDownStoryboard_Completed(object sender, object e)
        {
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
        }

        private async void MenuDownloadNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            CloseNowPlayingMenu_Click(null, null);
            if (currentTrack != null) await DownloadTrackAsync(currentTrack);
        }

        private void NowPlayingPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = NowPlayingPivot.SelectedIndex;
            DotPlayer.Opacity = idx == 0 ? 1.0 : 0.3;
            DotLyrics.Opacity = idx == 1 ? 1.0 : 0.3;
            DotQueue.Opacity  = idx == 2 ? 1.0 : 0.3;
        }

        private void SlideDownStoryboard_Completed(object sender, object e)
        {
            NowPlayingView.Visibility = Visibility.Collapsed;
        }

        private void SongItem_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                var el = sender as FrameworkElement;
                if (el != null)
                {
                    var flyout = FlyoutBase.GetAttachedFlyout(el);
                    if (flyout != null)
                    {
                        flyout.ShowAt(el);
                    }
                }
            }
        }

        private YouTubeTrack _bottomSheetTrack;

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var track = btn.DataContext as YouTubeTrack;
            if (track == null) return;

            _bottomSheetTrack = track;
            BottomSheetTitle.Text = track.Title;
            BottomSheetArtist.Text = track.ChannelName;
            try {
                BottomSheetCover.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(track.ThumbnailUrl)) { DecodePixelWidth = 100 };
            } catch {}

            CustomBottomSheet.Visibility = Visibility.Visible;
            BottomSheetSlideUpStoryboard.Begin();
        }

        private void CloseBottomSheet_Click(object sender, RoutedEventArgs e)
        {
            BottomSheetSlideDownStoryboard.Begin();
        }

        private void BottomSheetSlideDownStoryboard_Completed(object sender, object e)
        {
            CustomBottomSheet.Visibility = Visibility.Collapsed;
        }

        private void BottomSheetContent_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void BottomSheetPlay_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack != null) PlayTrack(_bottomSheetTrack);
        }

        private void BottomSheetAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack != null)
            {
                _trackPendingForPlaylist = _bottomSheetTrack;
                AddToPlaylistDialog.Visibility = Visibility.Visible;
            }
        }

        private void BottomSheetGoToArtist_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack != null) OpenArtistProfile(_bottomSheetTrack.ChannelId, _bottomSheetTrack.ChannelName);
        }

        private void BottomSheetSleepTimer_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            SleepTimer_Click(null, null);
        }

        private void BottomSheetShare_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack != null)
            {
                _trackToShare = _bottomSheetTrack;
                DataTransferManager.ShowShareUI();
            }
        }

        private async void OpenYouTubePlaylist(string playlistId, string playlistName, string coverUrl = null)
        {
            try
            {
                // Close artist profile if it's open, so playlist view is visible above it
                ArtistProfileView.Visibility = Visibility.Collapsed;

                PlaylistDetailsTitle.Text = playlistName;
                PlaylistDetailsCoverBrush.ImageSource = null;
                PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(coverUrl), UriKind.Absolute)) { DecodePixelWidth = 150 };
                    PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                }
                PlaylistSongsList.ItemsSource = null;
                PlaylistDetailsView.Visibility = Visibility.Visible;
                PlaylistSlideInStoryboard.Begin();
                
                var tracks = new ObservableCollection<YouTubeTrack>();
                bool useFallback = false;
                string proxyThumbnail = null;

                try
                {
                    var plResult = await InnerTubeClient.BrowsePlaylistAsync(playlistId);
                    proxyThumbnail = plResult.ThumbnailUrl;
                    if (!string.IsNullOrEmpty(plResult.Title))
                    {
                        PlaylistDetailsTitle.Text = plResult.Title;
                    }
                    foreach (var t in plResult.Tracks)
                    {
                        tracks.Add(t);
                    }
                }
                catch { useFallback = true; }

                if (useFallback || tracks.Count == 0)
                {
                    string apiKey = ApiKeyTextBox.Text.Trim();
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        ShowToast("API Key required or proxy failed.");
                        return;
                    }

                    string url = $"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&maxResults=50&playlistId={playlistId}&key={apiKey}";
                    var response = await _apiClient.GetStringAsync(url);
                    var json = Newtonsoft.Json.Linq.JObject.Parse(response);
                    
                    var items = json["items"];
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            try
                            {
                                var snippet = item["snippet"];
                                string title = snippet["title"]?.ToString();
                                if (title == "Private video" || title == "Deleted video") continue;
                                
                                string vidId = snippet["resourceId"]?["videoId"]?.ToString();
                                string channel = snippet["videoOwnerChannelTitle"]?.ToString() ?? snippet["channelTitle"]?.ToString();
                                string channelId = snippet["videoOwnerChannelId"]?.ToString() ?? snippet["channelId"]?.ToString();
                                var thumbs = snippet["thumbnails"];
                                string thumbUrl = thumbs?["maxres"]?["url"]?.ToString()
                                   ?? thumbs?["standard"]?["url"]?.ToString()
                                   ?? thumbs?["high"]?["url"]?.ToString()
                                   ?? thumbs?["medium"]?["url"]?.ToString()
                                   ?? thumbs?["default"]?["url"]?.ToString();
                                   
                                tracks.Add(new YouTubeTrack
                                {
                                    VideoId = vidId,
                                    Title = title,
                                    ChannelName = CleanChannelName(channel),
                                    ChannelId = channelId,
                                    ThumbnailUrl = thumbUrl
                                });
                            }
                            catch { continue; }
                        }
                    }
                }

                // If no cover was set, try proxy thumbnail or first track's thumbnail
                if (PlaylistDetailsCoverRect.Visibility == Visibility.Collapsed && tracks.Count > 0)
                {
                    string fallbackCover = proxyThumbnail;
                    if (string.IsNullOrEmpty(fallbackCover))
                    {
                        fallbackCover = tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.ThumbnailUrl))?.ThumbnailUrl;
                    }
                    if (!string.IsNullOrEmpty(fallbackCover))
                    {
                        try
                        {
                            PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(fallbackCover), UriKind.Absolute)) { DecodePixelWidth = 150 };
                            PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                        }
                        catch { }
                    }
                }
                
                _currentViewingPlaylist = new UserPlaylist { Name = playlistName, Tracks = tracks };
                PlaylistSongsList.ItemsSource = _currentViewingPlaylist.Tracks;
                PlaylistDetailsTrackCount.Text = tracks.Count + " tracks";
            }
            catch { ShowToast("Failed to load playlist"); }
        }

        private async void OpenArtistProfile(string channelId, string channelName)
        {
            ArtistProfileView.Visibility = Visibility.Visible;
            ArtistSlideInStoryboard.Begin();
            ArtistLoadingBar.Visibility = Visibility.Visible;
            ArtistSongsList.Visibility = Visibility.Collapsed;
            ArtistProfileTitle.Text = channelName ?? "Unknown Artist";
            ArtistProfileCover.Source = null;
            ArtistProfileAvatar.ImageSource = null;
            ArtistAlbumsSection.Visibility = Visibility.Collapsed;
            ArtistAlbumsList.ItemsSource = null;

            List<YouTubeTrack> tracks = null;
            List<ArtistAlbum> albums = null;
            bool hasCustomAvatar = false;

            // --- LẤY AVATAR THẬT TỪ YOUTUBE API (NẾU CÓ API KEY) ---
            string apiKey = ApiKeyTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(apiKey))
            {
                try
                {
                    string ytUrl = string.IsNullOrEmpty(channelId) 
                        ? "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=1&q=" + Uri.EscapeDataString(channelName) + "&type=channel&key=" + apiKey
                        : "https://www.googleapis.com/youtube/v3/channels?part=snippet&id=" + Uri.EscapeDataString(channelId) + "&key=" + apiKey;
                    
                    var response = await _apiClient.GetStringAsync(ytUrl);
                    var json = Newtonsoft.Json.Linq.JObject.Parse(response);
                    var items = json["items"];
                    if (items != null && items.Any())
                    {
                        var snippet = items[0]["snippet"];
                        if (string.IsNullOrEmpty(channelId)) 
                        {
                            channelId = snippet["channelId"]?.ToString() ?? items[0]["id"]?["channelId"]?.ToString();
                        }
                        var thumbUrl = snippet["thumbnails"]?["high"]?["url"]?.ToString() ?? snippet["thumbnails"]?["default"]?["url"]?.ToString();
                        if (thumbUrl != null)
                        {
                            ArtistProfileAvatar.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(thumbUrl))) { DecodePixelWidth = 200 };
                            hasCustomAvatar = true;
                        }
                        var titleStr = snippet["title"]?.ToString();
                        if (!string.IsNullOrEmpty(titleStr)) ArtistProfileTitle.Text = titleStr;
                    }
                }
                catch { }
            }

            // Lớp 1: InnerTube trực tiếp (không cần proxy)
            if (!string.IsNullOrEmpty(channelId))
            {
                try
                {
                    var artistResult = await InnerTubeClient.BrowseArtistAsync(channelId);
                    if (artistResult.Tracks != null && artistResult.Tracks.Count > 0)
                        tracks = artistResult.Tracks;

                    if (!string.IsNullOrEmpty(artistResult.AvatarUrl) && !hasCustomAvatar)
                    {
                        ArtistProfileAvatar.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(artistResult.AvatarUrl))) { DecodePixelWidth = 200 };
                        hasCustomAvatar = true;
                    }
                    if (!string.IsNullOrEmpty(artistResult.CoverUrl))
                    {
                        ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(artistResult.CoverUrl))) { DecodePixelWidth = 400 };
                    }
                    if (!string.IsNullOrEmpty(artistResult.Name) && artistResult.Name != "Artist")
                    {
                        ArtistProfileTitle.Text = artistResult.Name;
                    }
                    if (artistResult.Albums != null && artistResult.Albums.Count > 0)
                    {
                        albums = artistResult.Albums;
                    }
                }
                catch { }
            }

            // Lớp 2: Dự phòng (Fallback) gọi /api/search như cũ
            if (tracks == null || tracks.Count == 0)
            {
                string query = channelName ?? "";
                if (!string.IsNullOrEmpty(channelId)) query += " \"Topic\""; 
                tracks = await FetchMusicList(query);
            }
            
            var list = new ObservableCollection<YouTubeTrack>();
            if (tracks != null)
            {
                foreach(var t in tracks) 
                {
                    if (t.VideoId != null && t.VideoId.StartsWith("CHANNEL:")) continue;
                    list.Add(t);
                }
                
                if (list.Count > 0)
                {
                    try {
                        var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(list[0].ThumbnailUrl))) { DecodePixelWidth = 400 };
                        if (ArtistProfileCover.Source == null) ArtistProfileCover.Source = bmp;
                        if (!hasCustomAvatar) ArtistProfileAvatar.ImageSource = bmp;
                        
                        if (ArtistProfileTitle.Text == "Nghệ sĩ" || ArtistProfileTitle.Text == "Artist" || ArtistProfileTitle.Text == "Unknown Artist")
                        {
                            var trackWithArtist = list.FirstOrDefault(t => !string.IsNullOrEmpty(t.ChannelName) && t.ChannelName != "Nghệ sĩ" && t.ChannelName != "Artist");
                            if (trackWithArtist != null) ArtistProfileTitle.Text = trackWithArtist.ChannelName;
                            else if (!string.IsNullOrEmpty(list[0].ChannelName)) ArtistProfileTitle.Text = list[0].ChannelName;
                        }
                    } catch {}
                }
            }

            ArtistSongsList.ItemsSource = list;
            ArtistLoadingBar.Visibility = Visibility.Collapsed;
            ArtistSongsList.Visibility = Visibility.Visible;

            // Populate albums carousel
            if (albums != null && albums.Count > 0)
            {
                // Group by section title (Albums, Singles, etc.)
                var firstSection = albums[0].SectionTitle;
                ArtistAlbumsTitle.Text = firstSection;
                ArtistAlbumsList.ItemsSource = albums;
                ArtistAlbumsSection.Visibility = Visibility.Visible;
            }
        }

        private void MenuGoToArtistNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
            NowPlayingView.Visibility = Visibility.Collapsed;
            
            if (currentTrack != null)
            {
                OpenArtistProfile(currentTrack.ChannelId, currentTrack.ChannelName);
            }
        }

        private void CloseArtistProfile_Click(object sender, RoutedEventArgs e)
        {
            ArtistSlideOutStoryboard.Begin();
        }

        private void ArtistSlideOutStoryboard_Completed(object sender, object e)
        {
            ArtistProfileView.Visibility = Visibility.Collapsed;
            // [OPT-M9] Giải phóng ảnh khi đóng — tiết kiệm RAM
            ArtistProfileCover.Source = null;
            ArtistProfileAvatar.ImageSource = null;
            ArtistSongsList.ItemsSource = null;
            ArtistAlbumsList.ItemsSource = null;
            ArtistAlbumsSection.Visibility = Visibility.Collapsed;
        }

        private void ArtistPlayAll_Click(object sender, RoutedEventArgs e)
        {
            var list = ArtistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>;
            if (list != null && list.Count > 0) PlayTrack(list[0]);
        }

        private void ArtistShuffle_Click(object sender, RoutedEventArgs e)
        {
            var list = ArtistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>;
            if (list != null && list.Count > 0)
            {
                var rng = new Random();
                int idx = rng.Next(list.Count);
                PlayTrack(list[idx]);
            }
        }

        private void ArtistAlbum_ItemClick(object sender, ItemClickEventArgs e)
        {
            var album = e.ClickedItem as ArtistAlbum;
            if (album == null) return;

            // If browseId looks like a playlist, open it
            if (!string.IsNullOrEmpty(album.BrowseId))
            {
                string playlistId = album.BrowseId;
                if (playlistId.StartsWith("MPREb_"))
                {
                    // Album browseId — browse as playlist
                    OpenYouTubePlaylist(playlistId, album.Title, album.ThumbnailUrl);
                }
                else if (playlistId.StartsWith("VL") || playlistId.StartsWith("PL"))
                {
                    OpenYouTubePlaylist(playlistId.Replace("VL", ""), album.Title, album.ThumbnailUrl);
                }
                else
                {
                    // Try to browse as playlist anyway  
                    OpenYouTubePlaylist(playlistId, album.Title, album.ThumbnailUrl);
                }
            }
        }

        private void ToastFadeOutStoryboard_Completed(object sender, object e)
        {
            ToastNotification.Visibility = Visibility.Collapsed;
        }

        private void MenuPlay_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null) PlayTrack(track);
        }

        private async void MenuDownload_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null) await DownloadTrackAsync(track);
        }

        private void MenuFavorite_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null)
            {
                var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                if (existing != null) favoriteTracks.Remove(existing);
                else favoriteTracks.Insert(0, track);
                SaveFavoritesAsync();
                ShowToast(existing != null ? "Removed from Favorites" : "Added to Favorites");

                if (currentTrack != null && currentTrack.VideoId == track.VideoId)
                {
                    BigHeartBtn.Content = existing == null ? "♥" : "♡";
                    BigHeartBtn.Foreground = existing == null ? _greenBrush : _whiteBrush;
                }
            }
        }

        private async void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null && track.VideoId.StartsWith("LOCAL:"))
            {
                try
                {
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(track.VideoId.Substring(6));
                    await file.DeleteAsync();

                    downloadedTracks.Remove(track);

                    var fav = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                    if (fav != null) { favoriteTracks.Remove(fav); SaveFavoritesAsync(); }

                    var hist = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                    if (hist != null)
                    {
                        historyTracks.Remove(hist);
                        var ignoredHist = SaveHistoryAsyncTask();
                        RefreshHomeHistorySections();
                    }

                    foreach (var playlist in userPlaylists)
                    {
                        var pt = playlist.Tracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                        if (pt != null) playlist.Tracks.Remove(pt);
                    }
                    SavePlaylistsAsync();

                    ShowToast("File deleted from device");
                }
                catch { }
            }
            else if (track != null)
            {
                ShowToast("Can only delete downloaded (LOCAL) files!");
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e) { if (currentTrack != null) await DownloadTrackAsync(currentTrack); }

        private async Task DownloadTrackAsync(YouTubeTrack track)
        {
            if (track == null || string.IsNullOrEmpty(track.VideoId) || track.VideoId.StartsWith("LOCAL:")) return;
            if (!IsInternetAvailable()) { ShowToast("Internet required to download"); return; }

            try
            {
                string downloadUrl = ProxyBaseUrl + "/api/download?v=" + track.VideoId + "&key=" + _apiSecretKey;
                string safeTitle = string.Join("", track.Title.Split(System.IO.Path.GetInvalidFileNameChars()));
                StorageFile destinationFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(safeTitle + ".m4a", CreationCollisionOption.ReplaceExisting);

                BackgroundDownloader downloader = new BackgroundDownloader();
                DownloadOperation download = downloader.CreateDownload(new Uri(downloadUrl), destinationFile);

                DownloadStatusBar.Visibility = Visibility.Visible;

                DownloadStatusText.Text = "Connecting: " + track.Title;
                DownloadProgressBar.Value = 0;
                DownloadProgressBar.IsIndeterminate = true;

                var progressCallback = new Progress<DownloadOperation>(op =>
                {
                    if (op.Progress.TotalBytesToReceive > 0)
                    {
                        DownloadProgressBar.IsIndeterminate = false;
                        double progress = (double)op.Progress.BytesReceived / op.Progress.TotalBytesToReceive * 100;
                        DownloadProgressBar.Value = progress;
                        DownloadStatusText.Text = "Downloading: " + track.Title + " (" + (int)progress + "%)";
                    }
                });

                await download.StartAsync().AsTask(progressCallback);

                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = 100;

                DownloadStatusText.Text = "Download complete: " + track.Title;

                await LoadDownloadsAsync();

                await Task.Delay(3000);
                DownloadStatusBar.Visibility = Visibility.Collapsed;
            }
            catch
            {
                DownloadStatusBar.Visibility = Visibility.Collapsed;
                ShowToast("Download failed or cancelled.");
            }
        }

        private void HeartButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack == null) return;
            var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == currentTrack.VideoId);
            if (existing != null) { favoriteTracks.Remove(existing); BigHeartBtn.Content = "♡"; BigHeartBtn.Foreground = _whiteBrush; }
            else { favoriteTracks.Insert(0, currentTrack); BigHeartBtn.Content = "♥"; BigHeartBtn.Foreground = _greenBrush; }
            SaveFavoritesAsync();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;
            bool newState = !(settings.Values.ContainsKey("ShuffleMode") ? (bool)settings.Values["ShuffleMode"] : false);
            settings.Values["ShuffleMode"] = newState;
            ShuffleIcon.Foreground = newState ? _greenBrush : _whiteBrush;
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;
            int newMode = (settings.Values.ContainsKey("RepeatMode") ? (int)settings.Values["RepeatMode"] : 0) + 1;
            if (newMode > 2) newMode = 0;
            settings.Values["RepeatMode"] = newMode;
            UpdateRepeatUI(newMode);
        }

        private void UpdateRepeatUI(int mode)
        {
            if (mode == 0) { RepeatIcon.Glyph = "\uE1CD"; RepeatIcon.Foreground = _whiteBrush; }
            else if (mode == 1) { RepeatIcon.Glyph = "\uE1CD"; RepeatIcon.Foreground = _greenBrush; }
            else if (mode == 2) { RepeatIcon.Glyph = "\uE1CC"; RepeatIcon.Foreground = _greenBrush; }
        }

        private void SetupTimer()
        {
            try { _appMediaPlayer.CurrentStateChanged += BackgroundMediaPlayer_CurrentStateChanged; } catch { }
            _bgTimer = new Timer(TimerCallback, null, 0, 1000);
        }

        private async void BackgroundMediaPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        SetPlayPauseIcon(sender.CurrentState == MediaPlayerState.Playing);
                    }
                    catch { }
                });
            }
            catch { }
        }

        private async void TimerCallback(object state)
        {
            // [OPT-P1] Bail-out sớm TRƯỚC khi gọi Dispatcher — tiết kiệm thread switch trên WP8.1
            if (_isSliderManipulating) return;
            MediaPlayer session;
            try { session = _appMediaPlayer; } catch { return; }
            if (session == null || session.CurrentState != MediaPlayerState.Playing) return;

            TimeSpan pos, dur;
            try { pos = session.Position; dur = session.NaturalDuration; } catch { return; }
            if (dur.TotalSeconds <= 0) return;

            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    try
                    {
                        MusicSlider.Maximum    = dur.TotalSeconds;
                        MusicSlider.Value      = pos.TotalSeconds;
                        CurrentTimeText.Text   = pos.ToString(@"m\:ss");
                        TotalTimeText.Text     = dur.ToString(@"m\:ss");
                        MiniProgressBar.Maximum = dur.TotalSeconds;
                        MiniProgressBar.Value   = pos.TotalSeconds;

                        if (NowPlayingView.Visibility != Visibility.Visible) return;
                        if (NowPlayingPivot.SelectedIndex != 1) return;
                        if (currentLyrics.Count == 0 || LyricsListView.Visibility != Visibility.Visible) return;

                        int newIndex = -1;
                        for (int i = 0; i < currentLyrics.Count; i++)
                        {
                            if (pos >= currentLyrics[i].Time.Subtract(TimeSpan.FromSeconds(0.2))) newIndex = i;
                            else break;
                        }

                        if (newIndex == currentLyricIndex || newIndex < 0) return;

                        int oldIndex = currentLyricIndex;
                        currentLyricIndex = newIndex;

                        if (oldIndex >= 0 && oldIndex < currentLyrics.Count)
                        {
                            // [OPT-M6] Dùng cached static brush — không tạo object mới mỗi giây
                            currentLyrics[oldIndex].ColorBrush  = _lyricInactiveBrush;
                            currentLyrics[oldIndex].FontSize    = _baseLyricSize;
                            currentLyrics[oldIndex].Opacity     = 0.5;
                            currentLyrics[oldIndex].FontWeight  = Windows.UI.Text.FontWeights.Normal;
                        }

                        currentLyrics[currentLyricIndex].ColorBrush  = _lyricActiveBrush;
                        currentLyrics[currentLyricIndex].FontSize     = _highlightLyricSize;
                        currentLyrics[currentLyricIndex].Opacity      = 1.0;
                        currentLyrics[currentLyricIndex].FontWeight   = Windows.UI.Text.FontWeights.Bold;

                        LyricsListView.ScrollIntoView(currentLyrics[currentLyricIndex]);

                        if (_cachedLyricsScrollViewer == null)
                            _cachedLyricsScrollViewer = GetScrollViewer(LyricsListView);

                        var container = LyricsListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                        if (_cachedLyricsScrollViewer != null && container != null)
                        {
                            var transform    = container.TransformToVisual(_cachedLyricsScrollViewer);
                            var lyricPos     = transform.TransformPoint(new Point(0, 0));
                            double targetOff = _cachedLyricsScrollViewer.VerticalOffset + lyricPos.Y
                                            - (_cachedLyricsScrollViewer.ViewportHeight / 2.0)
                                            + (container.ActualHeight / 2.0);
                            _cachedLyricsScrollViewer.ChangeView(null, targetOff, null, false);
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void MusicSlider_PointerPressed(object sender, PointerRoutedEventArgs e) => _isSliderManipulating = true;
        private void MusicSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isSliderManipulating = false;
            try
            {
                if (_appMediaPlayer.CurrentState != MediaPlayerState.Closed)
                {
                    // FIX #7: Clamp giá trị seek để không bị âm với clip ngắn
                    _appMediaPlayer.Position = TimeSpan.FromSeconds(Math.Min(MusicSlider.Value, Math.Max(0, _appMediaPlayer.NaturalDuration.TotalSeconds - 2)));
                    if (_appMediaPlayer.CurrentState == MediaPlayerState.Paused) _appMediaPlayer.Play();
                }
            }
            catch { }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // FIX: Nhận diện OS đã tự đóng Background Task (State = Closed) để ra lệnh phát lại từ đầu
                if (_appMediaPlayer.CurrentState == MediaPlayerState.Closed)
                {
                    if (currentTrack != null) PlayTrack(currentTrack);
                }
                else if (_appMediaPlayer.CurrentState == MediaPlayerState.Playing)
                {
                    _appMediaPlayer.Pause();
                }
                else
                {
                    _appMediaPlayer.Play();
                }
            }
            catch { }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_appMediaPlayer.Position.TotalSeconds > 3)
                {
                    _appMediaPlayer.Position = TimeSpan.Zero;
                    _appMediaPlayer.Play();
                }
                else
                {
                    BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { "PrevTrackMessage", "" } });
                }
            }
            catch { try { BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { "PrevTrackMessage", "" } }); } catch { } }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // FIX: Đánh thức background task nếu nó đã chết thay vì gửi message mù
                if (_appMediaPlayer.CurrentState == MediaPlayerState.Closed && currentTrack != null)
                {
                    PlayTrack(currentTrack);
                    return;
                }
                BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { "NextTrackMessage", "" } });
            }
            catch { }
        }
    }

    public class YouTubeTrack
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string ChannelName { get; set; }
        public string ChannelId { get; set; }
        public string ThumbnailUrl { get; set; }

        public Visibility DeleteVisibility
        {
            get { return (VideoId != null && VideoId.StartsWith("LOCAL:")) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility DownloadVisibility
        {
            get { return (VideoId != null && VideoId.StartsWith("LOCAL:")) ? Visibility.Collapsed : Visibility.Visible; }
        }
    }

    public class LyricLine : INotifyPropertyChanged
    {
        public TimeSpan Time { get; set; }

        private string _text;
        public string Text { get { return _text; } set { _text = value; OnPropertyChanged("Text"); } }

        private SolidColorBrush _colorBrush = new SolidColorBrush(Windows.UI.Colors.Gray);
        public SolidColorBrush ColorBrush { get { return _colorBrush; } set { _colorBrush = value; OnPropertyChanged("ColorBrush"); } }

        private double _fontSize = 18;
        public double FontSize { get { return _fontSize; } set { _fontSize = value; OnPropertyChanged("FontSize"); } }

        private double _opacity = 0.5;
        public double Opacity { get { return _opacity; } set { _opacity = value; OnPropertyChanged("Opacity"); } }

        private Windows.UI.Text.FontWeight _fontWeight = Windows.UI.Text.FontWeights.Normal;
        public Windows.UI.Text.FontWeight FontWeight { get { return _fontWeight; } set { _fontWeight = value; OnPropertyChanged("FontWeight"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    public class UserPlaylist
    {
        public string Name { get; set; }
        public ObservableCollection<YouTubeTrack> Tracks { get; set; }
        public UserPlaylist() { Tracks = new ObservableCollection<YouTubeTrack>(); }
    }
}