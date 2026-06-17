using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsInternetAvailable()) { ShowToast("No Internet"); return; }
            _typingTimer.Stop(); SuggestionPopup.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchLoading.Visibility = Visibility.Visible;
                DefaultSearchUI.Visibility = Visibility.Collapsed;
                SearchSongList.Visibility = Visibility.Collapsed;
                _nextSearchToken = "";
                _currentSearchQuery = SearchBox.Text.Trim();
                _isLoadingMoreSearch = false;
                ExecuteSearch(_currentSearchQuery);
            }
        }

        private ScrollViewer _searchScrollViewer;

        private async void ExecuteSearch(string query)
        {
            SearchLoading.Visibility = Visibility.Visible;
            DefaultSearchUI.Visibility = Visibility.Collapsed;

            SearchSongList.Visibility = Visibility.Visible;
            SearchSongList.ItemsSource = searchResults;

            searchResults.Clear();

            // Detect active filter
            var songBg = ((Windows.UI.Xaml.Media.SolidColorBrush)FilterSongsBtn.Background).Color;
            var playlistBg = ((Windows.UI.Xaml.Media.SolidColorBrush)FilterPlaylistsBtn.Background).Color;
            var artistBg = ((Windows.UI.Xaml.Media.SolidColorBrush)FilterArtistsBtn.Background).Color;
            var videoBg = ((Windows.UI.Xaml.Media.SolidColorBrush)FilterVideosBtn.Background).Color;
            var activeColor = Windows.UI.Color.FromArgb(255, 29, 185, 84); // #1DB954

            // YouTube Music search params for filtered search
            // Songs: EgWKAQIIAWoKEAMQBBAKEAkQBQ%3D%3D
            // Videos: EgWKAQIQAWoKEAMQBBAKEAkQBQ%3D%3D  
            // Playlists: EgeKAQQoAEABagoQAxAEEAoQCRAF
            // Artists: EgWKAQIgAWoKEAMQBBAKEAkQBQ%3D%3D
            string searchFilter = null;
            if (songBg == activeColor) searchFilter = "songs";
            else if (videoBg == activeColor) searchFilter = "videos";
            else if (playlistBg == activeColor) searchFilter = "playlists";
            else if (artistBg == activeColor) searchFilter = "artists";

            var tracks = await FetchMusicList(query, "", searchFilter: searchFilter);

            if (tracks.Count > 0)
            {
                var filteredTracks = tracks.AsEnumerable();
                
                // Additional client-side filter as safety net
                if (searchFilter == "songs" || searchFilter == "videos")
                    filteredTracks = filteredTracks.Where(t => !t.VideoId.StartsWith("PLAYLIST:") && !t.VideoId.StartsWith("CHANNEL:"));
                else if (searchFilter == "playlists")
                    filteredTracks = filteredTracks.Where(t => t.VideoId.StartsWith("PLAYLIST:"));
                else if (searchFilter == "artists")
                    filteredTracks = filteredTracks.Where(t => t.VideoId.StartsWith("CHANNEL:"));

                foreach (var t in filteredTracks) searchResults.Add(t);
            }
            else
            {
                ShowToast("No results found.");
            }
            SearchLoading.Visibility = Visibility.Collapsed;

            System.Diagnostics.Debug.WriteLine("[Search] Results: " + searchResults.Count + ", NextToken: " + (_nextSearchToken ?? "null"));

            // Attach ScrollViewer AFTER data loaded and layout updated
            SearchSongList.UpdateLayout();
            AttachSearchScrollViewer();

            // Fade-in search results
            SearchSongList.Opacity = 0;
            var fadeIn = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var anim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(200))
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, SearchSongList);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
            fadeIn.Children.Add(anim);
            fadeIn.Begin();
        }

        private void AttachSearchScrollViewer()
        {
            if (_searchScrollViewer != null)
            {
                _searchScrollViewer.ViewChanged -= SearchScrollViewer_ViewChanged;
                _searchScrollViewer.ViewChanged += SearchScrollViewer_ViewChanged;
                return;
            }

            var sv = GetScrollViewer(SearchSongList);
            if (sv != null)
            {
                _searchScrollViewer = sv;
                sv.ViewChanged -= SearchScrollViewer_ViewChanged;
                sv.ViewChanged += SearchScrollViewer_ViewChanged;
                System.Diagnostics.Debug.WriteLine("[Search] ScrollViewer attached OK");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Search] ScrollViewer NOT found, will retry on Loaded");
                SearchSongList.Loaded -= SearchSongList_Loaded;
                SearchSongList.Loaded += SearchSongList_Loaded;
            }
        }

        private void SearchSongList_Loaded(object sender, RoutedEventArgs e)
        {
            SearchSongList.Loaded -= SearchSongList_Loaded;
            var sv = GetScrollViewer(SearchSongList);
            if (sv != null)
            {
                _searchScrollViewer = sv;
                sv.ViewChanged -= SearchScrollViewer_ViewChanged;
                sv.ViewChanged += SearchScrollViewer_ViewChanged;
                System.Diagnostics.Debug.WriteLine("[Search] ScrollViewer attached via Loaded event");
            }
        }

        private async void SearchScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv != null && sv.VerticalOffset >= sv.ScrollableHeight - 200 && !_isLoadingMoreSearch && !string.IsNullOrEmpty(_nextSearchToken))
            {
                _isLoadingMoreSearch = true;
                SearchLoading.Visibility = Visibility.Visible;
                System.Diagnostics.Debug.WriteLine("[Search] Loading more with token: " + _nextSearchToken.Substring(0, Math.Min(30, _nextSearchToken.Length)) + "...");

                var tracks = await FetchMusicList(_currentSearchQuery, _nextSearchToken);
                if (tracks != null)
                {
                    foreach (var t in tracks) searchResults.Add(t);
                    System.Diagnostics.Debug.WriteLine("[Search] Loaded " + tracks.Count + " more, total: " + searchResults.Count);
                }

                SearchLoading.Visibility = Visibility.Collapsed;
                _isLoadingMoreSearch = false;
            }
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

        private void ResetFilters()
        {
            FilterAllBtn.Background = _filterInactiveBg;
            FilterSongsBtn.Background = _filterInactiveBg;
            FilterPlaylistsBtn.Background = _filterInactiveBg;
            FilterArtistsBtn.Background = _filterInactiveBg;
            FilterVideosBtn.Background = _filterInactiveBg;
        }

        private void FilterAllBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterAllBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

        private void FilterSongsBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterSongsBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

        private void FilterPlaylistsBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterPlaylistsBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

        private void FilterArtistsBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterArtistsBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

        private void FilterVideosBtn_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ResetFilters();
            FilterVideosBtn.Background = _filterActiveBg;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchButton_Click(null, null);
        }

        // ==========================================
        // DISCOVER SECTION — Trending music from YouTube Music Explore
        // ==========================================
        private bool _discoverLoaded = false;

        private async void LoadDiscoverSection()
        {
            if (_discoverLoaded && DiscoverListView.Items != null && DiscoverListView.Items.Count > 0) return;
            
            try
            {
                var items = await InnerTubeClient.BrowseExploreAsync();
                if (items != null && items.Count > 0)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        DiscoverListView.ItemsSource = items;
                        _discoverLoaded = true;
                    });
                }
            }
            catch { }
        }

        private void DiscoverItem_Click(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as DiscoverItem;
            if (item == null) return;

            // Open Shorts view (discover items are part of Shorts feed)
            OpenShortsView(0);
        }

        private void OpenShortsFromDiscover()
        {
            OpenShortsView(0);
        }

        private void OpenShortsBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenShortsView(0);
        }

    }
}
