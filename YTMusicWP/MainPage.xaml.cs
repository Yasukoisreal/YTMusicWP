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
    public class HomeSectionTemplateSelector : DataTemplateSelector
    {
        public DataTemplate NormalTemplate { get; set; }
        public DataTemplate QuickPicksTemplate { get; set; }
        public DataTemplate MultiTrackColumnTemplate { get; set; }
        public DataTemplate SpeedDialTemplate { get; set; }
        public DataTemplate FeaturedCardTemplate { get; set; }
        public DataTemplate MostDiscussedTemplate { get; set; }
        public DataTemplate LandscapeVideoTemplate { get; set; }
        public DataTemplate VideoTemplate { get; set; }
        public DataTemplate EditorialBannerTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            var section = item as YTMusicWP.InnerTubeClient.HomeSection;
            if (section != null)
            {
                switch (section.Layout)
                {
                    case YTMusicWP.InnerTubeClient.HomeSectionLayout.MultiTrackColumn:
                        return MultiTrackColumnTemplate ?? QuickPicksTemplate ?? NormalTemplate;
                    case YTMusicWP.InnerTubeClient.HomeSectionLayout.SpeedDial:
                        return SpeedDialTemplate ?? NormalTemplate;
                    case YTMusicWP.InnerTubeClient.HomeSectionLayout.FeaturedCard:
                        return FeaturedCardTemplate ?? NormalTemplate;
                    case YTMusicWP.InnerTubeClient.HomeSectionLayout.MostDiscussed:
                        return MostDiscussedTemplate ?? NormalTemplate;
                    case YTMusicWP.InnerTubeClient.HomeSectionLayout.LandscapeVideo:
                        return LandscapeVideoTemplate ?? NormalTemplate;
                    case YTMusicWP.InnerTubeClient.HomeSectionLayout.EditorialBanner:
                        return EditorialBannerTemplate ?? NormalTemplate;
                    case YTMusicWP.InnerTubeClient.HomeSectionLayout.QuickPicks:
                        return MultiTrackColumnTemplate ?? QuickPicksTemplate ?? NormalTemplate;
                    case YTMusicWP.InnerTubeClient.HomeSectionLayout.Normal:
                    default:
                        return NormalTemplate;
                }
            }
            return NormalTemplate;
        }
    }

    public sealed partial class MainPage : Page
    {
        private Anim.Storyboard _marqueeStoryboard;

        private void StartTitleMarquee()
        {
            StopTitleMarquee();

            TextBlock targetTitle;
            Canvas targetCanvas;
            TranslateTransform targetTranslate;

            if (_isAppleMusicStyle)
            {
                targetTitle = AppleMusicMainTitle;
                targetCanvas = AppleMusicTitleMarqueeCanvas;
                targetTranslate = AppleMusicTitleTranslate;
            }
            else
            {
                targetTitle = BigTitle;
                targetCanvas = TitleMarqueeCanvas;
                targetTranslate = TitleTranslate;
            }

            if (targetTitle == null || targetCanvas == null || targetTranslate == null) return;

            targetTitle.UpdateLayout();
            double textWidth = targetTitle.ActualWidth;
            double canvasWidth = targetCanvas.ActualWidth;
            if (canvasWidth <= 0) canvasWidth = targetCanvas.Width;
            if (double.IsNaN(canvasWidth) || canvasWidth <= 0) return;
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
            anim.KeyFrames.Add(new Anim.EasingDoubleKeyFrame
            {
                Value = 0,
                KeyTime = Anim.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + scrollDuration + 0.8)),
                EasingFunction = new Anim.CubicEase { EasingMode = Anim.EasingMode.EaseInOut }
            });
            anim.RepeatBehavior = new Anim.RepeatBehavior(1000); // repeat many times
            Anim.Storyboard.SetTarget(anim, targetTranslate);
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
            if (TitleTranslate != null) TitleTranslate.X = 0;
            if (AppleMusicTitleTranslate != null) AppleMusicTitleTranslate.X = 0;
        }

        private void TitleMarqueeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            TitleMarqueeCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        }

        private void AppleMusicTitleMarqueeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (AppleMusicTitleMarqueeCanvas != null)
            {
                AppleMusicTitleMarqueeCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            }
        }
        private void MiniLyricCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            MiniLyricCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        }
        private bool? _playIconIsPlaying;

        private void SetPlayPauseIcon(bool isPlaying)
        {
            // Pop only on a real change (this is also called to re-assert the same state)
            bool changed = _playIconIsPlaying.HasValue && _playIconIsPlaying.Value != isPlaying;
            _playIconIsPlaying = isPlaying;

            Symbol sym = isPlaying ? Symbol.Pause : Symbol.Play;
            MiniPlayIcon.Symbol = sym;
            BigPlayIcon.Symbol = sym;
            if (AppleMusicPlayBox != null)
                AppleMusicPlayBox.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
            if (AppleMusicPauseBox != null)
                AppleMusicPauseBox.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;

            if (changed)
            {
                Services.MotionHelper.Pop(MiniPlayIcon);
                if (NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible)
                {
                    Services.MotionHelper.Pop(BigPlayIcon);
                    Services.MotionHelper.Pop(isPlaying ? AppleMusicPauseBox : AppleMusicPlayBox);
                }
            }
        }

        private static readonly HttpClient _apiClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        private ObservableCollection<YouTubeTrack> searchResults = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> homeTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> favoriteTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> downloadedTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> historyTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<SearchSuggestionItem> searchSuggestions = new ObservableCollection<SearchSuggestionItem>();

        private ObservableCollection<YouTubeTrack> historyQuickGridTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> homeHistoryCarouselTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> podcastTracks = new ObservableCollection<YouTubeTrack>();
        private ObservableCollection<YouTubeTrack> audiobookTracks = new ObservableCollection<YouTubeTrack>();

        private ObservableCollection<UserPlaylist> userPlaylists = new ObservableCollection<UserPlaylist>();
        private UserPlaylist _currentViewingPlaylist = null;
        private string _currentViewingYtPlaylistId = null;
        private string _playlistContinuationToken = null;
        private bool _isLoadingMorePlaylist = false;
        private YouTubeTrack _trackPendingForPlaylist = null;



        private ObservableCollection<YouTubeTrack> currentQueueTracks = new ObservableCollection<YouTubeTrack>();

        private ObservableCollection<LyricLine> currentLyrics = new ObservableCollection<LyricLine>();
        private int currentLyricIndex = -1;

        private int _lyricFontSize = 22;

        private DispatcherTimer _typingTimer = new DispatcherTimer();
        private YouTubeTrack currentTrack = null;
        private volatile bool _isSliderManipulating = false;
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
        private static readonly SolidColorBrush _lyricInactiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 179, 179, 179));
        private static readonly SolidColorBrush _appleMusicLyricInactiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 155, 155, 155)); // #9B9B9B
        private static readonly SolidColorBrush _greenBrush = new SolidColorBrush(Windows.UI.Colors.Green);
        private static readonly SolidColorBrush _whiteBrush = new SolidColorBrush(Windows.UI.Colors.White);



        public MainPage()
        {
            this.InitializeComponent();
            LiveDebugDialog.ToastRequested += LiveDebugDialog_ToastRequested;
            LoginWebContainer.LoginWebView.NavigationStarting += LoginWebView_NavigationStarting;
            LoginWebContainer.LoginWebView.NavigationCompleted += LoginWebView_NavigationCompleted;
            LoginWebContainer.LoginWebView.NavigationFailed += LoginWebView_NavigationFailed;

            _appMediaPlayer = BackgroundMediaPlayer.Current;

            SearchSongList.ItemsSource = searchResults;

            HomeHistoryCarousel.ItemsSource = homeHistoryCarouselTracks;
            HomeQuickGrid.ItemsSource = historyQuickGridTracks;
            HomeQuickGrid.Visibility = Visibility.Collapsed;

            LibraryPanel.FavoriteSongList.ItemsSource = favoriteTracks;
            LibraryPanel.DownloadedSongList.ItemsSource = downloadedTracks;
            SuggestionList.ItemsSource = searchSuggestions;
            LibraryPanel.HistorySongList.ItemsSource = historyTracks;
            LyricsListView.ItemsSource = currentLyrics;
            QueueListView.ItemsSource = currentQueueTracks;
            LibraryPanel.PlaylistsListView.ItemsSource = userPlaylists;
            DialogPlaylistList.ItemsSource = _youtubeUserPlaylists;

            _typingTimer.Interval = TimeSpan.FromMilliseconds(400);
            _typingTimer.Tick += TypingTimer_Tick;

            _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _sleepTimer.Tick += SleepTimer_Tick;

            InnerTubeClient.LoadCookieAuthFromSettings();
            LoadSettings();
            SetupTimer();
            UpdateGreetingText();

            BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
            NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;

            Application.Current.Suspending += Current_Suspending;
            Application.Current.Resuming += Current_Resuming;
            UpdateStatusBarColor(false, animate: false);
            InitializeStartupSplash();
            InitializeHomePullToRefresh();
            CleanupOrphanedTempFilesAsync();
            InitializeListenTogether();
        }

        private async void CleanupOrphanedTempFilesAsync()
        {
            try
            {
                bool isPlaying = false;
                try
                {
                    isPlaying = Windows.Media.Playback.BackgroundMediaPlayer.Current.CurrentState == Windows.Media.Playback.MediaPlayerState.Playing;
                }
                catch { }

                var localFolder = ApplicationData.Current.LocalFolder;
                var files = await localFolder.GetFilesAsync();
                foreach (var file in files)
                {
                    string name = file.Name.ToLowerInvariant();
                    if (isPlaying && (name.StartsWith("temp_live_buf_") || name.StartsWith("temp_play_")))
                    {
                        continue;
                    }

                    if (name.StartsWith("temp_play_") || name.StartsWith("temp_live_buf_") || name.EndsWith(".tmp") || name.EndsWith(".tagging"))
                    {
                        try { await file.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
                    }
                }
            }
            catch { }

            try
            {
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                var tempFiles = await tempFolder.GetFilesAsync();
                foreach (var file in tempFiles)
                {
                    try { await file.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
                }
            }
            catch { }
        }

        #region Startup Splash Animation
        private static readonly TaskCompletionSource<bool> _splashReadyTcs = new TaskCompletionSource<bool>();
        private DispatcherTimer _splashTimeoutTimer;
        private bool _splashDismissed = false;

        private void InitializeStartupSplash()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings.Values;
                bool defaultSplash = !Services.MemoryHelper.IsLowMemoryDevice;
                bool enableSplash = SafeGetBool(settings, "EnableSplashAnimation", defaultSplash);

                if (!enableSplash)
                {
                    _splashReadyTcs.TrySetResult(true);
                    DismissSplash(animate: false);
                    return;
                }

                // Listen to animation completion and loaded events from XamlAnimatedGif
                XamlAnimatedGif.AnimationBehavior.Loaded += SplashAnimatedImage_Loaded;
                XamlAnimatedGif.AnimationBehavior.AnimationCompleted += SplashAnimatedImage_AnimationCompleted;
                XamlAnimatedGif.AnimationBehavior.Error += SplashAnimatedImage_Error;

                // Set RepeatBehavior to 1 iteration explicitly
                try
                {
                    XamlAnimatedGif.AnimationBehavior.SetRepeatBehavior(SplashAnimatedImage, new Windows.UI.Xaml.Media.Animation.RepeatBehavior(1));
                }
                catch { }

                // Fallback safety timer: dismiss at 3.4 seconds (112 frames * 30ms = 3.36s)
                _splashTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3400) };
                _splashTimeoutTimer.Tick += (s, e) =>
                {
                    DismissSplash(animate: true);
                };
                _splashTimeoutTimer.Start();
            }
            catch
            {
                _splashReadyTcs.TrySetResult(true);
                DismissSplash(animate: false);
            }
        }

        private void SplashAnimatedImage_Loaded(object sender, EventArgs e)
        {
            if (sender == SplashAnimatedImage)
            {
                var animator = XamlAnimatedGif.AnimationBehavior.GetAnimator(SplashAnimatedImage);
                if (animator != null)
                {
                    animator.CurrentFrameChanged += (s, args) =>
                    {
                        // Stop immediately on the final frame so it cannot loop back to start
                        if (animator.CurrentFrameIndex >= animator.FrameCount - 1)
                        {
                            try { animator.Pause(); } catch { }
                            DismissSplash(animate: true);
                        }
                    };
                }
            }
        }

        private void SplashAnimatedImage_AnimationCompleted(object sender, XamlAnimatedGif.AnimationCompletedEventArgs e)
        {
            if (sender == SplashAnimatedImage)
            {
                DismissSplash(animate: true);
            }
        }

        private void SplashAnimatedImage_Error(object sender, XamlAnimatedGif.AnimationErrorEventArgs e)
        {
            if (sender == SplashAnimatedImage)
            {
                DismissSplash(animate: false);
            }
        }

        private void SplashOverlay_Tapped(object sender, TappedRoutedEventArgs e)
        {
            DismissSplash(animate: true);
        }

        private void DismissSplash(bool animate = true)
        {
            if (_splashDismissed) return;
            _splashDismissed = true;

            _splashReadyTcs.TrySetResult(true);

            if (_splashTimeoutTimer != null)
            {
                _splashTimeoutTimer.Stop();
                _splashTimeoutTimer = null;
            }

            if (!animate || SplashFadeOutStoryboard == null)
            {
                CleanupSplash();
                return;
            }

            try
            {
                SplashFadeOutStoryboard.Begin();
            }
            catch
            {
                CleanupSplash();
            }
        }

        private void SplashFadeOutStoryboard_Completed(object sender, object e)
        {
            CleanupSplash();
        }

        private void CleanupSplash()
        {
            try
            {
                XamlAnimatedGif.AnimationBehavior.Loaded -= SplashAnimatedImage_Loaded;
                XamlAnimatedGif.AnimationBehavior.AnimationCompleted -= SplashAnimatedImage_AnimationCompleted;
                XamlAnimatedGif.AnimationBehavior.Error -= SplashAnimatedImage_Error;
            }
            catch { }

            if (SplashOverlay != null)
            {
                SplashOverlay.Visibility = Visibility.Collapsed;
            }
            if (SplashAnimatedImage != null)
            {
                try
                {
                    XamlAnimatedGif.AnimationBehavior.SetSourceUri(SplashAnimatedImage, null);
                }
                catch { }
            }
        }
        #endregion

        private void HomeQuickGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var wrapGrid = HomeQuickGrid.ItemsPanelRoot as ItemsWrapGrid;
            if (wrapGrid != null && e.NewSize.Width > 0)
            {
                wrapGrid.ItemWidth = Math.Max(0, e.NewSize.Width / 2);
                wrapGrid.ItemHeight = 66;
            }
        }

        private void UpdateGreetingText()
        {
            if (GreetingText == null) return;
            int hour = DateTime.Now.Hour;
            if (InnerTubeClient.CurrentLanguage == "vi")
            {
                if (hour < 12) GreetingText.Text = "Chào buổi sáng";
                else if (hour < 18) GreetingText.Text = "Chào buổi chiều";
                else GreetingText.Text = "Chào buổi tối";
            }
            else
            {
                if (hour < 12) GreetingText.Text = "Good morning";
                else if (hour < 18) GreetingText.Text = "Good afternoon";
                else GreetingText.Text = "Good evening";
            }
        }

        public void UpdateLocalizedUI()
        {
            try
            {
                UpdateGreetingText();

                bool isVi = (InnerTubeClient.CurrentLanguage == "vi");

                // Bottom Nav
                if (NavHomeText != null) NavHomeText.Text = isVi ? "Trang chủ" : "Home";
                if (NavSearchText != null) NavSearchText.Text = isVi ? "Tìm kiếm" : "Search";
                if (NavLibraryText != null) NavLibraryText.Text = isVi ? "Thư viện" : "Library";
                if (NavSamplesText != null) NavSamplesText.Text = isVi ? "Đoạn nhạc" : "Samples";
                if (ShortsView != null) UpdateSamplesLocalizedText();

                // Home Headers
                if (HomePullText != null) HomePullText.Text = isVi ? "Kéo để làm mới" : "Pull to refresh";
                if (HomeHistoryTitleText != null) HomeHistoryTitleText.Text = isVi ? "Nghe lại" : "Jump back in";
                if (HomeArtistsTitleText != null) HomeArtistsTitleText.Text = isVi ? "Nghệ sĩ nghe gần đây" : "Recently played artists";
                if (HomeChartsTitleText != null) HomeChartsTitleText.Text = isVi ? "Bảng xếp hạng hàng đầu" : "Top Charts";

                // Chips & Search
                UpdateDefaultHomeChips();
                UpdateDefaultSearchChips();
            }
            catch { }
        }

        // ==========================================
        // BACKGROUND AUDIO TASK RECOVERY
        // The OS can cancel the background audio task (e.g. reason=SystemPolicy while it sits paused and the
        // Samples MediaElement owns the audio). The cached MediaPlayer proxy is then dead and every call throws,
        // so the polling timers stop at the first failure (_playerDisconnected) and the proxy is re-acquired
        // when playback is needed again (PlayTrack, leaving Samples, resume).
        // ==========================================
        private volatile bool _playerDisconnected = false;

        private void MarkPlayerDisconnected()
        {
            if (_playerDisconnected) return;
            _playerDisconnected = true;
            System.Diagnostics.Debug.WriteLine("[Player] background audio task gone; polling paused until reconnect");
        }

        /// <summary>Re-acquires BackgroundMediaPlayer.Current (restarting the audio task) if it was lost. Returns true if usable.</summary>
        private bool EnsureBackgroundPlayer()
        {
            if (!_playerDisconnected) return true;
            try
            {
                var old = _appMediaPlayer;
                try { if (old != null) old.CurrentStateChanged -= BackgroundMediaPlayer_CurrentStateChanged; } catch { }
                _appMediaPlayer = BackgroundMediaPlayer.Current;
                _appMediaPlayer.CurrentStateChanged -= BackgroundMediaPlayer_CurrentStateChanged;
                _appMediaPlayer.CurrentStateChanged += BackgroundMediaPlayer_CurrentStateChanged;
                BackgroundMediaPlayer.MessageReceivedFromBackground -= BackgroundMediaPlayer_MessageReceivedFromBackground;
                BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
                var probe = _appMediaPlayer.CurrentState; // throws if still unusable
                _playerDisconnected = false;
                System.Diagnostics.Debug.WriteLine("[Player] background audio task reconnected");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Player] reconnect failed: " + ex.Message);
                return false;
            }
        }

        private void Current_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            try
            {
                if (_bgTimer != null)
                {
                    _bgTimer.Dispose();
                    _bgTimer = null;
                }
                BackgroundMediaPlayer.MessageReceivedFromBackground -= BackgroundMediaPlayer_MessageReceivedFromBackground;
                _appMediaPlayer.CurrentStateChanged -= BackgroundMediaPlayer_CurrentStateChanged;
                NetworkInformation.NetworkStatusChanged -= NetworkInformation_NetworkStatusChanged;
            }
            catch { }

            // 512MB phones reclaim memory by terminating suspended apps, largest first: shrink before going to sleep
            try
            {
                if (_shortsIsOpen) ReleaseSampleVideoForSuspend();
                Services.MemoryHelper.TrimMemory();
                Services.MemoryHelper.Mark("Suspended");
            }
            catch { }
        }

        private async void Current_Resuming(object sender, object e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    UpdateGreetingText();

                    // FIX: Làm mới lại liên kết với OS Player khi bị ngắt kết nối do ngủ đông
                    _appMediaPlayer = BackgroundMediaPlayer.Current;
                    _playerDisconnected = false;
                    Services.MemoryHelper.Mark("Resumed");
                    _isSliderManipulating = false; // Mở khóa thanh tua nhạc nếu bị kẹt

                    if (_bgTimer == null)
                    {
                        _bgTimer = new Timer(TimerCallback, null, 0, 1000);
                    }
                    // FIX #5: Unsubscribe trước để tránh double subscription khi fast resume
                    BackgroundMediaPlayer.MessageReceivedFromBackground -= BackgroundMediaPlayer_MessageReceivedFromBackground;
                    BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
                    _appMediaPlayer.CurrentStateChanged -= BackgroundMediaPlayer_CurrentStateChanged;
                    _appMediaPlayer.CurrentStateChanged += BackgroundMediaPlayer_CurrentStateChanged;
                    NetworkInformation.NetworkStatusChanged -= NetworkInformation_NetworkStatusChanged;
                    NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
                    SyncBackgroundPlayer();

                    bool npVisible = (NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible)
                                   || (FullscreenLyricsView != null && FullscreenLyricsView.Visibility == Visibility.Visible);
                    UpdateStatusBarColor(npVisible, animate: false);

                    if (YTMusicWP.Services.ListenTogether.ListenTogetherManager.Instance.InRoom)
                    {
                        var ignoredSync = YTMusicWP.Services.ListenTogether.ListenTogetherManager.Instance.RequestSyncAsync();
                    }
                }
                catch { }

                // The Samples video was released on suspend: reload the clip that was on screen
                if (_shortsIsOpen) ResumeSampleVideoAfterSuspend();

                await FlushPendingHistoryAsync();
            });
        }

        private async Task FlushPendingHistoryAsync()
        {
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (ls.ContainsKey("PendingHistory"))
                {
                    string pending = ls["PendingHistory"]?.ToString();
                    ls.Remove("PendingHistory");

                    if (!string.IsNullOrEmpty(pending))
                    {
                        var parts = pending.Split(new[] { "^^^" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            var props = part.Split('|');
                            if (props.Length >= 4)
                            {
                                var track = new YouTubeTrack { VideoId = props[0], Title = props[1], ChannelName = props[2], ThumbnailUrl = props[3] };
                                await YTMusicWP.Services.DatabaseHelper.AddOrUpdateHistoryAsync(track);
                                
                                // Update UI if needed
                                var existingHistory = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                                if (existingHistory != null) historyTracks.Remove(existingHistory);
                                historyTracks.Insert(0, track);
                                if (historyTracks.Count > 50) historyTracks.RemoveAt(historyTracks.Count - 1);
                            }
                        }
                        RefreshHomeHistorySections();
                    }
                }
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


        private readonly Services.MotionGroup _toastMotion = new Services.MotionGroup();

        private async void ShowToast(string message, int durationMs = 2500)
        {
            // Cancel CTS cũ an toàn (không gọi Dispose() ngay để tránh race condition với Task.Delay)
            var oldCts = _toastCts;
            _toastCts = new CancellationTokenSource();
            if (oldCts != null)
            {
                try { oldCts.Cancel(); } catch { }
            }
            var token = _toastCts.Token;

            // Rises into place while fading in; a new message while one is showing just continues from where it is
            ToastText.Text = message;
            var toastT = Services.MotionHelper.EnsureTransform(ToastNotification);
            if (ToastNotification.Visibility != Visibility.Visible)
            {
                ToastNotification.Opacity = 0;
                toastT.TranslateY = 24;
                ToastNotification.Visibility = Visibility.Visible;
            }
            _toastMotion.Animate(Services.MotionHelper.EnterMs, Anim.EasingMode.EaseOut, null,
                Services.MotionHelper.To(ToastNotification, "Opacity", 1),
                Services.MotionHelper.To(toastT, "TranslateY", 0));
            try
            {
                await Task.Delay(durationMs, token);
                _toastMotion.Animate(Services.MotionHelper.ExitMs, Anim.EasingMode.EaseIn,
                    () => ToastNotification.Visibility = Visibility.Collapsed,
                    Services.MotionHelper.To(ToastNotification, "Opacity", 0),
                    Services.MotionHelper.To(toastT, "TranslateY", 12));
            }
            catch (OperationCanceledException) { /* Toast mới đã thay thế */ }
        }

        private bool IsInternetAvailable()
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                return (profile != null && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);
            }
            catch
            {
                return false;
            }
        }

        private async void NetworkInformation_NetworkStatusChanged(object sender)
        {
            try
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (IsInternetAvailable())
                    {
                        if (homeTracks.Count == 0)
                        {
                            var ignored = LoadHomeRecommendations();
                        }
                        if (IsWifiConnected())
                        {
                            var ignoredSmart = TriggerSmartDownloadsAsync();
                        }
                    }
                });
            }
            catch { }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Wait for startup splash animation to finish or user tap to skip
            // Ensures 100% smooth 30fps animation without CPU/Disk/Network contention
            if (_splashReadyTcs != null)
            {
                await _splashReadyTcs.Task;
            }

            var ignoredClean = CleanTempStreamsInternalAsync();
            var ignoredDl = CleanStaleDownloadsAsync();

            await YTMusicWP.Services.DatabaseHelper.InitializeAsync();

            // Set InnerTube region & language from settings (affects all API calls)
            string region = "US";
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
                region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();
            else
                region = DetectOsRegion();
            InnerTubeClient.SetRegion(region);

            string lang = "AUTO";
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("AppLanguage"))
                lang = ApplicationData.Current.LocalSettings.Values["AppLanguage"].ToString();

            if (lang == "AUTO")
                InnerTubeClient.SetLanguage(InnerTubeClient.DetectLanguageFromRegion(region));
            else
                InnerTubeClient.SetLanguage(lang);

            // Cookie auth is now loaded in the constructor

            DataTransferManager.GetForCurrentView().DataRequested += MainPage_DataRequested;

            await FlushPendingHistoryAsync();

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
                    // Fire-and-forget Home network loading so it runs in parallel with UI/Disk
                    var ignored = LoadHomeRecommendations();
                }

                // Upgrade old 16:9 thumbnails in favorites to genuine 1:1 YTM square album art
                var _ = UpgradeCachedThumbnailsAsync();

                // Auto-sync YouTube data in background if logged in
                AutoSyncYouTubeAsync();

                // Trigger smart downloads on Wi-Fi if enabled
                if (IsWifiConnected())
                {
                    var ignoredSmart = TriggerSmartDownloadsAsync();
                }
            }

            // Handle Secondary Tile deep link
            string args = e.Parameter as string;
            if (!string.IsNullOrEmpty(args))
            {
                HandleTileDeepLink(args);
            }
        }

        public void HandleTileDeepLink(string args)
        {
            if (string.IsNullOrEmpty(args)) return;
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                await Task.Delay(350); // Ensure collections are populated
                if (args == "playlist:liked" || args == "favorites")
                {
                    SwitchTab(2); // Library tab
                    OpenLikedSongsView();
                }
                else if (args.StartsWith("ytplaylist:"))
                {
                    SwitchTab(2);
                    string ytplId = args.Substring("ytplaylist:".Length);
                    var ytpl = _youtubeUserPlaylists.FirstOrDefault(p => p.PlaylistId == ytplId);
                    OpenYouTubePlaylist(ytplId, ytpl != null ? ytpl.Title : "Playlist", ytpl != null ? ytpl.ThumbnailUrl : null);
                }
                else if (args.StartsWith("playlist:"))
                {
                    SwitchTab(2);
                    string plName = args.Substring("playlist:".Length);
                    var pl = userPlaylists.FirstOrDefault(p => p.Name == plName);
                    if (pl != null)
                    {
                        OpenUserPlaylist(pl);
                    }
                }
                else if (args.StartsWith("artist:"))
                {
                    string artist = args.Substring("artist:".Length);
                    OpenArtistProfile(artist, artist, true);
                }
            });
        }

        private async void AutoSyncYouTubeAsync()
        {
            try
            {
                string token = await GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(token) || YTMusicWP.InnerTubeClient.HasCookieAuth)
                {
                    await SyncAllAsync(token);
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        RefreshLibraryList();
                        if (IsWifiConnected())
                        {
                            var ignoredSmart = TriggerSmartDownloadsAsync();
                        }
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
        }

        private YouTubeTrack _trackToShare;

        private static readonly SolidColorBrush _filterActiveBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 185, 84)); // #1DB954
        private static readonly SolidColorBrush _filterInactiveBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)); // #232323

        // [REMOVED] SearchYouTubeMusic() — dead code, tất cả search paths đều dùng FetchMusicList()

        private void SongList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var track = e.ClickedItem as YouTubeTrack;
            if (track != null)
            {
                var itemsControl = sender as ItemsControl;
                var source = itemsControl?.ItemsSource as IEnumerable<YouTubeTrack>;
                PlayTrack(track, source);
            }
        }

        private YouTubeTrack _bottomSheetTrack;


    }
}
