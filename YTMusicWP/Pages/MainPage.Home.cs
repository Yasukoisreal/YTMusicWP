using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private ObservableCollection<YTMusicWP.InnerTubeClient.HomeSection> _homeDynamicSections;
        private string _homeContinuationToken;
        private bool _isLoadingMoreHomeSections;
        private bool _hasMoreHomeSections;
        private int _homeLoadedPagesCount;
        private bool _isLoadingHomeEndSections;
        private Border _activeHomeChipBorder;
        private string _currentHomeFilterParams;
        private string _currentFilterChipTitle;

        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _ytmChipInactiveBgBrush =
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 38));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _ytmChipInactiveFgBrush =
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _ytmChipActiveBgBrush =
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _ytmChipActiveFgBrush =
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 15, 15));

        private void RefreshHomeHistorySections()
        {
            if (historyTracks.Count > 0)
            {
                HomeHistorySection.Visibility = Visibility.Visible;
                HomeQuickGrid.ItemsSource = null;
                HomeQuickGrid.Visibility = Visibility.Collapsed;
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
        private CancellationTokenSource _refreshArtistsCts = null;

        private async void RefreshRecentArtists()
        {
            try
            {
                if (_refreshArtistsCts != null)
                {
                    try { _refreshArtistsCts.Cancel(); _refreshArtistsCts.Dispose(); } catch { }
                }
                _refreshArtistsCts = new CancellationTokenSource();
                var token = _refreshArtistsCts.Token;

                var seenArtists = new System.Collections.Generic.HashSet<string>();
                var artistItems = new System.Collections.Generic.List<YouTubeTrack>();

                // [OPT-AV] Load cached avatars from LocalSettings
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;

                // [MIGRATION] Purge any poisoned artist channel IDs/avatars from earlier builds
                int cacheVer = SafeGetInt(localSettings, "AvatarCacheVer", 0);
                if (cacheVer < 2)
                {
                    var keysToRemove = localSettings.Keys
                        .Where(k => k.StartsWith("AvatarChId_") || k.StartsWith("AvatarCache_"))
                        .ToList();
                    foreach (var k in keysToRemove)
                    {
                        localSettings.Remove(k);
                    }
                    localSettings["AvatarCacheVer"] = 2;
                }

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
                    if (YTMusicWP.Services.MemoryHelper.IsLowMemoryDevice) return;
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
                                    if (token.IsCancellationRequested) return;
                                    var artistTrack = await InnerTubeClient.FindArtistAsync(artist.Title);
                                    if (token.IsCancellationRequested || artistTrack == null) return;

                                    string ytmChannelId = artistTrack.ChannelId ?? (artistTrack.VideoId != null ? artistTrack.VideoId.Replace("CHANNEL:", "") : "");
                                    string avatarUrl = GetArtistAvatar(artistTrack.ThumbnailUrl);
                                    if (!string.IsNullOrEmpty(avatarUrl) && !string.IsNullOrEmpty(ytmChannelId))
                                    {
                                        // Save verified artist channelId & avatar to cache
                                        string ck = "AvatarCache_" + artist.Title.ToLowerInvariant();
                                        string ckId = "AvatarChId_" + artist.Title.ToLowerInvariant();
                                        localSettings[ck] = avatarUrl;
                                        localSettings[ckId] = ytmChannelId;

                                        if (token.IsCancellationRequested) return;
                                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                                        {
                                            artistItems[idx].ThumbnailUrl = avatarUrl;
                                            artistItems[idx].ChannelId = ytmChannelId;
                                            artistItems[idx].VideoId = "CHANNEL:" + ytmChannelId;
                                        });
                                    }
                                }
                                catch { }
                            }));
                        }
                        foreach (var searchTask in batch)
                        {
                            if (token.IsCancellationRequested) return;
                            await searchTask;
                            if (token.IsCancellationRequested) return;
                            await Task.Delay(500); // 500ms delay between each artist search to avoid API spam
                        }
                        
                        if (token.IsCancellationRequested) return;
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

        private async Task LoadHomeRecommendations(string filterParams = null)
        {
            if (!_isRefreshingHome && HomeLoading != null)
                HomeLoading.Visibility = Visibility.Visible;

            // Reset pagination
            _homeContinuationToken = null;
            _hasMoreHomeSections = false;
            _homeLoadedPagesCount = 0;
            _isLoadingMoreHomeSections = false;

            // When a filter is selected, hide personal history and artists shelves since filter feeds only contain mood/activity content
            if (!string.IsNullOrEmpty(filterParams))
            {
                if (HomeHistorySection != null) HomeHistorySection.Visibility = Visibility.Collapsed;
                if (HomeArtistsSection != null) HomeArtistsSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                RefreshHomeHistorySections();
            }

            // ═══════════════════════════════════════════════════
            // PRIMARY: YouTube Music Home (FE_music_home) + filterParams
            // ═══════════════════════════════════════════════════
            try
            {
                HomeDynamicSections.ItemsSource = null;
                if (_homeDynamicSections == null)
                {
                    _homeDynamicSections = new ObservableCollection<YTMusicWP.InnerTubeClient.HomeSection>();
                }
                else
                {
                    _homeDynamicSections.Clear();
                }

                var homeFirstPageTask = InnerTubeClient.BrowseHomeFirstPageAsync(filterParams);
                var homeResult = default(InnerTubeClient.HomeBrowseResult);

                try { homeResult = await homeFirstPageTask; } catch { }

                // Moods & Charts are kept Collapsed until all home sections/continuation pages have loaded
                if (MoodsGenresListView != null)
                {
                    MoodsGenresListView.Visibility = Visibility.Collapsed;
                    MoodsGenresListView.ItemsSource = null;
                }
                if (HomeChartsTitle != null)
                    HomeChartsTitle.Visibility = Visibility.Collapsed;
                if (HomeChartsCarousel != null)
                {
                    HomeChartsCarousel.Visibility = Visibility.Collapsed;
                    HomeChartsCarousel.ItemsSource = null;
                }

                // Update chips with localized/returned chip cloud if present
                if (homeResult != null && homeResult.Chips != null && homeResult.Chips.Count > 0)
                {
                    UpdateHomeChips(homeResult.Chips);
                }

                // Dynamic home sections (Page 1)
                if (homeResult != null && homeResult.Sections != null && homeResult.Sections.Count > 0)
                {
                    var secList = new System.Collections.Generic.List<YTMusicWP.InnerTubeClient.HomeSection>(homeResult.Sections);
                    EnrichHomeSections(secList, !string.IsNullOrEmpty(filterParams));

                    foreach (var sec in secList)
                    {
                        _homeDynamicSections.Add(sec);
                    }
                    HomeDynamicSections.ItemsSource = _homeDynamicSections;

                    _homeContinuationToken = homeResult.ContinuationToken;
                    _hasMoreHomeSections = !string.IsNullOrEmpty(_homeContinuationToken);
                    _homeLoadedPagesCount = 1;

                    _currentHomeQuery = homeResult.Sections[0].Title;
                    var topTracks = secList.SelectMany(s => s.Tracks).Where(t => IsMusicTrack(t)).Take(5).ToList();
                    YTMusicWP.Services.TileService.UpdateRecommendations(topTracks, favoriteTracks, historyTracks);

                    if (!_hasMoreHomeSections)
                    {
                        ShowHomeEndSections();
                    }

                    HomeLoading.Visibility = Visibility.Collapsed;
                    return;
                }
                else
                {
                    HomeDynamicSections.ItemsSource = _homeDynamicSections;
                }
            }
            catch
            {
                if (HomeDynamicSections != null && HomeDynamicSections.ItemsSource == null)
                {
                    HomeDynamicSections.ItemsSource = _homeDynamicSections;
                }
            }

            // ═══════════════════════════════════════════════════
            // FALLBACK: Search-based recommendations (if BrowseHome fails)
            // ═══════════════════════════════════════════════════
            string region = InnerTubeClient.CurrentRegion;
            string year = DateTime.Now.Year.ToString();

            string[] queries;
            string[] fallbackTitles;

            if (!string.IsNullOrEmpty(filterParams) && !string.IsNullOrEmpty(_currentFilterChipTitle))
            {
                queries = new[] {
                    _currentFilterChipTitle + " music " + year,
                    _currentFilterChipTitle + " hits",
                    _currentFilterChipTitle + " songs"
                };
                fallbackTitles = new[] { _currentFilterChipTitle, "Popular " + _currentFilterChipTitle, "Recommended" };
            }
            else
            {
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
            }

            _currentHomeQuery = queries[0];

            var fallbackSections = new System.Collections.Generic.List<InnerTubeClient.HomeSection>();
            for (int i = 0; i < queries.Length; i++)
            {
                var results = await FetchMusicList(queries[i], "", "songs");
                if (results != null)
                {
                    var sec = new InnerTubeClient.HomeSection { Title = fallbackTitles[i] };
                    if (i == 0 || i == 1)
                    {
                        sec.Layout = InnerTubeClient.HomeSectionLayout.MultiTrackColumn;
                    }
                    else if (i == 2)
                    {
                        sec.Layout = InnerTubeClient.HomeSectionLayout.LandscapeVideo;
                        foreach (var t in results) { t.CoverWidth = 260; }
                    }
                    foreach (var t in results) { if (IsMusicTrack(t)) sec.Tracks.Add(t); }
                    if (sec.Tracks.Count > 0)
                    {
                        if (sec.Layout == InnerTubeClient.HomeSectionLayout.MultiTrackColumn)
                            sec.PopulateColumns(4);
                        fallbackSections.Add(sec);
                    }
                }
            }
            EnrichHomeSections(fallbackSections, !string.IsNullOrEmpty(filterParams));
            HomeDynamicSections.ItemsSource = fallbackSections;
            if (fallbackSections.Count > 0)
            {
                var topTracks2 = fallbackSections.SelectMany(s => s.Tracks).Where(t => IsMusicTrack(t)).Take(5).ToList();
                YTMusicWP.Services.TileService.UpdateRecommendations(topTracks2, favoriteTracks, historyTracks);
            }
            _hasMoreHomeSections = false;
            ShowHomeEndSections();
            HomeLoading.Visibility = Visibility.Collapsed;
        }

        private async Task LoadNextHomeContinuationAsync()
        {
            if (_isLoadingMoreHomeSections || !_hasMoreHomeSections || string.IsNullOrEmpty(_homeContinuationToken))
                return;

            // Strict 512MB RAM cap: max 4 pages of home sections to prevent unbounded memory growth
            if (YTMusicWP.Services.MemoryHelper.IsLowMemoryDevice && _homeLoadedPagesCount >= 4)
            {
                _hasMoreHomeSections = false;
                ShowHomeEndSections();
                return;
            }

            _isLoadingMoreHomeSections = true;

            try
            {
                var nextResult = await InnerTubeClient.BrowseHomeContinuationAsync(_homeContinuationToken);
                if (nextResult != null && nextResult.Sections != null && nextResult.Sections.Count > 0)
                {
                    EnrichHomeSections(nextResult.Sections, true);
                    foreach (var sec in nextResult.Sections)
                    {
                        if (_homeDynamicSections != null)
                        {
                            _homeDynamicSections.Add(sec);
                        }
                    }
                    _homeContinuationToken = nextResult.ContinuationToken;
                    _hasMoreHomeSections = !string.IsNullOrEmpty(_homeContinuationToken);
                    _homeLoadedPagesCount++;

                    if (YTMusicWP.Services.MemoryHelper.IsLowMemoryDevice && _homeLoadedPagesCount >= 4)
                    {
                        _hasMoreHomeSections = false;
                    }
                }
                else
                {
                    _hasMoreHomeSections = false;
                }
            }
            catch
            {
                // Transient network errors should not crash or permanently disable
            }
            finally
            {
                _isLoadingMoreHomeSections = false;
                if (!_hasMoreHomeSections)
                {
                    ShowHomeEndSections();
                }
            }
        }

        private async void ShowHomeEndSections()
        {
            if (_isLoadingHomeEndSections) return;
            _isLoadingHomeEndSections = true;

            try
            {
                var moodsTask = InnerTubeClient.BrowseMoodsAndGenresAsync();
                var chartsTask = InnerTubeClient.BrowseChartsAsync();

                var moodsData = default(System.Collections.Generic.List<YTMusicWP.MoodCategory>);
                var chartsData = default(System.Collections.Generic.List<DiscoverItem>);

                try { moodsData = await moodsTask; } catch { }
                try { chartsData = await chartsTask; } catch { }

                if (moodsData != null && moodsData.Count > 0 && MoodsGenresListView != null)
                {
                    MoodsGenresListView.ItemsSource = moodsData;
                    MoodsGenresListView.Visibility = Visibility.Visible;
                }

                if (chartsData != null && chartsData.Count > 0)
                {
                    if (HomeChartsTitle != null)
                        HomeChartsTitle.Visibility = Visibility.Visible;
                    if (HomeChartsCarousel != null)
                    {
                        HomeChartsCarousel.ItemsSource = chartsData;
                        HomeChartsCarousel.Visibility = Visibility.Visible;
                    }
                }
            }
            catch { }
            finally
            {
                _isLoadingHomeEndSections = false;
            }
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

        private void SetChipVisualState(Border border, bool isSelected)
        {
            if (border == null) return;
            border.Background = isSelected ? _ytmChipActiveBgBrush : _ytmChipInactiveBgBrush;
            var tb = border.Child as TextBlock;
            if (tb != null)
            {
                tb.Foreground = isSelected ? _ytmChipActiveFgBrush : _ytmChipInactiveFgBrush;
            }
        }

        private void HomeChip_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            var chipParams = border.Tag as string;
            var tb = border.Child as TextBlock;
            string chipTitle = tb != null ? tb.Text : "";

            if (_activeHomeChipBorder == border)
            {
                // Toggle OFF (deselect active chip)
                SetChipVisualState(border, false);
                _activeHomeChipBorder = null;
                _currentHomeFilterParams = null;
                _currentFilterChipTitle = null;
                if (HomeMusicPanel != null)
                    HomeMusicPanel.ChangeView(0, 0, 1.0f, true);
                var ignored = LoadHomeRecommendations(null);
            }
            else
            {
                // Select new chip
                if (_activeHomeChipBorder != null)
                {
                    SetChipVisualState(_activeHomeChipBorder, false);
                }
                _activeHomeChipBorder = border;
                _currentHomeFilterParams = chipParams;
                _currentFilterChipTitle = chipTitle;
                SetChipVisualState(border, true);
                if (HomeMusicPanel != null)
                    HomeMusicPanel.ChangeView(0, 0, 1.0f, true);
                var ignored = LoadHomeRecommendations(_currentHomeFilterParams);
            }
        }

        private void UpdateHomeChips(System.Collections.Generic.List<InnerTubeClient.HomeChipItem> chips)
        {
            if (chips == null || chips.Count == 0 || HomeChipsPanel == null) return;

            if (HomeChipsPanel.Children.Count == chips.Count)
            {
                for (int i = 0; i < chips.Count; i++)
                {
                    var border = HomeChipsPanel.Children[i] as Border;
                    if (border != null)
                    {
                        border.Tag = chips[i].Params;
                        var tb = border.Child as TextBlock;
                        if (tb != null)
                        {
                            tb.Text = chips[i].Title;
                        }
                    }
                }
            }
            else
            {
                HomeChipsPanel.Children.Clear();
                _activeHomeChipBorder = null;
                for (int i = 0; i < chips.Count; i++)
                {
                    var chip = chips[i];
                    var border = new Border
                    {
                        Background = _ytmChipInactiveBgBrush,
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(14, 6, 14, 6),
                        Margin = new Thickness(0, 0, (i == chips.Count - 1) ? 16 : 8, 0),
                        Tag = chip.Params
                    };
                    var tb = new TextBlock
                    {
                        Text = chip.Title,
                        Foreground = _ytmChipInactiveFgBrush,
                        FontSize = 13,
                        FontWeight = Windows.UI.Text.FontWeights.SemiBold
                    };
                    try
                    {
                        if (Resources.ContainsKey("MontserratSemiBold"))
                            tb.FontFamily = (Windows.UI.Xaml.Media.FontFamily)Resources["MontserratSemiBold"];
                    }
                    catch { }
                    border.Child = tb;
                    border.Tapped += HomeChip_Tapped;
                    if (!string.IsNullOrEmpty(_currentHomeFilterParams) && chip.Params == _currentHomeFilterParams)
                    {
                        _activeHomeChipBorder = border;
                        SetChipVisualState(border, true);
                    }
                    HomeChipsPanel.Children.Add(border);
                }
            }
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
        private const double PULL_RESTING_OFFSET = 60.0;
        private const double PULL_TRIGGER_THRESHOLD = 15.0;

        private bool _isPullReady = false;
        private bool _isRefreshingHome = false;
        private bool _pullEligible = true;
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _pullMutedBrush =
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 179, 179, 179));

        private void InitializeHomePullToRefresh()
        {
            if (HomeMusicPanel == null) return;

            HomeMusicPanel.ViewChanging += HomeMusicPanel_ViewChanging;
            HomeMusicPanel.ViewChanged += HomeMusicPanel_ViewChanged;
            HomeMusicPanel.Loaded += HomeMusicPanel_Loaded;
            _pullEligible = true;
        }

        private async void HomeMusicPanel_Loaded(object sender, RoutedEventArgs e)
        {
            _pullEligible = true;
            ResetHomeScrollToRest(immediate: true);
            await Task.Delay(50);
            ResetHomeScrollToRest(immediate: true);
            _pullEligible = true;
        }

        internal void ResetHomeScrollToRest(bool immediate = false)
        {
            if (HomeMusicPanel == null) return;
            try
            {
                HomeMusicPanel.ChangeView(null, PULL_RESTING_OFFSET, null, disableAnimation: immediate);
            }
            catch { }
            _pullEligible = true;
        }

        internal void EnsureHomePullTimer()
        {
            if (HomeMusicPanel != null && HomeMusicPanel.VerticalOffset < PULL_RESTING_OFFSET && HomeMusicPanel.Visibility == Visibility.Visible)
            {
                ResetHomeScrollToRest(immediate: true);
            }
            _pullEligible = true;
        }

        private void HomeMusicPanel_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            if (HomeMusicPanel == null || HomePullIndicator == null) return;

            double offset = e.NextView.VerticalOffset;

            // Normal scroll down into feed
            if (offset >= PULL_RESTING_OFFSET)
            {
                if (offset >= PULL_RESTING_OFFSET + 10.0)
                {
                    _pullEligible = false;
                }

                if (!_isRefreshingHome)
                {
                    _isPullReady = false;
                    HomePullIndicator.Opacity = 0;
                }

                // Check for lazy loading near bottom (SimpMusic style)
                if (HomeMusicPanel.ScrollableHeight > 0 && offset >= HomeMusicPanel.ScrollableHeight - 600)
                {
                    if (!_isLoadingMoreHomeSections && _hasMoreHomeSections && !string.IsNullOrEmpty(_homeContinuationToken))
                    {
                        var _ = LoadNextHomeContinuationAsync();
                    }
                }
                return;
            }

            // Pulling down in overscroll area (offset < 60.0)
            if (_isRefreshingHome) return;

            // If view is changing due to inertia (fling from below, or inertia bounce),
            // OR if this gesture did not originate from the top rest position:
            if (e.IsInertial || !_pullEligible)
            {
                // If user was dragging, reached threshold and just released finger (transition to inertia bounce):
                if (_isPullReady)
                {
                    _isPullReady = false;
                    _pullEligible = false;
                    var ignored = RefreshHomeFeedAsync();
                    return;
                }

                // Otherwise (upward inertia fling from below, or scroll from bottom):
                // Hide indicator and do not arm pull-to-refresh
                _isPullReady = false;
                HomePullIndicator.Opacity = 0;
                return;
            }

            // Active direct user drag starting from top rest position
            double pullDistance = PULL_RESTING_OFFSET - offset;
            HomePullIndicator.Opacity = Math.Min(1.0, pullDistance / 30.0);

            if (offset <= PULL_TRIGGER_THRESHOLD)
            {
                if (!_isPullReady)
                {
                    _isPullReady = true;
                    HomePullArrowRotate.Angle = 180;
                    HomePullArrowPath.Fill = _chipActiveBrush;
                    HomePullText.Text = "Release to refresh";
                    HomePullText.Foreground = _chipActiveBrush;
                }
            }
            else
            {
                if (_isPullReady)
                {
                    _isPullReady = false;
                    HomePullArrowRotate.Angle = 0;
                    HomePullArrowPath.Fill = _pullMutedBrush;
                    HomePullText.Text = "Pull to refresh";
                    HomePullText.Foreground = _pullMutedBrush;
                }
            }
        }

        private void HomeMusicPanel_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!e.IsIntermediate && !_isRefreshingHome)
            {
                // Check lazy loading on inertia stop
                if (HomeMusicPanel != null && HomeMusicPanel.ScrollableHeight > 0 && HomeMusicPanel.VerticalOffset >= HomeMusicPanel.ScrollableHeight - 600)
                {
                    if (!_isLoadingMoreHomeSections && _hasMoreHomeSections && !string.IsNullOrEmpty(_homeContinuationToken))
                    {
                        var _ = LoadNextHomeContinuationAsync();
                    }
                }

                if (_isPullReady)
                {
                    _isPullReady = false;
                    _pullEligible = false;
                    var ignored = RefreshHomeFeedAsync();
                }
                else if (HomeMusicPanel != null && HomeMusicPanel.VerticalOffset < PULL_RESTING_OFFSET)
                {
                    ResetHomeScrollToRest(immediate: false);
                }
                else if (HomeMusicPanel != null && Math.Abs(HomeMusicPanel.VerticalOffset - PULL_RESTING_OFFSET) <= 5.0)
                {
                    _pullEligible = true;
                }
            }
        }

        private async Task RefreshHomeFeedAsync()
        {
            if (_isRefreshingHome) return;
            _isRefreshingHome = true;

            try
            {
                // 1. Visually clear current items so user sees home page disappear and reload
                _homeContinuationToken = null;
                _hasMoreHomeSections = false;
                _homeLoadedPagesCount = 0;
                _isLoadingMoreHomeSections = false;
                _isLoadingHomeEndSections = false;
                if (_homeDynamicSections != null)
                    _homeDynamicSections.Clear();
                if (HomeDynamicSections != null)
                    HomeDynamicSections.ItemsSource = null;
                if (HomeQuickGrid != null)
                {
                    HomeQuickGrid.ItemsSource = null;
                    HomeQuickGrid.Visibility = Visibility.Collapsed;
                }
                if (HomeHistoryCarousel != null)
                    HomeHistoryCarousel.ItemsSource = null;
                if (HomeArtistsCarousel != null)
                    HomeArtistsCarousel.ItemsSource = null;
                if (HomeHistorySection != null)
                    HomeHistorySection.Visibility = Visibility.Collapsed;
                if (HomeArtistsSection != null)
                    HomeArtistsSection.Visibility = Visibility.Collapsed;
                if (MoodsGenresListView != null)
                {
                    MoodsGenresListView.ItemsSource = null;
                    MoodsGenresListView.Visibility = Visibility.Collapsed;
                }
                if (HomeChartsCarousel != null)
                {
                    HomeChartsCarousel.ItemsSource = null;
                    HomeChartsCarousel.Visibility = Visibility.Collapsed;
                }
                if (HomeChartsTitle != null)
                    HomeChartsTitle.Visibility = Visibility.Collapsed;

                // 2. Show UI progress indicators
                if (HomePullIndicator != null)
                    HomePullIndicator.Opacity = 1.0;
                if (HomePullSpinnerGrid != null)
                    HomePullSpinnerGrid.Visibility = Visibility.Visible;
                if (HomePullSpinnerStoryboard != null)
                    HomePullSpinnerStoryboard.Begin();
                if (HomePullArrowBox != null)
                    HomePullArrowBox.Visibility = Visibility.Collapsed;
                if (HomePullText != null)
                {
                    HomePullText.Text = "Updating...";
                    HomePullText.Foreground = _chipActiveBrush;
                }

                // 3. Clear Home caches to force fresh recommendations from YouTube
                InnerTubeClient.ClearHomeCache();

                var loadRecsTask = LoadHomeRecommendations(_currentHomeFilterParams);
                if (string.IsNullOrEmpty(_currentHomeFilterParams))
                {
                    RefreshHomeHistorySections();
                }
                await loadRecsTask;
            }
            catch { }
            finally
            {
                // 5. Reset UI states smoothly
                if (HomeLoading != null)
                    HomeLoading.Visibility = Visibility.Collapsed;

                if (HomePullSpinnerStoryboard != null)
                    HomePullSpinnerStoryboard.Stop();
                if (HomePullSpinnerGrid != null)
                    HomePullSpinnerGrid.Visibility = Visibility.Collapsed;
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
                _pullEligible = true;

                // 6. Smoothly snap back to resting position if still at top
                if (HomeMusicPanel != null && HomeMusicPanel.VerticalOffset < PULL_RESTING_OFFSET)
                {
                    ResetHomeScrollToRest(immediate: false);
                }
            }
        }
        #endregion

        #region Dynamic Home Sections Enrichment & Interactions
        private void EnrichHomeSections(System.Collections.Generic.List<YTMusicWP.InnerTubeClient.HomeSection> sections, bool isFilterActive)
        {
            if (sections == null) return;

            // 1. Ensure all MultiTrackColumn sections have their columns populated, and SpeedDial has pages populated
            foreach (var sec in sections)
            {
                if (sec.Layout == YTMusicWP.InnerTubeClient.HomeSectionLayout.MultiTrackColumn || sec.Layout == YTMusicWP.InnerTubeClient.HomeSectionLayout.QuickPicks)
                {
                    sec.Layout = YTMusicWP.InnerTubeClient.HomeSectionLayout.MultiTrackColumn;
                    if (sec.TrackColumns == null || sec.TrackColumns.Count == 0)
                        sec.PopulateColumns(4);
                }
                else if (sec.Layout == YTMusicWP.InnerTubeClient.HomeSectionLayout.SpeedDial)
                {
                    if (sec.SpeedDialPages == null || sec.SpeedDialPages.Count == 0)
                        sec.PopulateSpeedDialPages(9);
                }
                else if (sec.Layout == YTMusicWP.InnerTubeClient.HomeSectionLayout.LandscapeVideo)
                {
                    foreach (var t in sec.Tracks) { t.CoverWidth = 260; }
                }
            }

            // If a mood/activity filter is active (e.g. "Relax", "Workout"), don't inject personal speed-dial or library mix
            if (isFilterActive) return;

            // 2. "Speed dial" (Speed Dial 3x3 Grid Carousel)
            bool hasSpeedDial = sections.Any(s => s.Layout == YTMusicWP.InnerTubeClient.HomeSectionLayout.SpeedDial);
            if (!hasSpeedDial)
            {
                var dialTracks = new System.Collections.Generic.List<YouTubeTrack>();
                if (historyTracks != null && historyTracks.Count > 0)
                {
                    dialTracks.AddRange(historyTracks.Where(t => IsMusicTrack(t)));
                }
                if (dialTracks.Count < 9 && favoriteTracks != null && favoriteTracks.Count > 0)
                {
                    foreach (var f in favoriteTracks.Where(t => IsMusicTrack(t)))
                    {
                        if (!dialTracks.Any(d => d.VideoId == f.VideoId))
                            dialTracks.Add(f);
                    }
                }
                if (dialTracks.Count < 9)
                {
                    foreach (var t in sections.SelectMany(s => s.Tracks).Where(t => IsMusicTrack(t)))
                    {
                        if (!dialTracks.Any(d => d.VideoId == t.VideoId))
                            dialTracks.Add(t);
                        if (dialTracks.Count >= 18) break;
                    }
                }

                if (dialTracks.Count >= 1)
                {
                    var speedDial = new YTMusicWP.InnerTubeClient.HomeSection
                    {
                        Title = "Speed dial",
                        Layout = YTMusicWP.InnerTubeClient.HomeSectionLayout.SpeedDial
                    };
                    int dialTake = Math.Min(18, Math.Max(dialTracks.Count, 9));
                    for (int i = 0; i < dialTake; i++)
                    {
                        var src = dialTracks[i % dialTracks.Count];
                        var dt = new YouTubeTrack
                        {
                            VideoId = src.VideoId,
                            Title = src.Title,
                            ChannelName = src.ChannelName,
                            ThumbnailUrl = src.ThumbnailUrl,
                            PlayProgressPercent = 0.35 + ((i * 17) % 55) / 100.0
                        };
                        speedDial.Tracks.Add(dt);
                    }
                    speedDial.PopulateSpeedDialPages(9);
                    sections.Insert(0, speedDial);
                }
            }

            // 3. "From your library" (Featured Card with 3 preview tracks + Play, Radio, Save)
            bool hasFeatured = sections.Any(s => s.Layout == YTMusicWP.InnerTubeClient.HomeSectionLayout.FeaturedCard);
            if (!hasFeatured)
            {
                var libraryPool = (favoriteTracks != null && favoriteTracks.Count >= 3) ? favoriteTracks : historyTracks;
                if (libraryPool != null && libraryPool.Count >= 3)
                {
                    var featSec = new YTMusicWP.InnerTubeClient.HomeSection
                    {
                        Title = "Familiar and similar favorites",
                        Subtitle = "Based on songs you recently loved",
                        CategoryTag = "FROM YOUR LIBRARY",
                        Layout = YTMusicWP.InnerTubeClient.HomeSectionLayout.FeaturedCard,
                        FeaturedCoverUrl = libraryPool[0].ThumbnailUrl
                    };
                    for (int i = 0; i < Math.Min(6, libraryPool.Count); i++)
                    {
                        featSec.Tracks.Add(libraryPool[i]);
                    }
                    int insertPos = Math.Min(2, sections.Count);
                    sections.Insert(insertPos, featSec);
                }
            }

            // 4. "Most discussed tracks" (Most Discussed Tracks with Real YouTube Comments)
            bool hasDiscussed = sections.Any(s => s.Layout == YTMusicWP.InnerTubeClient.HomeSectionLayout.MostDiscussed || s.Title.Contains("discussed") || s.Title.Contains("bình luận"));
            if (!hasDiscussed)
            {
                var candidateTracks = sections.SelectMany(s => s.Tracks).Where(t => IsMusicTrack(t) && !t.VideoId.StartsWith("PLAYLIST:") && !t.VideoId.StartsWith("CHANNEL:")).Take(6).ToList();
                if (candidateTracks.Count >= 2)
                {
                    var discussedSec = new YTMusicWP.InnerTubeClient.HomeSection
                    {
                        Title = "Most discussed tracks",
                        Layout = YTMusicWP.InnerTubeClient.HomeSectionLayout.MostDiscussed
                    };

                    foreach (var tr in candidateTracks)
                    {
                        var dTrack = new YouTubeTrack
                        {
                            VideoId = tr.VideoId,
                            Title = tr.Title,
                            ChannelName = tr.ChannelName,
                            ThumbnailUrl = tr.ThumbnailUrl,
                            TopCommentText = "Loading top comment...",
                            CommentCount = "..."
                        };
                        discussedSec.Tracks.Add(dTrack);
                    }
                    int insertPos = Math.Min(4, sections.Count);
                    sections.Insert(insertPos, discussedSec);

                    // Asynchronously load real comments from YouTube
                    var ignoredComments = LoadRealCommentsForSectionAsync(discussedSec);
                }
            }

            // 5. "Discover this week's hottest new tracks!" (Editorial Discovery Banner)
            bool hasBanner = sections.Any(s => s.Layout == YTMusicWP.InnerTubeClient.HomeSectionLayout.EditorialBanner);
            if (!hasBanner && sections.Count >= 3)
            {
                var bannerSec = new YTMusicWP.InnerTubeClient.HomeSection
                {
                    Title = "Discover this week's hottest new tracks!",
                    Layout = YTMusicWP.InnerTubeClient.HomeSectionLayout.EditorialBanner
                };
                int insertPos = Math.Min(3, sections.Count);
                sections.Insert(insertPos, bannerSec);
            }
        }

        private void PlayAllSection_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var section = element?.Tag as YTMusicWP.InnerTubeClient.HomeSection;
            if (section != null && section.Tracks != null && section.Tracks.Count > 0)
            {
                var musicTracks = section.Tracks.Where(t => IsMusicTrack(t)).ToList();
                if (musicTracks.Count > 0)
                {
                    PlayTrack(musicTracks[0]);
                }
            }
        }

        private void HomeTrackRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var track = element?.DataContext as YouTubeTrack ?? element?.Tag as YouTubeTrack;
            if (track != null)
            {
                PlayTrack(track);
            }
        }

        private void FeaturedTrack_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var track = element?.Tag as YouTubeTrack ?? element?.DataContext as YouTubeTrack;
            if (track != null)
            {
                PlayTrack(track);
            }
        }

        private void FeaturedCardRadio_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var section = element?.Tag as YTMusicWP.InnerTubeClient.HomeSection;
            if (section != null && section.Tracks != null && section.Tracks.Count > 0)
            {
                _bottomSheetTrack = section.Tracks[0];
                BottomSheetGoToRadio_Click(null, null);
            }
        }

        private void FeaturedCardSave_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var section = element?.Tag as YTMusicWP.InnerTubeClient.HomeSection;
            if (section != null && section.Tracks != null && section.Tracks.Count > 0)
            {
                int addedCount = 0;
                foreach (var t in section.Tracks)
                {
                    if (IsMusicTrack(t) && !favoriteTracks.Any(f => f.VideoId == t.VideoId))
                    {
                        favoriteTracks.Insert(0, t);
                        addedCount++;
                    }
                }
                if (addedCount > 0)
                {
                    SaveFavoritesAsync();
                    ShowToast("Added " + addedCount + " songs to Favorites");
                }
                else
                {
                    ShowToast("All songs are already in Favorites");
                }
            }
        }

        private void EditorialBanner_Tapped(object sender, TappedRoutedEventArgs e)
        {
            OpenYouTubePlaylist("RDCLAK5uy_mXSZ6uWtp8W7PC7QnwF2cY9RyD8zNd7DY", "RELEASED", null);
        }

        private async Task LoadRealCommentsForSectionAsync(YTMusicWP.InnerTubeClient.HomeSection section)
        {
            if (section == null || section.Tracks == null) return;

            foreach (var track in section.Tracks)
            {
                if (string.IsNullOrEmpty(track.VideoId) || track.VideoId.StartsWith("PLAYLIST:") || track.VideoId.StartsWith("CHANNEL:"))
                    continue;

                try
                {
                    var snippet = await YTMusicWP.InnerTubeClient.GetTopCommentSnippetAsync(track.VideoId);
                    if (snippet != null)
                    {
                        if (!string.IsNullOrEmpty(snippet.Text))
                        {
                            track.TopCommentText = snippet.Text;
                        }
                        else
                        {
                            track.TopCommentText = "Active discussion on YouTube Music.";
                        }

                        string cCount = snippet.CommentCountText;
                        if (!string.IsNullOrEmpty(cCount))
                        {
                            track.CommentCount = cCount.IndexOf("comment", StringComparison.OrdinalIgnoreCase) >= 0 
                                ? cCount 
                                : (cCount + " comments");
                        }
                        else
                        {
                            track.CommentCount = "Top comment";
                        }
                        track.TopCommentAuthor = snippet.Author;
                    }
                    else
                    {
                        if (track.CommentCount == "...")
                        {
                            track.CommentCount = "Comments";
                            track.TopCommentText = "Trending track with active community listeners.";
                        }
                    }
                }
                catch { }
            }
        }
        #endregion
    }
}
