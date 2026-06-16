using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;

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
            }
            else
            {
                HomeHistorySection.Visibility = Visibility.Collapsed;
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
        private void SetHomeChipActive(Border active)
        {
            HomeChipAll.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51));
            HomeChipMusic.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51));
            HomeChipPodcasts.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51));
            HomeChipAudiobooks.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51));
            active.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 185, 84)); // #1DB954
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
                string region = "US";
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
                    region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();

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
                string region = "US";
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("TrendingRegion"))
                    region = ApplicationData.Current.LocalSettings.Values["TrendingRegion"].ToString();

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
