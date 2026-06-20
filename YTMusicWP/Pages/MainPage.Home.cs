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

                // Recently Played Artists — extract unique artists from history
                RefreshRecentArtists();
            }
            else
            {
                HomeHistorySection.Visibility = Visibility.Collapsed;
                HomeArtistsSection.Visibility = Visibility.Collapsed;
            }
        }

        private async void RefreshRecentArtists()
        {
            try
            {
                var seenArtists = new System.Collections.Generic.HashSet<string>();
                var artistItems = new System.Collections.Generic.List<YouTubeTrack>();

                foreach (var track in historyTracks)
                {
                    if (string.IsNullOrEmpty(track.ChannelName) || track.ChannelName == "Unknown") continue;
                    string key = track.ChannelName.ToLowerInvariant();
                    if (seenArtists.Contains(key)) continue;
                    seenArtists.Add(key);

                    artistItems.Add(new YouTubeTrack
                    {
                        VideoId = !string.IsNullOrEmpty(track.ChannelId) ? "CHANNEL:" + track.ChannelId : "",
                        Title = track.ChannelName,
                        ChannelName = track.ChannelName,
                        ChannelId = track.ChannelId,
                        ThumbnailUrl = track.ThumbnailUrl
                    });

                    if (artistItems.Count >= 10) break;
                }

                if (artistItems.Count >= 2)
                {
                    HomeArtistsSection.Visibility = Visibility.Visible;
                    HomeArtistsCarousel.ItemsSource = artistItems;

                    // Fetch real artist avatars via YouTube Music search (correct disambiguation)
                    for (int i = 0; i < artistItems.Count; i += 3)
                    {
                        var batch = new System.Collections.Generic.List<Task>(3);
                        for (int j = i; j < Math.Min(i + 3, artistItems.Count); j++)
                        {
                            var artist = artistItems[j];
                            int idx = j;
                            batch.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    // Search YouTube Music for artist — more reliable than channelId browse
                                    var searchResults = await InnerTubeClient.SearchAsync(artist.Title, 5);
                                    var artistMatch = searchResults.FirstOrDefault(r =>
                                        r.VideoId != null && r.VideoId.StartsWith("CHANNEL:") &&
                                        r.Title.Equals(artist.Title, StringComparison.OrdinalIgnoreCase));

                                    if (artistMatch != null)
                                    {
                                        string ytmChannelId = artistMatch.VideoId.Replace("CHANNEL:", "");
                                        var result = await InnerTubeClient.BrowseArtistAsync(ytmChannelId);
                                        if (!string.IsNullOrEmpty(result.AvatarUrl))
                                        {
                                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                                            {
                                                artistItems[idx].ThumbnailUrl = GetHighResThumbnail(result.AvatarUrl);
                                                artistItems[idx].ChannelId = ytmChannelId;
                                            });
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(artist.ChannelId))
                                    {
                                        // Fallback: use existing channelId
                                        var result = await InnerTubeClient.BrowseArtistAsync(artist.ChannelId);
                                        if (!string.IsNullOrEmpty(result.AvatarUrl))
                                        {
                                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                                            {
                                                artistItems[idx].ThumbnailUrl = GetHighResThumbnail(result.AvatarUrl);
                                            });
                                        }
                                    }
                                }
                                catch { }
                            }));
                        }
                        await Task.WhenAll(batch);
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
            // PRIMARY: YouTube Music Home (FE_music_home)
            // Real recommendations, new releases, trending — all from YT Music algorithm
            // ═══════════════════════════════════════════════════
            try
            {
                var homeSections = await InnerTubeClient.BrowseHomeAsync();
                if (homeSections != null && homeSections.Count > 0)
                {
                    // Map API sections to UI carousels (up to 8)
                    var carousels = new[] { homeTracks, popTracks, lofiTracks, workoutTracks, genre5Tracks, genre6Tracks, genre7Tracks, genre8Tracks };
                    var titles = new[] { HomeTrendingTitle, HomePopTitle, HomeChillTitle, HomeWorkoutTitle, HomeGenre5Title, HomeGenre6Title, HomeGenre7Title, HomeGenre8Title };

                    for (int i = 0; i < Math.Min(homeSections.Count, 8); i++)
                    {
                        titles[i].Text = homeSections[i].Title;
                        foreach (var t in homeSections[i].Tracks)
                        {
                            carousels[i].Add(t);
                        }
                    }


                    if (homeSections.Count > 0) _currentHomeQuery = homeSections[0].Title;
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
                        "rap Việt " + year,
                        "V-pop acoustic",
                        "nhạc phim Việt hay nhất",
                        "EDM Việt mix",
                        "nhạc indie Việt"
                    };
                    fallbackTitles = new[] {
                        "Made for you", "Nhạc trẻ", "Bolero - Trữ tình", "Rap Việt",
                        "Acoustic Việt", "Nhạc phim Việt", "EDM Việt Mix", "Indie Việt"
                    };
                    break;
                case "KR":
                    queries = new[] {
                        "K-pop trending " + year,
                        "K-pop girl group hits",
                        "K-drama OST " + year,
                        "K-pop boy group hits",
                        "K-R&B chill",
                        "K-pop dance hits",
                        "Korean indie",
                        "K-hip hop " + year
                    };
                    fallbackTitles = new[] {
                        "Made for you", "Girl Group Hits", "K-Drama OST", "Boy Group Hits",
                        "K-R&B Chill", "K-Pop Dance", "Korean Indie", "K-Hip Hop"
                    };
                    break;
                case "JP":
                    queries = new[] {
                        "J-pop trending " + year,
                        "Anime OST " + year,
                        "J-pop chill vibes",
                        "J-rock hits",
                        "Vocaloid popular",
                        "city pop Japanese",
                        "anime opening " + year,
                        "Japanese lofi hip hop"
                    };
                    fallbackTitles = new[] {
                        "Made for you", "Anime OST", "Chill vibes", "J-Rock",
                        "Vocaloid", "City Pop", "Anime Opening", "Japanese Lofi"
                    };
                    break;
                default:
                    queries = new[] {
                        "top hits " + year,
                        "pop hits " + year,
                        "lofi chill beats relax",
                        "workout gym motivation music",
                        "hip hop rap hits " + year,
                        "R&B soul hits",
                        "rock classics greatest hits",
                        "indie alternative " + year
                    };
                    fallbackTitles = new[] {
                        "Made for you", "Pop Hits", "Chill vibes", "Workout Motivation",
                        "Hip-Hop & Rap", "R&B & Soul", "Rock Classics", "Indie & Alt"
                    };
                    break;
            }

            HomeTrendingTitle.Text = fallbackTitles[0];
            HomePopTitle.Text = fallbackTitles[1];
            HomeChillTitle.Text = fallbackTitles[2];
            HomeWorkoutTitle.Text = fallbackTitles[3];
            HomeGenre5Title.Text = fallbackTitles[4];
            HomeGenre6Title.Text = fallbackTitles[5];
            HomeGenre7Title.Text = fallbackTitles[6];
            HomeGenre8Title.Text = fallbackTitles[7];

            _currentHomeQuery = queries[0];

            var batch1a = FetchMusicList(queries[0], "", "songs");
            var batch1b = FetchMusicList(queries[1], "", "songs");
            var trending = await batch1a;
            if (trending != null) foreach (var t in trending) { if (IsMusicTrack(t)) homeTracks.Add(t); }
            var pop = await batch1b;
            if (pop != null) foreach (var t in pop) { if (IsMusicTrack(t)) popTracks.Add(t); }

            var batch2a = FetchMusicList(queries[2], "", "songs");
            var batch2b = FetchMusicList(queries[3], "", "songs");
            var chill = await batch2a;
            if (chill != null) foreach (var t in chill) { if (IsMusicTrack(t)) lofiTracks.Add(t); }
            var workout = await batch2b;
            if (workout != null) foreach (var t in workout) { if (IsMusicTrack(t)) workoutTracks.Add(t); }

            HomeLoading.Visibility = Visibility.Collapsed;

            var batch3a = FetchMusicList(queries[4], "", "songs");
            var batch3b = FetchMusicList(queries[5], "", "songs");
            var g5 = await batch3a;
            if (g5 != null) foreach (var t in g5) { if (IsMusicTrack(t)) genre5Tracks.Add(t); }
            var g6 = await batch3b;
            if (g6 != null) foreach (var t in g6) { if (IsMusicTrack(t)) genre6Tracks.Add(t); }

            var batch4a = FetchMusicList(queries[6], "", "songs");
            var batch4b = FetchMusicList(queries[7], "", "songs");
            var g7 = await batch4a;
            if (g7 != null) foreach (var t in g7) { if (IsMusicTrack(t)) genre7Tracks.Add(t); }
            var g8 = await batch4b;
            if (g8 != null) foreach (var t in g8) { if (IsMusicTrack(t)) genre8Tracks.Add(t); }
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

        private void MoodChill_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SwitchTab(1);
            SearchBox.Text = "lofi chill beats relax";
            SearchButton_Click(null, null);
        }

        private void MoodFocus_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SwitchTab(1);
            SearchBox.Text = "focus study concentration music";
            SearchButton_Click(null, null);
        }

        private void MoodEnergy_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SwitchTab(1);
            SearchBox.Text = "energy workout pump up hits";
            SearchButton_Click(null, null);
        }

        private void MoodSad_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SwitchTab(1);
            SearchBox.Text = "sad emotional songs";
            SearchButton_Click(null, null);
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
