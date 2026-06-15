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
    }
}
