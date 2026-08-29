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

        // [OPT-AV] Debounce: don't refresh artists more than once per 5 minutes
        private DateTime _lastArtistRefreshTime = DateTime.MinValue;

        private async void RefreshRecentArtists()
        {
            // Debounce: skip if called within last 5 minutes
            if ((DateTime.Now - _lastArtistRefreshTime).TotalMinutes < 5) return;
            _lastArtistRefreshTime = DateTime.Now;

            try
            {
                var seenArtists = new System.Collections.Generic.HashSet<string>();
                var artistItems = new System.Collections.Generic.List<YouTubeTrack>();

                // [OPT-AV] Load cached avatars from LocalSettings
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;

                foreach (var track in historyTracks)
                {
                    if (string.IsNullOrEmpty(track.ChannelName) || track.ChannelName == "Unknown") continue;
                    string key = track.ChannelName.ToLowerInvariant();
                    if (seenArtists.Contains(key)) continue;
                    seenArtists.Add(key);

                    // Check avatar cache first
                    string cacheKey = "AvatarCache_" + key;
                    string cachedAvatar = localSettings.ContainsKey(cacheKey) ? localSettings[cacheKey] as string : null;
                    string cachedChannelKey = "AvatarChId_" + key;
                    string cachedChannelId = localSettings.ContainsKey(cachedChannelKey) ? localSettings[cachedChannelKey] as string : null;

                    artistItems.Add(new YouTubeTrack
                    {
                        VideoId = !string.IsNullOrEmpty(cachedChannelId) ? "CHANNEL:" + cachedChannelId
                                : !string.IsNullOrEmpty(track.ChannelId) ? "CHANNEL:" + track.ChannelId : "",
                        Title = track.ChannelName,
                        ChannelName = track.ChannelName,
                        ChannelId = cachedChannelId ?? track.ChannelId,
                        ThumbnailUrl = !string.IsNullOrEmpty(cachedAvatar) ? cachedAvatar : GetSquareThumbnail(track.ThumbnailUrl)
                    });

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
                                            });
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(artist.ChannelId))
                                    {
                                        var searchResults2 = await InnerTubeClient.SearchAsync(artist.Title + " artist", 3);
                                        var fallbackMatch = searchResults2.FirstOrDefault(r =>
                                            r.VideoId != null && r.VideoId.StartsWith("CHANNEL:"));
                                        if (fallbackMatch != null && !string.IsNullOrEmpty(fallbackMatch.ThumbnailUrl))
                                        {
                                            string fallbackAvatar = GetArtistAvatar(fallbackMatch.ThumbnailUrl);
                                            string ck = "AvatarCache_" + artist.Title.ToLowerInvariant();
                                            localSettings[ck] = fallbackAvatar;

                                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                                            {
                                                artistItems[idx].ThumbnailUrl = fallbackAvatar;
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
                string token = await GetAccessTokenAsync();
                var homeTask = InnerTubeClient.BrowseHomeAsync(token);
                var chartsTask = InnerTubeClient.BrowseChartsAsync();
                var homeSections = default(System.Collections.Generic.List<InnerTubeClient.HomeSection>);
                var chartsData = default(System.Collections.Generic.List<DiscoverItem>);

                try
                {
                    homeSections = await homeTask;
                }
                catch { }

                try
                {
                    chartsData = await chartsTask;
                }
                catch { }

                // Charts
                if (chartsData != null && chartsData.Count > 0)
                {
                    HomeChartsTitle.Visibility = Visibility.Visible;
                    HomeChartsCarousel.Visibility = Visibility.Visible;
                    HomeChartsCarousel.ItemsSource = chartsData;
                }

                // Dynamic home sections — bind ALL sections YouTube returns
                if (homeSections != null && homeSections.Count > 0)
                {
                    HomeDynamicSections.ItemsSource = homeSections;

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
    }
}
