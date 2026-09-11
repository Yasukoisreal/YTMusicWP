using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private void RefreshHomeHistorySections()
        {
            if (historyTracks.Count > 0)
            {
                HomeHistorySection.Visibility = Visibility.Visible;
                HomeQuickGrid.ItemsSource = null;
                HomeHistoryCarousel.ItemsSource = null;

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

                HomeQuickGrid.ItemsSource = historyQuickGridTracks;
                HomeHistoryCarousel.ItemsSource = homeHistoryCarouselTracks;

                // Recently Played Artists — extract unique artists from history
                RefreshRecentArtists();
            }
            else
            {
                HomeHistorySection.Visibility = Visibility.Collapsed;
                HomeArtistsSection.Visibility = Visibility.Collapsed;
            }
        }

        private static System.Collections.Generic.List<string> SplitArtistNames(string rawArtists)
        {
            var list = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(rawArtists)) return list;

            string cleaned = CleanChannelName(rawArtists);

            // Delimiters for multiple artists
            string[] delimiters = new string[] { ", ", " & ", " feat. ", " ft. ", " featuring ", " Feat. ", " Ft. ", " Featuring ", " / " };
            string[] parts = cleaned.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                string trimmed = part.Trim().Trim(',', '&', '/', ';', ' ');
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(trimmed);
                }
            }

            if (list.Count == 0 && !string.IsNullOrWhiteSpace(cleaned))
            {
                list.Add(cleaned.Trim());
            }

            return list;
        }

        // [OPT-AV] Debounce avatar fetch requests
        private DateTime _lastArtistFetchTime = DateTime.MinValue;

        private async void RefreshRecentArtists()
        {
            try
            {
                var seenArtists = new System.Collections.Generic.HashSet<string>();
                var artistItems = new System.Collections.Generic.List<YouTubeTrack>();

                // [OPT-AV] Load cached avatars from LocalSettings
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;

                foreach (var track in historyTracks)
                {
                    if (string.IsNullOrEmpty(track.ChannelName) || track.ChannelName == "Unknown") continue;

                    var names = SplitArtistNames(track.ChannelName);
                    for (int n = 0; n < names.Count; n++)
                    {
                        string artistName = names[n];
                        string key = artistName.ToLowerInvariant();
                        if (seenArtists.Contains(key)) continue;
                        seenArtists.Add(key);

                        // Check avatar cache first
                        string cacheKey = "AvatarCache_" + key;
                        string cachedAvatar = localSettings.ContainsKey(cacheKey) ? localSettings[cacheKey] as string : null;
                        string cachedChannelKey = "AvatarChId_" + key;
                        string cachedChannelId = localSettings.ContainsKey(cachedChannelKey) ? localSettings[cachedChannelKey] as string : null;

                        // Only assign track.ChannelId if this was the sole artist or if cachedChannelId exists
                        string chId = !string.IsNullOrEmpty(cachedChannelId) ? cachedChannelId
                                    : (names.Count == 1 ? track.ChannelId : "");

                        artistItems.Add(new YouTubeTrack
                        {
                            VideoId = !string.IsNullOrEmpty(chId) ? "CHANNEL:" + chId : "",
                            Title = artistName,
                            ChannelName = artistName,
                            ChannelId = chId ?? "",
                            ThumbnailUrl = !string.IsNullOrEmpty(cachedAvatar) ? cachedAvatar : GetSquareThumbnail(track.ThumbnailUrl)
                        });

                        if (artistItems.Count >= 10) break;
                    }

                    if (artistItems.Count >= 10) break;
                }

                if (artistItems.Count >= 2)
                {
                    HomeArtistsSection.Visibility = Visibility.Visible;
                    HomeArtistsCarousel.ItemsSource = artistItems;

                    // Only fetch avatars for artists that DON'T have a cached avatar
                    var uncachedArtists = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < artistItems.Count; i++)
                    {
                        string cacheKey = "AvatarCache_" + artistItems[i].Title.ToLowerInvariant();
                        if (!localSettings.ContainsKey(cacheKey))
                            uncachedArtists.Add(i);
                    }

                    if (uncachedArtists.Count == 0) return;
                    if ((DateTime.Now - _lastArtistFetchTime).TotalSeconds < 15) return;
                    _lastArtistFetchTime = DateTime.Now;

                    // Fetch only uncached avatars (batched 3 at a time)
                    for (int i = 0; i < uncachedArtists.Count; i += 3)
                    {
                        var batch = new System.Collections.Generic.List<Task>(3);
                        for (int j = i; j < Math.Min(i + 3, uncachedArtists.Count); j++)
                        {
                            int idx = uncachedArtists[j];
                            var artist = artistItems[idx];
                            batch.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    var searchResults = await InnerTubeClient.SearchAsync(artist.Title, 5);
                                    var artistMatch = searchResults.FirstOrDefault(r =>
                                        r.VideoId != null && r.VideoId.StartsWith("CHANNEL:") &&
                                        r.Title.Equals(artist.Title, StringComparison.OrdinalIgnoreCase));

                                    if (artistMatch != null)
                                    {
                                        string ytmChannelId = artistMatch.VideoId.Replace("CHANNEL:", "");
                                        string avatarUrl = GetArtistAvatar(artistMatch.ThumbnailUrl);
                                        if (!string.IsNullOrEmpty(avatarUrl))
                                        {
                                            // Save to cache
                                            string ck = "AvatarCache_" + artist.Title.ToLowerInvariant();
                                            string ckId = "AvatarChId_" + artist.Title.ToLowerInvariant();
                                            localSettings[ck] = avatarUrl;
                                            localSettings[ckId] = ytmChannelId;

                                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                                            {
                                                artistItems[idx].ThumbnailUrl = avatarUrl;
                                                artistItems[idx].ChannelId = ytmChannelId;
                                                artistItems[idx].VideoId = "CHANNEL:" + ytmChannelId;
                                            });
                                        }
                                    }
                                    else
                                    {
                                        var searchResults2 = await InnerTubeClient.SearchAsync(artist.Title + " artist", 3);
                                        var fallbackMatch = searchResults2.FirstOrDefault(r =>
                                            r.VideoId != null && r.VideoId.StartsWith("CHANNEL:"));
                                        if (fallbackMatch != null && !string.IsNullOrEmpty(fallbackMatch.ThumbnailUrl))
                                        {
                                            string fallbackAvatar = GetArtistAvatar(fallbackMatch.ThumbnailUrl);
                                            string ytmChannelId = fallbackMatch.VideoId.Replace("CHANNEL:", "");
                                            string ck = "AvatarCache_" + artist.Title.ToLowerInvariant();
                                            string ckId = "AvatarChId_" + artist.Title.ToLowerInvariant();
                                            localSettings[ck] = fallbackAvatar;
                                            if (!string.IsNullOrEmpty(ytmChannelId)) localSettings[ckId] = ytmChannelId;

                                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                                            {
                                                artistItems[idx].ThumbnailUrl = fallbackAvatar;
                                                if (!string.IsNullOrEmpty(ytmChannelId))
                                                {
                                                    artistItems[idx].ChannelId = ytmChannelId;
                                                    artistItems[idx].VideoId = "CHANNEL:" + ytmChannelId;
                                                }
                                            });
                                        }
                                    }
                                }
                                catch { }
                            }));
                        }
                        foreach (var searchTask in batch)
                        {
                            await searchTask;
                            await Task.Delay(500); // 500ms delay between each artist search to avoid API spam
                        }
                        
                        await Task.Delay(1000); // 1s rest between batches
                    }
                }
                else
                {
                    HomeArtistsSection.Visibility = Visibility.Collapsed;
                }
            }
            catch { HomeArtistsSection.Visibility = Visibility.Collapsed; }
        }

        private void RecentArtist_ItemClick(object sender, ItemClickEventArgs e)
        {
            var track = e.ClickedItem as YouTubeTrack;
            if (track == null) return;
            // trustChannelId=true — channelId was already resolved by avatar fetch
            OpenArtistProfile(track.ChannelId, track.Title, true);
        }

        private async Task LoadHomeRecommendations()
        {
            HomeLoading.Visibility = Visibility.Visible;

            // ═══════════════════════════════════════════════════
            // PRIMARY: YouTube Music Home (FE_music_home) + Charts in parallel
            // ═══════════════════════════════════════════════════
            try
            {
                var dynamicSections = new ObservableCollection<YTMusicWP.InnerTubeClient.HomeSection>();
                bool firstPage = true;

                Action<System.Collections.Generic.List<YTMusicWP.InnerTubeClient.HomeSection>> onPageLoaded = (sections) =>
                {
                    var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        if (sections != null)
                        {
                            if (firstPage)
                            {
                                HomeDynamicSections.ItemsSource = dynamicSections;
                                firstPage = false;
                            }
                            
                            // Add only new sections
                            for (int i = dynamicSections.Count; i < sections.Count; i++)
                            {
                                dynamicSections.Add(sections[i]);
                            }
                            
                            HomeLoading.Visibility = Visibility.Collapsed;
                        }
                    });
                };

                var homeTask = InnerTubeClient.BrowseHomeAsync(null, onPageLoaded);
                var chartsTask = InnerTubeClient.BrowseChartsAsync();
                var moodsTask = InnerTubeClient.BrowseMoodsAndGenresAsync();
                
                var chartsData = default(System.Collections.Generic.List<DiscoverItem>);
                var moodsData = default(System.Collections.Generic.List<YTMusicWP.MoodCategory>);

                // Wait for the full fetch to complete
                var homeSections = default(System.Collections.Generic.List<YTMusicWP.InnerTubeClient.HomeSection>);
                try { homeSections = await homeTask; } catch { }
                try { chartsData = await chartsTask; } catch { }
                try { moodsData = await moodsTask; } catch { }

                // Moods
                if (moodsData != null && moodsData.Count > 0)
                {
                    MoodsGenresListView.Visibility = Visibility.Visible;
                    MoodsGenresListView.ItemsSource = moodsData;
                }
                else
                {
                    MoodsGenresListView.Visibility = Visibility.Collapsed;
                }

                // Charts
                if (chartsData != null && chartsData.Count > 0)
                {
                    HomeChartsTitle.Visibility = Visibility.Visible;
                    HomeChartsCarousel.Visibility = Visibility.Visible;
                    HomeChartsCarousel.ItemsSource = chartsData;
                }

                // Dynamic home sections final pass
                if (homeSections != null && homeSections.Count > 0)
                {
                    _currentHomeQuery = homeSections[0].Title;
                    var topTracks = homeSections.SelectMany(s => s.Tracks).Where(t => IsMusicTrack(t)).Take(5).ToList();
                    YTMusicWP.Services.TileService.UpdateRecommendations(topTracks, favoriteTracks, historyTracks);

                    HomeLoading.Visibility = Visibility.Collapsed;
                    return;
                }
            }
            catch { }

            // ═══════════════════════════════════════════════════
            // FALLBACK: Search-based recommendations (if BrowseHome fails)
            // ═══════════════════════════════════════════════════
            string region = InnerTubeClient.CurrentRegion;
            string year = DateTime.Now.Year.ToString();

            string[] queries;
            string[] fallbackTitles;

            switch (region)
            {
                case "VN":
                    queries = new[] {
                        "nhạc Việt hot " + year,
                        "nhạc trẻ hay nhất " + year,
                        "bolero trữ tình chọn lọc",
                        "rap Việt " + year
                    };
                    fallbackTitles = new[] { "Made for you", "Nhạc trẻ", "Bolero - Trữ tình", "Rap Việt" };
                    break;
                case "KR":
                    queries = new[] {
                        "K-pop trending " + year,
                        "K-pop girl group hits",
                        "K-drama OST " + year,
                        "K-pop boy group hits"
                    };
                    fallbackTitles = new[] { "Made for you", "Girl Group Hits", "K-Drama OST", "Boy Group Hits" };
                    break;
                case "JP":
                    queries = new[] {
                        "J-pop trending " + year,
                        "Anime OST " + year,
                        "J-pop chill vibes",
                        "J-rock hits"
                    };
                    fallbackTitles = new[] { "Made for you", "Anime OST", "Chill vibes", "J-Rock" };
                    break;
                default:
                    queries = new[] {
                        "top hits " + year,
                        "pop hits " + year,
                        "lofi chill beats relax",
                        "workout gym motivation music"
                    };
                    fallbackTitles = new[] { "Made for you", "Pop Hits", "Chill vibes", "Workout Motivation" };
                    break;
            }

            _currentHomeQuery = queries[0];

            var fallbackSections = new System.Collections.Generic.List<InnerTubeClient.HomeSection>();
            for (int i = 0; i < queries.Length; i++)
            {
                var results = await FetchMusicList(queries[i], "", "songs");
                if (results != null)
                {
                    var sec = new InnerTubeClient.HomeSection { Title = fallbackTitles[i] };
                    foreach (var t in results) { if (IsMusicTrack(t)) sec.Tracks.Add(t); }
                    if (sec.Tracks.Count > 0) fallbackSections.Add(sec);
                }
            }
            HomeDynamicSections.ItemsSource = fallbackSections;
            if (fallbackSections.Count > 0)
            {
                var topTracks2 = fallbackSections.SelectMany(s => s.Tracks).Where(t => IsMusicTrack(t)).Take(5).ToList();
                YTMusicWP.Services.TileService.UpdateRecommendations(topTracks2, favoriteTracks, historyTracks);
            }
            HomeLoading.Visibility = Visibility.Collapsed;
        }

        private static bool IsMusicTrack(YouTubeTrack t)
        {
            if (t.VideoId == null || t.VideoId.StartsWith("CHANNEL:") || t.VideoId.StartsWith("PLAYLIST:")) return false;
            string ch = (t.ChannelName ?? "").ToLowerInvariant();
            if (ch == "episode" || ch == "podcast" || ch == "audiobook" || ch == "short stories") return false;
            string title = (t.Title ?? "").ToLowerInvariant();
            if (title.Contains("(storyteller)") || title.Contains("full audiobook") || title.Contains("full audio book")) return false;
            return true;
        }

        // ==========================================
        // HOME CHIP FILTERS
        // ==========================================
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _chipInactiveBrush =
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _chipActiveBrush =
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 185, 84));

        private void SetHomeChipActive(Border active)
        {
            HomeChipAll.Background = _chipInactiveBrush;
            HomeChipMusic.Background = _chipInactiveBrush;
            HomeChipPodcasts.Background = _chipInactiveBrush;
            HomeChipAudiobooks.Background = _chipInactiveBrush;
            active.Background = _chipActiveBrush;
        }

        private void ShowHomePanel(string panel)
        {
            HomeMusicPanel.Visibility = panel == "music" ? Visibility.Visible : Visibility.Collapsed;
            HomePodcastPanel.Visibility = panel == "podcasts" ? Visibility.Visible : Visibility.Collapsed;
            HomeAudiobookPanel.Visibility = panel == "audiobooks" ? Visibility.Visible : Visibility.Collapsed;
            if (panel == "music")
            {
                EnsureHomePullTimer();
            }
        }

        private void HomeChipAll_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SetHomeChipActive(HomeChipAll);
            ShowHomePanel("music");
        }

        private void HomeChipMusic_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SetHomeChipActive(HomeChipMusic);
            ShowHomePanel("music");
        }

        private void HomeChipPodcasts_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SetHomeChipActive(HomeChipPodcasts);
            ShowHomePanel("podcasts");
            if (podcastTracks.Count == 0)
            {
                var ignored = LoadPodcasts();
            }
        }

        private void HomeChipAudiobooks_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SetHomeChipActive(HomeChipAudiobooks);
            ShowHomePanel("audiobooks");
            if (audiobookTracks.Count == 0)
            {
                var ignored = LoadAudiobooks();
            }
        }

        private async Task LoadPodcasts()
        {
            PodcastLoading.Visibility = Visibility.Visible;
            try
            {
                string region = InnerTubeClient.CurrentRegion;

                string query = "popular podcasts";
                switch (region)
                {
                    case "VN": query = "podcast tiếng Việt hay nhất"; break;
                    case "KR": query = "인기 팟캐스트 한국"; break;
                    case "JP": query = "人気ポッドキャスト 日本"; break;
                    case "GB": query = "top podcasts UK"; break;
                }

                var results = await InnerTubeClient.SearchAsync(query, 30);
                podcastTracks.Clear();
                if (results != null)
                {
                    foreach (var t in results)
                    {
                        if (t.VideoId != null && !t.VideoId.StartsWith("CHANNEL:"))
                            podcastTracks.Add(t);
                    }
                }
                PodcastList.ItemsSource = podcastTracks;
            }
            catch { }
            PodcastLoading.Visibility = Visibility.Collapsed;
        }

        private async Task LoadAudiobooks()
        {
            AudiobookLoading.Visibility = Visibility.Visible;
            try
            {
                string region = InnerTubeClient.CurrentRegion;

                string query = "audiobook full length";
                switch (region)
                {
                    case "VN": query = "sách nói tiếng Việt full"; break;
                    case "KR": query = "오디오북 한국어"; break;
                    case "JP": query = "オーディオブック 日本語"; break;
                    case "GB": query = "audiobook full length english"; break;
                }

                var results = await InnerTubeClient.SearchAsync(query, 30);
                audiobookTracks.Clear();
                if (results != null)
                {
                    foreach (var t in results)
                    {
                        if (t.VideoId != null && !t.VideoId.StartsWith("CHANNEL:"))
                            audiobookTracks.Add(t);
                    }
                }
                AudiobookList.ItemsSource = audiobookTracks;
            }
            catch { }
            AudiobookLoading.Visibility = Visibility.Collapsed;
        }

        #region Pull to Refresh
        private DispatcherTimer _pullTimer;
        private bool _isPullReady = false;
        private bool _isRefreshingHome = false;
        private double _pullRestingY = -1;
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _pullMutedBrush =
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));

        private void InitializeHomePullToRefresh()
        {
            if (HomeMusicPanel == null) return;

            HomeMusicPanel.ViewChanging += HomeMusicPanel_ViewChanging;
            HomeMusicPanel.ViewChanged += HomeMusicPanel_ViewChanged;

            _pullTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _pullTimer.Tick += PullTimer_Tick;
            _pullTimer.Start();
        }

        internal void EnsureHomePullTimer()
        {
            if (HomeMusicPanel != null && HomeMusicPanel.VerticalOffset == 0 && HomeMusicPanel.Visibility == Visibility.Visible)
            {
                if (_pullTimer != null && !_pullTimer.IsEnabled)
                {
                    _pullTimer.Start();
                }
            }
        }

        private void HomeMusicPanel_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            if (e.NextView.VerticalOffset == 0)
            {
                if (_pullTimer != null && !_pullTimer.IsEnabled)
                {
                    _pullTimer.Start();
                }
            }
            else
            {
                if (_pullTimer != null && _pullTimer.IsEnabled)
                {
                    _pullTimer.Stop();
                }
                if (!_isRefreshingHome)
                {
                    _isPullReady = false;
                    if (HomePullIndicator != null)
                        HomePullIndicator.Opacity = 0;
                }
            }
        }

        private void PullTimer_Tick(object sender, object e)
        {
            if (HomePullIndicator == null || HomePanel == null || HomeMusicPanel == null) return;
            if (HomePanel.Visibility != Visibility.Visible || HomeMusicPanel.Visibility != Visibility.Visible) return;

            // Stop timer and reset state if user scrolled down into feed
            if (HomeMusicPanel.VerticalOffset > 0)
            {
                if (_pullTimer != null && _pullTimer.IsEnabled)
                    _pullTimer.Stop();
                if (HomePullIndicator.Opacity > 0)
                    HomePullIndicator.Opacity = 0;
                _isPullReady = false;
                return;
            }

            try
            {
                // Wait until element has valid layout
                if (HomePullIndicator.ActualHeight <= 0) return;

                var transform = HomePullIndicator.TransformToVisual(HomePanel);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                // Initialize resting baseline when at rest at offset 0
                if (_pullRestingY < 0 && point.Y > 0)
                {
                    _pullRestingY = point.Y;
                }

                double baseline = _pullRestingY > 0 ? _pullRestingY : 55.0;
                double pullDistance = point.Y - baseline;

                if (pullDistance <= 0)
                {
                    if (_isPullReady && !_isRefreshingHome)
                    {
                        // Released after pulling past threshold -> trigger refresh!
                        _isPullReady = false;
                        var ignored = RefreshHomeFeedAsync();
                    }
                    else if (!_isRefreshingHome && HomePullIndicator.Opacity > 0)
                    {
                        HomePullIndicator.Opacity = 0;
                        HomePullArrowRotate.Angle = 0;
                        HomePullArrowPath.Fill = _pullMutedBrush;
                        HomePullText.Text = "Pull to refresh";
                        HomePullText.Foreground = _pullMutedBrush;
                    }
                    return;
                }

                if (_isRefreshingHome)
                {
                    // Already refreshing: keep spinner and updating text visible if pulled
                    HomePullIndicator.Opacity = Math.Min(1.0, pullDistance / 25.0);
                    return;
                }

                // Smoothly fade in indicator as user pulls
                HomePullIndicator.Opacity = Math.Min(1.0, pullDistance / 25.0);

                if (pullDistance >= 40)
                {
                    // Threshold passed: Ready to refresh
                    _isPullReady = true;
                    HomePullArrowRotate.Angle = 180;
                    HomePullArrowPath.Fill = _chipActiveBrush;
                    HomePullText.Text = "Release to refresh";
                    HomePullText.Foreground = _chipActiveBrush;
                }
                else
                {
                    // Below threshold: dragging down or returning before release
                    _isPullReady = false;
                    HomePullArrowRotate.Angle = 0;
                    HomePullArrowPath.Fill = _pullMutedBrush;
                    HomePullText.Text = "Pull to refresh";
                    HomePullText.Foreground = _pullMutedBrush;
                }
            }
            catch
            {
                // Visual tree transition safety
            }
        }

        private void HomeMusicPanel_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (HomeMusicPanel != null && HomeMusicPanel.VerticalOffset == 0)
            {
                if (_pullTimer != null && !_pullTimer.IsEnabled)
                {
                    _pullTimer.Start();
                }
            }

            if (!e.IsIntermediate && _isPullReady && !_isRefreshingHome)
            {
                _isPullReady = false;
                var ignored = RefreshHomeFeedAsync();
            }
        }

        private async Task RefreshHomeFeedAsync()
        {
            if (_isRefreshingHome) return;
            _isRefreshingHome = true;

            try
            {
                // 1. Show UI progress indicators
                if (HomeLoading != null)
                    HomeLoading.Visibility = Visibility.Visible;

                if (HomePullSpinner != null)
                {
                    HomePullSpinner.Visibility = Visibility.Visible;
                    HomePullSpinner.IsActive = true;
                }
                if (HomePullArrowBox != null)
                    HomePullArrowBox.Visibility = Visibility.Collapsed;
                if (HomePullText != null)
                {
                    HomePullText.Text = "Updating...";
                    HomePullText.Foreground = _chipActiveBrush;
                }

                // 2. Clear Home caches to force fresh recommendations from YouTube
                InnerTubeClient.ClearHomeCache();

                // 3. Fetch fresh data concurrently
                var loadRecsTask = LoadHomeRecommendations();
                RefreshHomeHistorySections();
                await loadRecsTask;
            }
            catch { }
            finally
            {
                // 3. Reset UI states smoothly
                if (HomeLoading != null)
                    HomeLoading.Visibility = Visibility.Collapsed;

                if (HomePullSpinner != null)
                {
                    HomePullSpinner.IsActive = false;
                    HomePullSpinner.Visibility = Visibility.Collapsed;
                }
                if (HomePullArrowBox != null)
                    HomePullArrowBox.Visibility = Visibility.Visible;
                if (HomePullArrowRotate != null)
                    HomePullArrowRotate.Angle = 0;
                if (HomePullArrowPath != null)
                    HomePullArrowPath.Fill = _pullMutedBrush;
                if (HomePullText != null)
                {
                    HomePullText.Text = "Pull to refresh";
                    HomePullText.Foreground = _pullMutedBrush;
                }
                if (HomePullIndicator != null)
                    HomePullIndicator.Opacity = 0;

                _isPullReady = false;
                _isRefreshingHome = false;
            }
        }
        #endregion
    }
}
