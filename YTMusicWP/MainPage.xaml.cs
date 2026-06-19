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
using Anim = Windows.UI.Xaml.Media.Animation;

namespace YTMusicWP
{
    public sealed partial class MainPage : Page
    {
        private Anim.Storyboard _marqueeStoryboard;

        private void StartTitleMarquee()
        {
            StopTitleMarquee();
            BigTitle.UpdateLayout();
            double textWidth = BigTitle.ActualWidth;
            double canvasWidth = TitleMarqueeCanvas.ActualWidth;
            if (canvasWidth <= 0) canvasWidth = TitleMarqueeCanvas.Width;
            if (textWidth <= canvasWidth || textWidth <= 0) return;

            double overflow = textWidth - canvasWidth;
            double speed = 30;
            double scrollDuration = overflow / speed;
            if (scrollDuration < 1) scrollDuration = 1;

            _marqueeStoryboard = new Anim.Storyboard();
            var anim = new Anim.DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new Anim.LinearDoubleKeyFrame { Value = 0, KeyTime = Anim.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)) });
            anim.KeyFrames.Add(new Anim.LinearDoubleKeyFrame { Value = -overflow, KeyTime = Anim.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + scrollDuration)) });
            anim.KeyFrames.Add(new Anim.LinearDoubleKeyFrame { Value = -overflow, KeyTime = Anim.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + scrollDuration)) });
            anim.KeyFrames.Add(new Anim.LinearDoubleKeyFrame { Value = 0, KeyTime = Anim.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + scrollDuration * 2)) });
            anim.RepeatBehavior = new Anim.RepeatBehavior(1000); // repeat many times
            Anim.Storyboard.SetTarget(anim, TitleTranslate);
            Anim.Storyboard.SetTargetProperty(anim, "X");
            _marqueeStoryboard.Children.Add(anim);
            _marqueeStoryboard.Begin();
        }

        private void StopTitleMarquee()
        {
            if (_marqueeStoryboard != null)
            {
                _marqueeStoryboard.Stop();
                _marqueeStoryboard = null;
            }
            TitleTranslate.X = 0;
        }

        private void TitleMarqueeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            TitleMarqueeCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        }
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
        private ObservableCollection<YouTubeTrack> genre5Tracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> genre6Tracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> genre7Tracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> genre8Tracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> podcastTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> audiobookTracks = new ObservableCollection<YouTubeTrack>();

        private ObservableCollection<UserPlaylist> userPlaylists = new ObservableCollection<UserPlaylist>();
        private UserPlaylist _currentViewingPlaylist = null;
        private string _currentViewingYtPlaylistId = null;
        private YouTubeTrack _trackPendingForPlaylist = null;



        private ObservableCollection<YouTubeTrack> currentQueueTracks = new ObservableCollection<YouTubeTrack>();

        private ObservableCollection<LyricLine> currentLyrics = new ObservableCollection<LyricLine>();
        private int currentLyricIndex = -1;

        private int _lyricFontSize = 22;

        private DispatcherTimer _typingTimer = new DispatcherTimer();
        private YouTubeTrack currentTrack = null;
        private bool _isSliderManipulating = false;
        private Timer _bgTimer;
        private CancellationTokenSource _toastCts;
        // [OPT-C2] Token riêng cho lyrics — dừng Task cũ khi bài đổi, tránh race condition
        private CancellationTokenSource _lyricsCts;

        private ScrollViewer _cachedLyricsScrollViewer = null;
        private ScrollViewer _cachedFullscreenLyricsScrollViewer = null;

        private string _nextSearchToken = "";
        private bool _isLoadingMoreSearch = false;
        // [OPT-Q4] Tách biệt query search và query home — tránh overwrite nhau
        private string _currentSearchQuery = "";
        private string _currentHomeQuery = "";


        private DispatcherTimer _sleepTimer;
        private int _sleepMinutesLeft = 0;
        private int _sleepTimerMode = 0;

        private MediaPlayer _appMediaPlayer;

        // [OPT-M6] Cache static brushes — avoid creating new objects every second/click (512MB RAM)
        private static readonly SolidColorBrush _lyricActiveBrush   = new SolidColorBrush(Windows.UI.Colors.White);
        private static readonly SolidColorBrush _lyricInactiveBrush = new SolidColorBrush(Windows.UI.Colors.Gray);
        private static readonly SolidColorBrush _greenBrush = new SolidColorBrush(Windows.UI.Colors.Green);
        private static readonly SolidColorBrush _whiteBrush = new SolidColorBrush(Windows.UI.Colors.White);



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
            HomeGenre5Carousel.ItemsSource = genre5Tracks;
            HomeGenre6Carousel.ItemsSource = genre6Tracks;
            HomeGenre7Carousel.ItemsSource = genre7Tracks;
            HomeGenre8Carousel.ItemsSource = genre8Tracks;

            FavoriteSongList.ItemsSource = favoriteTracks;
            DownloadedSongList.ItemsSource = downloadedTracks;
            SuggestionList.ItemsSource = searchSuggestions;
            HistorySongList.ItemsSource = historyTracks;
            LyricsListView.ItemsSource = currentLyrics;
            QueueListView.ItemsSource = currentQueueTracks;
            PlaylistsListView.ItemsSource = userPlaylists;
            DialogPlaylistList.ItemsSource = _youtubeUserPlaylists;

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

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Set InnerTube region from settings (affects all API calls)
            string region = "US";
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
                region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();
            InnerTubeClient.SetRegion(region);

            DataTransferManager.GetForCurrentView().DataRequested += MainPage_DataRequested;

            // [OPT-8] Parallel file I/O — independent operations run concurrently
            await Task.WhenAll(LoadFavoritesAsync(), LoadHistoryAsync(), LoadPlaylistsAsync(),
                LoadYouTubePlaylistsCacheAsync(), LoadYouTubeSubscriptionsCacheAsync());
            await LoadDownloadsAsync(); // depends on filesystem scan, runs after

            SyncBackgroundPlayer();

            if (!IsInternetAvailable())
            {
                ShowToast("Offline Mode. Play downloads in Library.");
            }
            else
            {
                if (homeTracks.Count == 0) 
                {
                    await LoadHomeRecommendations();
                }

                // Auto-sync YouTube data in background if logged in
                AutoSyncYouTubeAsync();
            }
        }

        private async void AutoSyncYouTubeAsync()
        {
            try
            {
                string token = await GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(token))
                {
                    await SyncAllAsync(token);
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        RefreshLibraryList();
                    });
                }
            }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DataTransferManager.GetForCurrentView().DataRequested -= MainPage_DataRequested;
            // Dispose timer to prevent leaks
            if (_bgTimer != null) { _bgTimer.Change(Timeout.Infinite, Timeout.Infinite); _bgTimer.Dispose(); _bgTimer = null; }
            // Stop gradient pulse
            if (_gradientPulseTimer != null) { _gradientPulseTimer.Stop(); }
        }

        private YouTubeTrack _trackToShare;

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
