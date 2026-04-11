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
        private static readonly HttpClient _apiClient = new HttpClient();
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

        private ObservableCollection<LyricLine> currentLyrics = new ObservableCollection<LyricLine>();
        private int currentLyricIndex = -1;

        private int _baseLyricSize = 18;
        private int _highlightLyricSize = 24;

        private DispatcherTimer _typingTimer = new DispatcherTimer();
        private YouTubeTrack currentTrack = null;
        private bool _isSliderManipulating = false;
        private Timer _bgTimer;

        private ScrollViewer _cachedLyricsScrollViewer = null;

        private string _nextSearchToken = "";
        private bool _isLoadingMoreSearch = false;
        private string _currentSearchQuery = "";

        private bool _isAuthProcessing = false;

        private DispatcherTimer _sleepTimer;
        private int _sleepMinutesLeft = 0;
        private int _sleepTimerMode = 0;

        private MediaPlayer _appMediaPlayer;

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
                if (_bgTimer != null) _bgTimer.Change(0, 1000);
                BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
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
            if (LoginWebContainer.Visibility == Visibility.Visible)
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
                PlaylistDetailsView.Visibility = Visibility.Collapsed;
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

        private async void ShowToast(string message, int durationMs = 2500)
        {
            ToastText.Text = message;
            ToastNotification.Visibility = Visibility.Visible;
            await Task.Delay(durationMs);
            ToastNotification.Visibility = Visibility.Collapsed;
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
                    Symbol sym = (_appMediaPlayer.CurrentState == MediaPlayerState.Playing) ? Symbol.Pause : Symbol.Play;
                    MiniPlayIcon.Symbol = sym;
                    BigPlayIcon.Symbol = sym;

                    string title = localSettings.ContainsKey("CurrentTitle") ? localSettings["CurrentTitle"].ToString() : "Unknown";
                    string artist = localSettings.ContainsKey("CurrentArtist") ? localSettings["CurrentArtist"].ToString() : "Unknown";
                    string vid = localSettings.ContainsKey("CurrentVideoId") ? localSettings["CurrentVideoId"].ToString() : "";
                    string thumb = localSettings.ContainsKey("CurrentThumbnail") ? localSettings["CurrentThumbnail"].ToString() : "";

                    MiniTitle.Text = title; BigTitle.Text = title;
                    MiniArtist.Text = artist; BigArtist.Text = artist;

                    MenuTitle.Text = title;
                    MenuArtist.Text = artist;

                    if (!string.IsNullOrEmpty(thumb))
                    {
                        var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(thumb, UriKind.Absolute));
                        bigBmp.DecodePixelWidth = 480;
                        BigCoverImage.ImageSource = bigBmp;
                        MenuCoverImage.ImageSource = bigBmp;

                        var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(thumb, UriKind.Absolute));
                        miniBmp.DecodePixelWidth = 100;
                        MiniCoverImage.ImageSource = miniBmp;
                    }

                    if (!string.IsNullOrEmpty(vid))
                    {
                        currentTrack = new YouTubeTrack { VideoId = vid, Title = title, ChannelName = artist, ThumbnailUrl = thumb };
                        bool isFav = favoriteTracks.Any(t => t.VideoId == vid);
                        BigHeartBtn.Content = isFav ? "♥" : "♡";
                        BigHeartBtn.Foreground = new SolidColorBrush(isFav ? Windows.UI.Colors.Green : Windows.UI.Colors.White);

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

            if (!IsInternetAvailable()) ShowToast("Offline Mode. Play downloads in Library.");
            else if (string.IsNullOrEmpty(ApiKeyTextBox.Text.Trim())) ShowToast("Swipe to Settings and add API Key.");
            else if (homeTracks.Count == 0) await LoadHomeRecommendations();
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
                downloadedTracks.Clear();
                foreach (var file in files)
                {
                    if (file.Name.EndsWith(".m4a") && !file.Name.StartsWith("temp_play_"))
                    {
                        downloadedTracks.Add(new YouTubeTrack
                        {
                            VideoId = "LOCAL:" + file.Name,
                            Title = file.Name.Replace(".m4a", ""),
                            ChannelName = "Offline Track",
                            ThumbnailUrl = "ms-appx:///Assets/Square71x71Logo.scale-240.png"
                        });
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
                PlaylistDetailsView.Visibility = Visibility.Collapsed;
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
                PlaylistSongsList.ItemsSource = _currentViewingPlaylist.Tracks;
                PlaylistDetailsView.Visibility = Visibility.Visible;
            }
        }

        private void ClosePlaylistDetails_Click(object sender, RoutedEventArgs e)
        {
            PlaylistDetailsView.Visibility = Visibility.Collapsed;
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

        private async void SaveHistoryAsync()
        {
            await SaveHistoryAsyncTask();
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
                        var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(thumb, UriKind.Absolute));
                        bigBmp.DecodePixelWidth = 480;
                        BigCoverImage.ImageSource = bigBmp;
                        MenuCoverImage.ImageSource = bigBmp;

                        var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(thumb, UriKind.Absolute));
                        miniBmp.DecodePixelWidth = 100;
                        MiniCoverImage.ImageSource = miniBmp;
                    }

                    if (!string.IsNullOrEmpty(vid))
                    {
                        currentTrack = new YouTubeTrack { VideoId = vid, Title = title, ChannelName = artist, ThumbnailUrl = thumb };

                        bool isFav = favoriteTracks.Any(t => t.VideoId == vid);
                        BigHeartBtn.Content = isFav ? "♥" : "♡";
                        BigHeartBtn.Foreground = new SolidColorBrush(isFav ? Windows.UI.Colors.Green : Windows.UI.Colors.White);

                        var ignored = UpdateLyricsAsync(title, artist);
                    }
                });
            }
        }

        private void LoadSettings()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (settings.ContainsKey("YouTubeApiKey")) ApiKeyTextBox.Text = settings["YouTubeApiKey"].ToString();
            if (settings.ContainsKey("GoogleClientId")) ClientIdTextBox.Text = settings["GoogleClientId"].ToString();
            if (settings.ContainsKey("GoogleClientSecret")) ClientSecretTextBox.Text = settings["GoogleClientSecret"].ToString();

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
                LoginStatusText.Foreground = new SolidColorBrush(Windows.UI.Colors.Green);
            }
            bool isShuffle = settings.ContainsKey("ShuffleMode") ? (bool)settings["ShuffleMode"] : false;
            ShuffleIcon.Foreground = new SolidColorBrush(isShuffle ? Windows.UI.Colors.Green : Windows.UI.Colors.White);
            int repeatMode = settings.ContainsKey("RepeatMode") ? (int)settings["RepeatMode"] : 0;
            UpdateRepeatUI(repeatMode);
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            string newKey = ApiKeyTextBox.Text.Trim();
            ApplicationData.Current.LocalSettings.Values["YouTubeApiKey"] = newKey;
            ApplicationData.Current.LocalSettings.Values["GoogleClientId"] = ClientIdTextBox.Text.Trim();
            ApplicationData.Current.LocalSettings.Values["GoogleClientSecret"] = ClientSecretTextBox.Text.Trim();

            var selectedRegion = RegionComboBox.SelectedItem as ComboBoxItem;
            if (selectedRegion != null && selectedRegion.Tag != null)
            {
                ApplicationData.Current.LocalSettings.Values["TrendingRegion"] = selectedRegion.Tag.ToString();
            }

            ShowToast("Settings Saved!");

            if (!string.IsNullOrEmpty(newKey) && IsInternetAvailable())
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
                    var items = json["items"].Reverse();

                    foreach (var item in items)
                    {
                        try
                        {
                            string vidId = item["id"]?.ToString();
                            if (string.IsNullOrEmpty(vidId)) continue;
                            if (favoriteTracks.Any(t => t.VideoId == vidId)) continue;

                            string title = System.Net.WebUtility.HtmlDecode(item["snippet"]?["title"]?.ToString() ?? "");
                            string channel = System.Net.WebUtility.HtmlDecode(item["snippet"]?["channelTitle"]?.ToString() ?? "");

                            if (channel.EndsWith(" - Topic")) channel = channel.Replace(" - Topic", "");
                            if (channel.EndsWith(" - Chủ đề")) channel = channel.Replace(" - Chủ đề", "");

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

        private void SearchSongList_Loaded(object sender, RoutedEventArgs e)
        {
            var sv = GetScrollViewer(SearchSongList);
            if (sv != null)
            {
                sv.ViewChanged += async (s, args) =>
                {
                    if (sv.VerticalOffset >= sv.ScrollableHeight - 200 && !_isLoadingMoreSearch && !string.IsNullOrEmpty(_nextSearchToken))
                    {
                        _isLoadingMoreSearch = true;
                        SearchLoading.Visibility = Visibility.Visible;

                        var tracks = await SearchYouTubeMusic(_currentSearchQuery, _nextSearchToken);
                        if (tracks != null) foreach (var t in tracks) searchResults.Add(t);

                        SearchLoading.Visibility = Visibility.Collapsed;
                        _isLoadingMoreSearch = false;
                    }
                };
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsInternetAvailable()) { ShowToast("No Internet"); return; }
            _typingTimer.Stop(); SuggestionPopup.Visibility = Visibility.Collapsed;
            string query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            _currentSearchQuery = query;
            _nextSearchToken = "";

            SearchLoading.Visibility = Visibility.Visible;
            DefaultSearchUI.Visibility = Visibility.Collapsed;

            SearchSongList.Visibility = Visibility.Visible;
            SearchSongList.UpdateLayout();

            var sv = GetScrollViewer(SearchSongList);
            if (sv != null)
            {
                sv.ViewChanged -= SearchScrollViewer_ViewChanged;
                sv.ViewChanged += SearchScrollViewer_ViewChanged;
            }

            searchResults.Clear();
            PerformSearch(query);
        }

        private async void PerformSearch(string query)
        {
            var tracks = await SearchYouTubeMusic(query);
            if (tracks != null && tracks.Count > 0) foreach (var t in tracks) searchResults.Add(t);
            else ShowToast("Quota exceeded or no results.");
            SearchLoading.Visibility = Visibility.Collapsed;
        }

        private async void SearchScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv != null && sv.VerticalOffset >= sv.ScrollableHeight - 200 && !_isLoadingMoreSearch && !string.IsNullOrEmpty(_nextSearchToken))
            {
                _isLoadingMoreSearch = true;
                SearchLoading.Visibility = Visibility.Visible;

                var tracks = await SearchYouTubeMusic(_currentSearchQuery, _nextSearchToken);
                if (tracks != null) foreach (var t in tracks) searchResults.Add(t);

                SearchLoading.Visibility = Visibility.Collapsed;
                _isLoadingMoreSearch = false;
            }
        }

        private async Task LoadHomeRecommendations()
        {
            HomeLoading.Visibility = Visibility.Visible;

            string year = DateTime.Now.Year.ToString();

            _currentSearchQuery = "top hits " + year + " \"Topic\"";
            var trending = await FetchMusicList(_currentSearchQuery);
            if (trending != null) foreach (var t in trending) homeTracks.Add(t);

            var pop = await FetchMusicList("pop music hits \"Topic\"");
            if (pop != null) foreach (var t in pop) popTracks.Add(t);

            var lofi = await FetchMusicList("lofi chill beats \"Topic\"");
            if (lofi != null) foreach (var t in lofi) lofiTracks.Add(t);

            var workout = await FetchMusicList("workout gym music \"Topic\"");
            if (workout != null) foreach (var t in workout) workoutTracks.Add(t);

            HomeLoading.Visibility = Visibility.Collapsed;
        }

        public async Task<List<YouTubeTrack>> FetchMusicList(string query, string pageToken = "")
        {
            string apiKey = ApiKeyTextBox.Text.Trim();
            if (string.IsNullOrEmpty(apiKey)) return null;

            string url = "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=10&q=" + Uri.EscapeDataString(query) + "&type=video&videoCategoryId=10&key=" + apiKey;

            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
            {
                string region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();
                if (region != "US") url += "&regionCode=" + region;
            }

            if (!string.IsNullOrEmpty(pageToken)) url += "&pageToken=" + pageToken;

            var list = new List<YouTubeTrack>();
            try
            {
                var response = await _apiClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                foreach (var item in json["items"])
                {
                    try
                    {
                        if (item["id"]?["videoId"] == null) continue;
                        string vidId = item["id"]["videoId"].ToString();
                        string title = System.Net.WebUtility.HtmlDecode(item["snippet"]?["title"]?.ToString() ?? "");
                        string channel = System.Net.WebUtility.HtmlDecode(item["snippet"]?["channelTitle"]?.ToString() ?? "");

                        if (channel.EndsWith(" - Topic")) channel = channel.Replace(" - Topic", "");
                        if (channel.EndsWith(" - Chủ đề")) channel = channel.Replace(" - Chủ đề", "");

                        var thumbs = item["snippet"]?["thumbnails"];
                        string thumbUrl = thumbs?["maxres"]?["url"]?.ToString() ?? thumbs?["standard"]?["url"]?.ToString() ?? thumbs?["high"]?["url"]?.ToString() ?? thumbs?["medium"]?["url"]?.ToString();

                        list.Add(new YouTubeTrack { VideoId = vidId, Title = title, ChannelName = channel, ThumbnailUrl = thumbUrl });
                    }
                    catch { continue; }
                }
                return list;
            }
            catch { return null; }
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

        public async Task<List<YouTubeTrack>> SearchYouTubeMusic(string query, string pageToken = "")
        {
            string apiKey = ApiKeyTextBox.Text.Trim();
            if (string.IsNullOrEmpty(apiKey)) return null;

            string optimizedQuery = query + " \"Topic\"";
            string url = "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=20&q=" + Uri.EscapeDataString(optimizedQuery) + "&type=video&videoCategoryId=10&key=" + apiKey;

            if (!string.IsNullOrEmpty(pageToken)) url += "&pageToken=" + pageToken;

            var list = new List<YouTubeTrack>();
            try
            {
                var response = await _apiClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                _nextSearchToken = json["nextPageToken"]?.ToString() ?? "";

                foreach (var item in json["items"])
                {
                    try
                    {
                        if (item["id"]?["videoId"] == null) continue;
                        string vidId = item["id"]["videoId"].ToString();
                        string title = System.Net.WebUtility.HtmlDecode(item["snippet"]?["title"]?.ToString() ?? "");
                        string channel = System.Net.WebUtility.HtmlDecode(item["snippet"]?["channelTitle"]?.ToString() ?? "");

                        if (channel.EndsWith(" - Topic")) channel = channel.Replace(" - Topic", "");
                        if (channel.EndsWith(" - Chủ đề")) channel = channel.Replace(" - Chủ đề", "");

                        var thumbs = item["snippet"]?["thumbnails"];
                        string thumbUrl = thumbs?["maxres"]?["url"]?.ToString() ?? thumbs?["standard"]?["url"]?.ToString() ?? thumbs?["high"]?["url"]?.ToString() ?? thumbs?["medium"]?["url"]?.ToString();

                        list.Add(new YouTubeTrack { VideoId = vidId, Title = title, ChannelName = channel, ThumbnailUrl = thumbUrl });
                    }
                    catch { continue; }
                }

                if (list.Count < 3 && string.IsNullOrEmpty(pageToken))
                {
                    string backupQuery = query + " audio";
                    string backupUrl = "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=20&q=" + Uri.EscapeDataString(backupQuery) + "&type=video&videoCategoryId=10&key=" + apiKey;
                    var backupResponse = await _apiClient.GetStringAsync(backupUrl);
                    var backupJson = JObject.Parse(backupResponse);

                    list.Clear();
                    foreach (var item in backupJson["items"])
                    {
                        try
                        {
                            if (item["id"]?["videoId"] == null) continue;
                            string vidId = item["id"]["videoId"].ToString();
                            string title = System.Net.WebUtility.HtmlDecode(item["snippet"]?["title"]?.ToString() ?? "");
                            string channel = System.Net.WebUtility.HtmlDecode(item["snippet"]?["channelTitle"]?.ToString() ?? "");

                            var thumbs = item["snippet"]?["thumbnails"];
                            string thumbUrl = thumbs?["maxres"]?["url"]?.ToString() ?? thumbs?["standard"]?["url"]?.ToString() ?? thumbs?["high"]?["url"]?.ToString() ?? thumbs?["medium"]?["url"]?.ToString();

                            list.Add(new YouTubeTrack { VideoId = vidId, Title = title, ChannelName = channel, ThumbnailUrl = thumbUrl });
                        }
                        catch { continue; }
                    }
                }

                return list;
            }
            catch { return null; }
        }

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
            currentLyrics.Clear();
            currentLyricIndex = -1;

            if (FindName("LyricsFallbackScrollViewer") != null) ((ScrollViewer)this.FindName("LyricsFallbackScrollViewer")).Visibility = Visibility.Collapsed;
            LyricsListView.Visibility = Visibility.Visible;
            LyricsLoadingBar.Visibility = Visibility.Visible;

            try
            {
                if (title.StartsWith("LOCAL:"))
                {
                    if (FindName("LyricsFallbackText") != null) ((TextBlock)this.FindName("LyricsFallbackText")).Text = "No lyrics for offline files.";
                    if (FindName("LyricsFallbackScrollViewer") != null) ((ScrollViewer)this.FindName("LyricsFallbackScrollViewer")).Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                    LyricsLoadingBar.Visibility = Visibility.Collapsed;
                    return;
                }

                string cleanTitle = title.Replace(" (Official Video)", "").Replace(" (Lyric Video)", "").Replace(" (Audio)", "").Split('-')[0].Trim();
                string cleanArtist = artist.Replace(" - Topic", "");

                string url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) + "&artist_name=" + Uri.EscapeDataString(cleanArtist);
                var response = await _apiClient.GetStringAsync(url);
                var jsonArray = JArray.Parse(response);

                string syncedLyrics = null;
                string plainLyrics = null;

                if (jsonArray.Count > 0)
                {
                    syncedLyrics = jsonArray[0]["syncedLyrics"]?.ToString();
                    plainLyrics = jsonArray[0]["plainLyrics"]?.ToString();
                }

                if (string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    string url2 = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(cleanTitle + " " + cleanArtist);
                    var response2 = await _apiClient.GetStringAsync(url2);
                    var jsonArray2 = JArray.Parse(response2);
                    if (jsonArray2.Count > 0)
                    {
                        syncedLyrics = jsonArray2[0]["syncedLyrics"]?.ToString();
                        plainLyrics = jsonArray2[0]["plainLyrics"]?.ToString();
                    }
                }

                if (!string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    var lines = syncedLyrics.Split('\n');
                    List<LyricLine> parsedLines = new List<LyricLine>();

                    foreach (var line in lines)
                    {
                        string tempLine = line.Trim();
                        List<TimeSpan> times = new List<TimeSpan>();

                        while (tempLine.StartsWith("[") && tempLine.IndexOf(']') > 0)
                        {
                            int bracketEnd = tempLine.IndexOf(']');
                            string timeStr = tempLine.Substring(1, bracketEnd - 1);
                            TimeSpan parsedTime = ParseLrcTime(timeStr);
                            times.Add(parsedTime);
                            tempLine = tempLine.Substring(bracketEnd + 1).Trim();
                        }

                        if (times.Count > 0)
                        {
                            string text = string.IsNullOrWhiteSpace(tempLine) ? "♪" : tempLine;
                            foreach (var t in times)
                            {
                                parsedLines.Add(new LyricLine { Time = t, Text = text });
                            }
                        }
                    }

                    parsedLines = parsedLines.OrderBy(l => l.Time).ToList();
                    foreach (var p in parsedLines) currentLyrics.Add(p);

                    currentLyrics.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "" });
                }
                else if (!string.IsNullOrWhiteSpace(plainLyrics))
                {
                    if (FindName("LyricsFallbackText") != null) ((TextBlock)this.FindName("LyricsFallbackText")).Text = plainLyrics;
                    if (FindName("LyricsFallbackScrollViewer") != null) ((ScrollViewer)this.FindName("LyricsFallbackScrollViewer")).Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (FindName("LyricsFallbackText") != null) ((TextBlock)this.FindName("LyricsFallbackText")).Text = "Lyrics not found for this track.";
                    if (FindName("LyricsFallbackScrollViewer") != null) ((ScrollViewer)this.FindName("LyricsFallbackScrollViewer")).Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                if (FindName("LyricsFallbackText") != null) ((TextBlock)this.FindName("LyricsFallbackText")).Text = "Failed to load lyrics. Please check your connection.";
                if (FindName("LyricsFallbackScrollViewer") != null) ((ScrollViewer)this.FindName("LyricsFallbackScrollViewer")).Visibility = Visibility.Visible;
                LyricsListView.Visibility = Visibility.Collapsed;
            }

            LyricsLoadingBar.Visibility = Visibility.Collapsed;
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

        private void PlayTrack(YouTubeTrack track)
        {
            try { _appMediaPlayer.Pause(); } catch { }

            if (!track.VideoId.StartsWith("LOCAL:") && !IsInternetAvailable()) { ShowToast("No Internet connection"); return; }

            currentTrack = track;
            MiniTitle.Text = track.Title; BigTitle.Text = track.Title;
            MiniArtist.Text = track.ChannelName; BigArtist.Text = track.ChannelName;
            MiniPlayIcon.Symbol = Symbol.Pause; BigPlayIcon.Symbol = Symbol.Pause;

            MenuTitle.Text = track.Title;
            MenuArtist.Text = track.ChannelName;

            var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(track.ThumbnailUrl, UriKind.Absolute));
            bigBmp.DecodePixelWidth = 480;
            BigCoverImage.ImageSource = bigBmp;
            MenuCoverImage.ImageSource = bigBmp;

            var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(track.ThumbnailUrl, UriKind.Absolute));
            miniBmp.DecodePixelWidth = 100;
            MiniCoverImage.ImageSource = miniBmp;

            bool isFav = favoriteTracks.Any(t => t.VideoId == track.VideoId);
            BigHeartBtn.Content = isFav ? "♥" : "♡";
            BigHeartBtn.Foreground = new SolidColorBrush(isFav ? Windows.UI.Colors.Green : Windows.UI.Colors.White);

            var ignored = UpdateLyricsAsync(track.Title, track.ChannelName);

            var existingHistory = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
            if (existingHistory != null) historyTracks.Remove(existingHistory);
            historyTracks.Insert(0, track);
            if (historyTracks.Count > 20) historyTracks.RemoveAt(historyTracks.Count - 1);
            SaveHistoryAsync();
            RefreshHomeHistorySections();

            ObservableCollection<YouTubeTrack> activeList = homeTracks;
            if (searchResults.Contains(track)) activeList = searchResults;
            else if (favoriteTracks.Contains(track)) activeList = favoriteTracks;
            else if (downloadedTracks.Contains(track)) activeList = downloadedTracks;
            else if (historyTracks.Contains(track)) activeList = historyTracks;
            else if (_currentViewingPlaylist != null && _currentViewingPlaylist.Tracks.Contains(track)) activeList = _currentViewingPlaylist.Tracks;

            int count = activeList.Count;
            string[] urls = new string[count];
            string[] titles = new string[count];
            string[] artists = new string[count];
            string[] videoIds = new string[count];
            string[] thumbnails = new string[count];

            for (int i = 0; i < count; i++)
            {
                var t = activeList[i];
                urls[i] = t.VideoId.StartsWith("LOCAL:")
                    ? "ms-appdata:///local/" + t.VideoId.Substring(6)
                    : "https://summer-fire-6e3f.adianhseng.workers.dev/api/play?v=" + t.VideoId + "&key=LumiaWP81-An";

                titles[i] = t.Title;
                artists[i] = t.ChannelName;
                videoIds[i] = t.VideoId;
                thumbnails[i] = t.ThumbnailUrl;
            }

            int startIndex = Math.Max(0, activeList.IndexOf(track));
            string finalAudioUrl = urls[startIndex];

            var message = new ValueSet {
                { "UpdatePlaylist", "" }, { "Urls", urls }, { "Titles", titles }, { "Artists", artists },
                { "VideoIds", videoIds }, { "Thumbnails", thumbnails }, { "StartIndex", startIndex }, { "FastUrl", finalAudioUrl }
            };
            try { BackgroundMediaPlayer.SendMessageToBackground(message); } catch { }
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
            if (NowPlayingPivot.SelectedIndex == 0)
            {
                DotPlayer.Opacity = 1.0;
                DotLyrics.Opacity = 0.3;
            }
            else
            {
                DotPlayer.Opacity = 0.3;
                DotLyrics.Opacity = 1.0;
            }
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

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            DependencyObject parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && !(parent is Grid))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            var grid = parent as Grid;
            if (grid != null)
            {
                var flyout = FlyoutBase.GetAttachedFlyout(grid);
                if (flyout != null)
                {
                    flyout.ShowAt(grid);
                }
            }
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
                    BigHeartBtn.Foreground = new SolidColorBrush(existing == null ? Windows.UI.Colors.Green : Windows.UI.Colors.White);
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
                        SaveHistoryAsync();
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
            if (track == null || track.VideoId.StartsWith("LOCAL:")) return;
            if (!IsInternetAvailable()) { ShowToast("Internet required to download"); return; }

            try
            {
                string downloadUrl = "https://summer-fire-6e3f.adianhseng.workers.dev/api/download?v=" + track.VideoId + "&key=LumiaWP81-An";
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
            if (existing != null) { favoriteTracks.Remove(existing); BigHeartBtn.Content = "♡"; BigHeartBtn.Foreground = new SolidColorBrush(Windows.UI.Colors.White); }
            else { favoriteTracks.Insert(0, currentTrack); BigHeartBtn.Content = "♥"; BigHeartBtn.Foreground = new SolidColorBrush(Windows.UI.Colors.Green); }
            SaveFavoritesAsync();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;
            bool newState = !(settings.Values.ContainsKey("ShuffleMode") ? (bool)settings.Values["ShuffleMode"] : false);
            settings.Values["ShuffleMode"] = newState;
            ShuffleIcon.Foreground = new SolidColorBrush(newState ? Windows.UI.Colors.Green : Windows.UI.Colors.White);
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
            if (mode == 0) { RepeatIcon.Glyph = "\uE1CD"; RepeatIcon.Foreground = new SolidColorBrush(Windows.UI.Colors.White); }
            else if (mode == 1) { RepeatIcon.Glyph = "\uE1CD"; RepeatIcon.Foreground = new SolidColorBrush(Windows.UI.Colors.Green); }
            else if (mode == 2) { RepeatIcon.Glyph = "\uE1CC"; RepeatIcon.Foreground = new SolidColorBrush(Windows.UI.Colors.Green); }
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
                        Symbol sym = (sender.CurrentState == MediaPlayerState.Playing) ? Symbol.Pause : Symbol.Play;
                        MiniPlayIcon.Symbol = sym; BigPlayIcon.Symbol = sym;
                    }
                    catch { }
                });
            }
            catch { }
        }

        private async void TimerCallback(object state)
        {
            if (_isSliderManipulating) return;
            try
            {
                var session = _appMediaPlayer;
                if (session.CurrentState != MediaPlayerState.Playing) return;
                TimeSpan pos = session.Position;
                TimeSpan dur = session.NaturalDuration;
                if (dur.TotalSeconds > 0)
                {
                    try
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            try
                            {
                                MusicSlider.Maximum = dur.TotalSeconds;
                                MusicSlider.Value = pos.TotalSeconds;
                                CurrentTimeText.Text = pos.ToString(@"m\:ss");
                                TotalTimeText.Text = dur.ToString(@"m\:ss");

                                MiniProgressBar.Maximum = dur.TotalSeconds;
                                MiniProgressBar.Value = pos.TotalSeconds;

                                if (NowPlayingView.Visibility == Visibility.Visible && NowPlayingPivot.SelectedIndex == 1)
                                {
                                    if (currentLyrics.Count > 0 && LyricsListView.Visibility == Visibility.Visible)
                                    {
                                        int newIndex = -1;
                                        for (int i = 0; i < currentLyrics.Count; i++)
                                        {
                                            if (pos >= currentLyrics[i].Time.Subtract(TimeSpan.FromSeconds(0.2))) newIndex = i;
                                            else break;
                                        }

                                        if (newIndex != currentLyricIndex && newIndex >= 0)
                                        {
                                            int oldIndex = currentLyricIndex;
                                            currentLyricIndex = newIndex;

                                            if (oldIndex >= 0 && oldIndex < currentLyrics.Count)
                                            {
                                                currentLyrics[oldIndex].ColorBrush = new SolidColorBrush(Windows.UI.Colors.Gray);
                                                currentLyrics[oldIndex].FontSize = _baseLyricSize;
                                                currentLyrics[oldIndex].Opacity = 0.5;
                                                currentLyrics[oldIndex].FontWeight = Windows.UI.Text.FontWeights.Normal;
                                            }

                                            currentLyrics[currentLyricIndex].ColorBrush = new SolidColorBrush(Windows.UI.Colors.White);
                                            currentLyrics[currentLyricIndex].FontSize = _highlightLyricSize;
                                            currentLyrics[currentLyricIndex].Opacity = 1.0;
                                            currentLyrics[currentLyricIndex].FontWeight = Windows.UI.Text.FontWeights.Bold;

                                            LyricsListView.ScrollIntoView(currentLyrics[currentLyricIndex]);
                                            LyricsListView.UpdateLayout();

                                            if (_cachedLyricsScrollViewer == null)
                                            {
                                                _cachedLyricsScrollViewer = GetScrollViewer(LyricsListView);
                                            }

                                            var container = LyricsListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;

                                            if (_cachedLyricsScrollViewer != null && container != null)
                                            {
                                                var transform = container.TransformToVisual(_cachedLyricsScrollViewer);
                                                var position = transform.TransformPoint(new Point(0, 0));

                                                double targetOffset = _cachedLyricsScrollViewer.VerticalOffset + position.Y - (_cachedLyricsScrollViewer.ViewportHeight / 2.0) + (container.ActualHeight / 2.0);

                                                _cachedLyricsScrollViewer.ChangeView(null, targetOffset, null, false);
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }
                }
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
                    _appMediaPlayer.Position = TimeSpan.FromSeconds(Math.Min(MusicSlider.Value, _appMediaPlayer.NaturalDuration.TotalSeconds - 2));
                    if (_appMediaPlayer.CurrentState == MediaPlayerState.Paused) _appMediaPlayer.Play();
                }
            }
            catch { }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_appMediaPlayer.CurrentState == MediaPlayerState.Playing) _appMediaPlayer.Pause();
                else _appMediaPlayer.Play();
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
            try { BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { "NextTrackMessage", "" } }); } catch { }
        }
    }

    public class YouTubeTrack
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string ChannelName { get; set; }
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