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

        private static readonly SolidColorBrush _filterActiveBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 185, 84)); // #1DB954
        private static readonly SolidColorBrush _filterInactiveBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)); // #232323

        // [REMOVED] SearchYouTubeMusic() — dead code, tất cả search paths đều dùng FetchMusicList()

        private void SongList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var track = e.ClickedItem as YouTubeTrack;
            if (track != null) PlayTrack(track);
        }

        private YouTubeTrack _bottomSheetTrack;

        private void ToastFadeOutStoryboard_Completed(object sender, object e)
        {
            ToastNotification.Visibility = Visibility.Collapsed;
        }

    }
}
