using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
                if (_activeSearchChipBorder != null)
                {
                    SetChipVisualState(_activeSearchChipBorder, false);
                    _activeSearchChipBorder = null;
                }
                _currentSearchFilterParams = null;
                ExecuteSearch(_currentSearchQuery);
            }
        }

        private ScrollViewer _searchScrollViewer;
        private Border _activeSearchChipBorder;
        private string _currentSearchFilterParams;
        private SearchCardItem _lastSearchCard;
        private List<SearchChipItem> _lastSearchChips;

        private async void ExecuteSearch(string query)
        {
            SearchLoading.Visibility = Visibility.Visible;
            DefaultSearchUI.Visibility = Visibility.Collapsed;

            SearchSongList.Visibility = Visibility.Visible;
            SearchSongList.ItemsSource = null;
            searchResults.Clear();
            if (TopResultCard != null) TopResultCard.Visibility = Visibility.Collapsed;
            if (SearchFilterScrollViewer != null) SearchFilterScrollViewer.Visibility = Visibility.Visible;

            var tracks = await FetchMusicList(query, "", searchFilter: _currentSearchFilterParams);

            if (tracks != null && tracks.Count > 0)
            {
                foreach (var t in tracks) searchResults.Add(t);
            }
            else if (_lastSearchCard == null)
            {
                ShowToast("No results found.");
            }

            // Populate Top Result Card
            PopulateTopResultCard(_lastSearchCard);

            // Sync Search Filter Chips from API
            UpdateSearchChips(_lastSearchChips);

            SearchSongList.ItemsSource = searchResults;
            SearchLoading.Visibility = Visibility.Collapsed;

            System.Diagnostics.Debug.WriteLine("[Search] Results: " + searchResults.Count + ", Card: " + (_lastSearchCard != null ? _lastSearchCard.Title : "none") + ", NextToken: " + (_nextSearchToken ?? "null"));

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

                try
                {
                    var tracks = await FetchMusicList(_currentSearchQuery, _nextSearchToken);
                    if (tracks != null && tracks.Count > 0)
                    {
                        for (int i = 0; i < tracks.Count; i++)
                        {
                            searchResults.Add(tracks[i]);
                            if (i % 5 == 4) await Task.Yield();
                        }
                        System.Diagnostics.Debug.WriteLine("[Search] Loaded " + tracks.Count + " more, total: " + searchResults.Count);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Search] Pagination error: " + ex.Message);
                }
                finally
                {
                    SearchLoading.Visibility = Visibility.Collapsed;
                    _isLoadingMoreSearch = false;
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _typingTimer.Stop();

            bool hasText = !string.IsNullOrWhiteSpace(SearchBox.Text);
            SearchClearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

            if (!hasText)
            {
                SuggestionPopup.Visibility = Visibility.Collapsed;
                DefaultSearchUI.Visibility = Visibility.Visible;
                SearchSongList.Visibility = Visibility.Collapsed;
                if (SearchFilterScrollViewer != null) SearchFilterScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                _typingTimer.Start();
                DefaultSearchUI.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchClear_Click(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus(FocusState.Programmatic);
            if (SearchFilterScrollViewer != null) SearchFilterScrollViewer.Visibility = Visibility.Collapsed;
        }

        private void SearchIcon_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SearchButton_Click(sender, null);
        }

        private void SearchBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                this.Focus(FocusState.Programmatic);
                SearchButton_Click(sender, null);
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
                var list = await InnerTubeClient.GetSearchSuggestionsAsync(query);

                // If user changed the search query while request was in-flight, discard old results
                if (SearchBox.Text.Trim() != query) return;

                searchSuggestions.Clear();

                if (list != null && list.Count > 0)
                {
                    foreach (var item in list)
                    {
                        searchSuggestions.Add(item);
                    }
                }

                // Fallback: if YouTube Music API returns nothing, use Google Suggest
                if (searchSuggestions.Count == 0)
                {
                    try
                    {
                        string url = "https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&q=" + Uri.EscapeDataString(query);
                        var response = await _apiClient.GetStringAsync(url);
                        var jsonArray = JArray.Parse(response);
                        if (jsonArray.Count > 1)
                        {
                            var suggestions = jsonArray[1] as JArray;
                            if (suggestions != null)
                            {
                                foreach (var item in suggestions.Take(6))
                                {
                                    searchSuggestions.Add(new SearchSuggestionItem
                                    {
                                        Type = SearchSuggestionType.Query,
                                        Query = item.ToString(),
                                        Title = item.ToString()
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (SearchBox.Text.Trim() == query)
                {
                    SuggestionPopup.Visibility = searchSuggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch { SuggestionPopup.Visibility = Visibility.Collapsed; }
        }

        private void SuggestionList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as SearchSuggestionItem;
            if (item == null) return;

            _typingTimer.Stop();
            SuggestionPopup.Visibility = Visibility.Collapsed;
            this.Focus(FocusState.Programmatic);

            if (item.Type == SearchSuggestionType.Query)
            {
                SearchBox.TextChanged -= SearchBox_TextChanged;
                SearchBox.Text = item.Query;
                SearchBox.TextChanged += SearchBox_TextChanged;
                SearchButton_Click(null, null);
            }
            else if (item.Type == SearchSuggestionType.Song)
            {
                if (!string.IsNullOrEmpty(item.VideoId))
                {
                    PlayTrack(new YouTubeTrack
                    {
                        VideoId = item.VideoId,
                        Title = item.Title,
                        ChannelName = item.Subtitle,
                        ThumbnailUrl = item.ThumbnailUrl
                    });
                }
                else
                {
                    SearchBox.TextChanged -= SearchBox_TextChanged;
                    SearchBox.Text = item.Title;
                    SearchBox.TextChanged += SearchBox_TextChanged;
                    SearchButton_Click(null, null);
                }
            }
            else if (item.Type == SearchSuggestionType.Artist)
            {
                if (!string.IsNullOrEmpty(item.BrowseId))
                {
                    OpenArtistProfile(item.BrowseId, item.Title, true);
                }
                else
                {
                    SearchBox.TextChanged -= SearchBox_TextChanged;
                    SearchBox.Text = item.Title;
                    SearchBox.TextChanged += SearchBox_TextChanged;
                    SearchButton_Click(null, null);
                }
            }
            else if (item.Type == SearchSuggestionType.Playlist || item.Type == SearchSuggestionType.Album)
            {
                if (!string.IsNullOrEmpty(item.BrowseId))
                {
                    string pid = item.BrowseId.StartsWith("VL") ? item.BrowseId.Substring(2) : item.BrowseId;
                    OpenYouTubePlaylist(pid, item.Title, item.ThumbnailUrl);
                }
                else
                {
                    SearchBox.TextChanged -= SearchBox_TextChanged;
                    SearchBox.Text = item.Title;
                    SearchBox.TextChanged += SearchBox_TextChanged;
                    SearchButton_Click(null, null);
                }
            }
        }

        private void SearchResultsArea_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (SuggestionPopup.Visibility == Visibility.Visible)
            {
                SuggestionPopup.Visibility = Visibility.Collapsed;
            }
        }

        private void SuggestionPopup_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void PopulateTopResultCard(SearchCardItem card)
        {
            if (TopResultCard == null) return;
            if (card == null)
            {
                TopResultCard.Visibility = Visibility.Collapsed;
                return;
            }

            TopCardTitle.Text = card.Title ?? "";
            TopCardSubtitle.Text = card.Subtitle ?? "";

            // Thumbnail
            if (!string.IsNullOrEmpty(card.ThumbnailUrl))
            {
                if (card.ArtistThumbVisibility == Visibility.Visible)
                {
                    TopCardArtistThumb.Visibility = Visibility.Visible;
                    TopCardSquareThumb.Visibility = Visibility.Collapsed;
                    var artistThumbBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    artistThumbBmp.DecodePixelWidth = 120;
                    artistThumbBmp.UriSource = new Uri(GetArtistAvatar(card.ThumbnailUrl), UriKind.Absolute);
                    TopCardArtistThumbBrush.ImageSource = artistThumbBmp;
                }
                else
                {
                    TopCardArtistThumb.Visibility = Visibility.Collapsed;
                    TopCardSquareThumb.Visibility = Visibility.Visible;
                    var squareThumbBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    squareThumbBmp.DecodePixelWidth = 120;
                    squareThumbBmp.UriSource = new Uri(GetSquareThumbnail(card.ThumbnailUrl), UriKind.Absolute);
                    TopCardSquareThumbBrush.ImageSource = squareThumbBmp;
                }
            }
            else
            {
                TopCardArtistThumb.Visibility = Visibility.Collapsed;
                TopCardSquareThumb.Visibility = Visibility.Collapsed;
            }

            // Action buttons
            TopCardShuffleBtn.Visibility = card.ShuffleButtonVisibility;
            TopCardMixBtn.Visibility = card.MixButtonVisibility;
            TopCardActionRow.Visibility = (card.ShuffleButtonVisibility == Visibility.Visible || card.MixButtonVisibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;

            // Top Songs
            if (card.TopSongs != null && card.TopSongs.Count > 0)
            {
                TopCardSongsPanel.Visibility = Visibility.Visible;

                // Song 1
                if (card.TopSongs.Count > 0)
                {
                    var s1 = card.TopSongs[0];
                    TopCardSong1.Visibility = Visibility.Visible;
                    TopCardSong1Title.Text = s1.Title ?? "";
                    TopCardSong1Sub.Text = s1.DisplaySubtitle ?? "";
                    if (!string.IsNullOrEmpty(s1.ThumbnailUrl))
                    {
                        var s1ThumbBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                        s1ThumbBmp.DecodePixelWidth = 80;
                        s1ThumbBmp.UriSource = new Uri(GetSquareThumbnail(s1.ThumbnailUrl), UriKind.Absolute);
                        TopCardSong1Thumb.ImageSource = s1ThumbBmp;
                    }
                }
                else TopCardSong1.Visibility = Visibility.Collapsed;

                // Song 2
                if (card.TopSongs.Count > 1)
                {
                    var s2 = card.TopSongs[1];
                    TopCardSong2.Visibility = Visibility.Visible;
                    TopCardSong2Title.Text = s2.Title ?? "";
                    TopCardSong2Sub.Text = s2.DisplaySubtitle ?? "";
                    if (!string.IsNullOrEmpty(s2.ThumbnailUrl))
                    {
                        var s2ThumbBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                        s2ThumbBmp.DecodePixelWidth = 80;
                        s2ThumbBmp.UriSource = new Uri(GetSquareThumbnail(s2.ThumbnailUrl), UriKind.Absolute);
                        TopCardSong2Thumb.ImageSource = s2ThumbBmp;
                    }
                }
                else TopCardSong2.Visibility = Visibility.Collapsed;

                // Song 3
                if (card.TopSongs.Count > 2)
                {
                    var s3 = card.TopSongs[2];
                    TopCardSong3.Visibility = Visibility.Visible;
                    TopCardSong3Title.Text = s3.Title ?? "";
                    TopCardSong3Sub.Text = s3.DisplaySubtitle ?? "";
                    if (!string.IsNullOrEmpty(s3.ThumbnailUrl))
                    {
                        var s3ThumbBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                        s3ThumbBmp.DecodePixelWidth = 80;
                        s3ThumbBmp.UriSource = new Uri(GetSquareThumbnail(s3.ThumbnailUrl), UriKind.Absolute);
                        TopCardSong3Thumb.ImageSource = s3ThumbBmp;
                    }
                }
                else TopCardSong3.Visibility = Visibility.Collapsed;
            }
            else
            {
                TopCardSongsPanel.Visibility = Visibility.Collapsed;
            }

            TopResultCard.Visibility = Visibility.Visible;
        }

        private void SearchChip_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            var chipParams = border.Tag as string;

            if (_activeSearchChipBorder == border)
            {
                // Deselect active chip (revert to All/unfiltered)
                SetChipVisualState(border, false);
                _activeSearchChipBorder = null;
                _currentSearchFilterParams = null;
            }
            else
            {
                if (_activeSearchChipBorder != null)
                {
                    SetChipVisualState(_activeSearchChipBorder, false);
                }
                _activeSearchChipBorder = border;
                _currentSearchFilterParams = chipParams;
                SetChipVisualState(border, true);
            }

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                _nextSearchToken = "";
                _isLoadingMoreSearch = false;
                ExecuteSearch(_currentSearchQuery);
            }
        }

        private void UpdateSearchChips(List<SearchChipItem> chips)
        {
            if (chips == null || chips.Count == 0 || SearchChipsPanel == null) return;

            if (SearchChipsPanel.Children.Count == chips.Count)
            {
                for (int i = 0; i < chips.Count; i++)
                {
                    var border = SearchChipsPanel.Children[i] as Border;
                    if (border != null)
                    {
                        border.Tag = chips[i].FilterParams;
                        var tb = border.Child as TextBlock;
                        if (tb != null) tb.Text = InnerTubeClient.NormalizeSearchChipTitle(chips[i].Title);

                        bool isSel = chips[i].IsSelected || (!string.IsNullOrEmpty(_currentSearchFilterParams) && chips[i].FilterParams == _currentSearchFilterParams);
                        if (isSel) _activeSearchChipBorder = border;
                        SetChipVisualState(border, isSel);
                    }
                }
            }
            else
            {
                SearchChipsPanel.Children.Clear();
                _activeSearchChipBorder = null;
                for (int i = 0; i < chips.Count; i++)
                {
                    var chip = chips[i];
                    var border = new Border
                    {
                        Background = _ytmChipInactiveBgBrush,
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(14, 6, 14, 6),
                        Margin = new Thickness(0, 0, (i == chips.Count - 1) ? 16 : 8, 0),
                        Tag = chip.FilterParams
                    };
                    var tb = new TextBlock
                    {
                        Text = InnerTubeClient.NormalizeSearchChipTitle(chip.Title),
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
                    border.Tapped += SearchChip_Tapped;

                    bool isSel = chip.IsSelected || (!string.IsNullOrEmpty(_currentSearchFilterParams) && chip.FilterParams == _currentSearchFilterParams);
                    if (isSel)
                    {
                        _activeSearchChipBorder = border;
                        SetChipVisualState(border, true);
                    }

                    SearchChipsPanel.Children.Add(border);
                }
            }
        }

        private void TopResultCard_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (_lastSearchCard == null) return;
            if (_lastSearchCard.ItemType == "artist" || (!string.IsNullOrEmpty(_lastSearchCard.BrowseId) && _lastSearchCard.BrowseId.StartsWith("UC")))
            {
                OpenArtistProfile(_lastSearchCard.BrowseId, _lastSearchCard.Title, true);
            }
            else if (_lastSearchCard.ItemType == "playlist" || (!string.IsNullOrEmpty(_lastSearchCard.BrowseId) && (_lastSearchCard.BrowseId.StartsWith("VL") || _lastSearchCard.BrowseId.StartsWith("PL"))))
            {
                OpenYouTubePlaylist(_lastSearchCard.BrowseId.Replace("VL", ""), _lastSearchCard.Title, _lastSearchCard.ThumbnailUrl);
            }
            else if (_lastSearchCard.ItemType == "album" || (!string.IsNullOrEmpty(_lastSearchCard.BrowseId) && _lastSearchCard.BrowseId.StartsWith("MPREb_")))
            {
                OpenYouTubePlaylist(_lastSearchCard.BrowseId, _lastSearchCard.Title, _lastSearchCard.ThumbnailUrl);
            }
            else if (!string.IsNullOrEmpty(_lastSearchCard.VideoId))
            {
                PlayTrack(new YouTubeTrack
                {
                    VideoId = _lastSearchCard.VideoId,
                    Title = _lastSearchCard.Title,
                    ChannelName = _lastSearchCard.Subtitle,
                    ThumbnailUrl = _lastSearchCard.ThumbnailUrl
                });
            }
        }

        private void TopCardChevron_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (e != null) e.Handled = true;
            TopResultCard_Tapped(sender, e);
        }

        private void TopCardChevron_Click(object sender, RoutedEventArgs e)
        {
            TopResultCard_Tapped(null, null);
        }

        private void TopResultShuffle_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (_lastSearchCard == null) return;
            if (!string.IsNullOrEmpty(_lastSearchCard.ShufflePlaylistId))
            {
                OpenYouTubePlaylist(_lastSearchCard.ShufflePlaylistId, _lastSearchCard.Title, _lastSearchCard.ThumbnailUrl);
            }
            else if (!string.IsNullOrEmpty(_lastSearchCard.BrowseId))
            {
                OpenArtistProfile(_lastSearchCard.BrowseId, _lastSearchCard.Title, true);
            }
        }

        private void TopResultMix_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (_lastSearchCard == null) return;
            if (!string.IsNullOrEmpty(_lastSearchCard.RadioPlaylistId))
            {
                OpenYouTubePlaylist(_lastSearchCard.RadioPlaylistId, _lastSearchCard.Title, _lastSearchCard.ThumbnailUrl);
            }
            else if (!string.IsNullOrEmpty(_lastSearchCard.VideoId))
            {
                OpenYouTubePlaylist("RDAMVM" + _lastSearchCard.VideoId, _lastSearchCard.Title, _lastSearchCard.ThumbnailUrl);
            }
        }

        private void TopCardSong1_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (_lastSearchCard?.TopSongs != null && _lastSearchCard.TopSongs.Count > 0)
                PlayTrack(_lastSearchCard.TopSongs[0]);
        }

        private void TopCardSong2_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (_lastSearchCard?.TopSongs != null && _lastSearchCard.TopSongs.Count > 1)
                PlayTrack(_lastSearchCard.TopSongs[1]);
        }

        private void TopCardSong3_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (_lastSearchCard?.TopSongs != null && _lastSearchCard.TopSongs.Count > 2)
                PlayTrack(_lastSearchCard.TopSongs[2]);
        }

        private void SearchSongList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var track = e.ClickedItem as YouTubeTrack;
            if (track == null) return;

            if (track.ItemType == "artist" || (track.VideoId != null && track.VideoId.StartsWith("CHANNEL:")))
            {
                string chId = track.ChannelId ?? (track.VideoId != null ? track.VideoId.Replace("CHANNEL:", "") : null);
                if (!string.IsNullOrEmpty(chId))
                    OpenArtistProfile(chId, track.Title, true);
                return;
            }

            if (track.ItemType == "playlist" || (track.VideoId != null && track.VideoId.StartsWith("PLAYLIST:")))
            {
                string pid = track.VideoId != null ? track.VideoId.Replace("PLAYLIST:", "") : null;
                if (!string.IsNullOrEmpty(pid))
                    OpenYouTubePlaylist(pid, track.Title, track.ThumbnailUrl);
                return;
            }

            if (track.ItemType == "album" || (track.VideoId != null && track.VideoId.StartsWith("ALBUM:")))
            {
                string aid = track.VideoId != null ? track.VideoId.Replace("ALBUM:", "") : null;
                if (!string.IsNullOrEmpty(aid))
                    OpenYouTubePlaylist(aid, track.Title, track.ThumbnailUrl);
                return;
            }

            PlayTrack(track);
        }

        // ==========================================
        // DISCOVER SECTION — Trending music from YouTube Music Explore
        // ==========================================
        private bool _discoverLoaded = false;

        private async void LoadDiscoverSection()
        {
            EnsureMoodsAndGenresLoaded();

            if (_discoverLoaded && DiscoverListView.Items != null && DiscoverListView.Items.Count > 0) return;
            
            try
            {
                var items = await InnerTubeClient.BrowseExploreAsync();

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (items != null && items.Count > 0)
                        DiscoverListView.ItemsSource = items;

                    _discoverLoaded = true;
                });
            }
            catch { }
        }

        // ==========================================
        // DYNAMIC MOODS & GENRES (YouTube Music / SimpMusic standard)
        // ==========================================
        private bool _moodsLoaded = false;
        private string _loadedMoodsLanguage = null;

        public async void EnsureMoodsAndGenresLoaded()
        {
            if (_moodsLoaded && SearchMoodSectionsControl != null && SearchMoodSectionsControl.ItemsSource != null && _loadedMoodsLanguage == InnerTubeClient.CurrentLanguage)
            {
                return;
            }

            try
            {
                var sections = await InnerTubeClient.BrowseMoodsAndGenresAsync();
                if (sections != null && sections.Count > 0)
                {
                    if (SearchMoodSectionsControl != null)
                    {
                        SearchMoodSectionsControl.ItemsSource = sections;
                    }
                    _moodsLoaded = true;
                    _loadedMoodsLanguage = InnerTubeClient.CurrentLanguage;

                    ResolveMoodCategoryArtworks(sections);
                }
            }
            catch { }
        }

        private async void ResolveMoodCategoryArtworks(List<MoodCategory> categories)
        {
            if (categories == null || categories.Count == 0) return;

            var missingItems = categories.SelectMany(c => c.Items)
                .Where(i => string.IsNullOrEmpty(i.ThumbnailUrl) && !string.IsNullOrEmpty(i.Params))
                .ToList();

            if (missingItems.Count == 0) return;

            await Task.Run(async () =>
            {
                var sem = new System.Threading.SemaphoreSlim(2, 2);
                var tasks = new List<Task>();

                foreach (var item in missingItems)
                {
                    var targetItem = item;
                    tasks.Add(Task.Run(async () =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            string thumb = await InnerTubeClient.GetMoodCategoryArtworkAsync(targetItem.Params);
                            if (!string.IsNullOrEmpty(thumb))
                            {
                                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                                {
                                    targetItem.ThumbnailUrl = thumb;
                                });
                            }
                        }
                        catch { }
                        finally
                        {
                            sem.Release();
                        }
                    }));
                }

                try { await Task.WhenAll(tasks); } catch { }
            });
        }

        private void MoodCategoryCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var grid = sender as Grid;
            if (grid != null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                grid.Clip = new Windows.UI.Xaml.Media.RectangleGeometry
                {
                    Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
                };
            }
        }

        private void MoodsSubGridView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var gv = sender as GridView;
            if (gv != null && e.NewSize.Width > 0)
            {
                var wrapGrid = gv.ItemsPanelRoot as ItemsWrapGrid;
                if (wrapGrid != null)
                {
                    double halfWidth = Math.Floor(e.NewSize.Width / 2.0);
                    if (halfWidth > 60)
                    {
                        wrapGrid.ItemWidth = halfWidth;
                        wrapGrid.ItemHeight = 92;
                    }
                }
            }
        }

        private async void MoodItem_Click(object sender, ItemClickEventArgs e)
        {
            string title = "";
            string browseId = "FEmusic_moods_and_genres_category";
            string paramsStr = "";

            var catItem = e.ClickedItem as MoodCategoryItem;
            if (catItem != null)
            {
                title = catItem.Title;
                paramsStr = catItem.Params;
                if (!string.IsNullOrEmpty(catItem.BrowseId)) browseId = catItem.BrowseId;
            }
            else
            {
                var moodItem = e.ClickedItem as MoodItem;
                if (moodItem != null)
                {
                    title = moodItem.Title;
                    paramsStr = moodItem.Params;
                    if (!string.IsNullOrEmpty(moodItem.BrowseId)) browseId = moodItem.BrowseId;
                }
                else
                {
                    return;
                }
            }

            MoodCategoryView.Visibility = Visibility.Visible;
            MoodCategoryTitle.Text = title;
            MoodCategoryLoading.Visibility = Visibility.Visible;
            MoodCategorySectionList.ItemsSource = null;

            try
            {
                var sections = await InnerTubeClient.BrowseMoodCategoryAsync(browseId, paramsStr);
                if (sections != null && sections.Count > 0)
                {
                    MoodCategorySectionList.ItemsSource = sections;
                }
            }
            catch { }
            
            MoodCategoryLoading.Visibility = Visibility.Collapsed;
        }

        private void CloseMoodCategory_Click(object sender, RoutedEventArgs e)
        {
            MoodCategoryView.Visibility = Visibility.Collapsed;
            MoodCategorySectionList.ItemsSource = null;
        }

        private void DiscoverItem_Click(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as DiscoverItem;
            if (item == null) return;

            if (!string.IsNullOrEmpty(item.VideoId))
            {
                // Open Shorts starting with this specific track
                OpenShortsWithTrack(new YouTubeTrack
                {
                    VideoId = item.VideoId,
                    Title = item.Title,
                    ChannelName = item.Subtitle,
                    ThumbnailUrl = item.ThumbnailUrl
                });
            }
            else if (!string.IsNullOrEmpty(item.PlaylistId))
            {
                OpenYouTubePlaylist(item.PlaylistId, item.Title, item.ThumbnailUrl);
            }
            else
            {
                OpenShortsView(0);
            }
        }

        private void UpdateDefaultSearchChips()
        {
            if (SearchTitleText != null)
            {
                SearchTitleText.Text = (InnerTubeClient.CurrentLanguage == "vi") ? "Tìm kiếm" : "Search";
            }
            if (SearchBox != null)
            {
                SearchBox.PlaceholderText = (InnerTubeClient.CurrentLanguage == "vi") ? "Bạn muốn nghe gì?" : "What do you want to listen to?";
            }

            if (SearchChipsPanel == null) return;

            string[] defaultTitlesVi = new[] { "Nghệ sĩ", "Bài hát", "Album", "Video", "Danh sách phát cộng đồng", "Tập", "Hồ sơ", "Podcast" };
            string[] defaultTitlesEn = new[] { "Artists", "Songs", "Albums", "Videos", "Community playlists", "Episodes", "Profiles", "Podcasts" };
            var titles = (InnerTubeClient.CurrentLanguage == "vi") ? defaultTitlesVi : defaultTitlesEn;

            int count = Math.Min(SearchChipsPanel.Children.Count, titles.Length);
            for (int i = 0; i < count; i++)
            {
                var border = SearchChipsPanel.Children[i] as Border;
                var tb = border != null ? border.Child as TextBlock : null;
                if (tb != null)
                {
                    tb.Text = titles[i];
                }
            }
        }
    }
}
