using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Playback;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using YTMusicWP.Models;
using YTMusicWP.Services;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private string _libraryFilter = "all";
        private string _librarySortMode = "activity";
        private ObservableCollection<LibraryItem> _libraryItems = new ObservableCollection<LibraryItem>();
        private bool _isViewingLikedSongs = false;
        private List<YouTubeTrack> _currentPlaylistFullTracks;
        private DispatcherTimer _playlistFilterTimer;
        private string _pendingExportM3u;

        private static readonly SolidColorBrush _libChipActiveTextBrush = new SolidColorBrush(Windows.UI.Colors.Black);

        private void LibChip_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            _libraryFilter = btn.Tag as string ?? "all";

            // Reset all chips to inactive (YouTube Music style)
            var chips = new[] { LibChipAll, LibChipPlaylists, LibChipSongs, LibChipArtists, LibChipDownloads };
            foreach (var chip in chips)
            {
                chip.Background = _ytmChipInactiveBgBrush;
                chip.Foreground = _ytmChipInactiveFgBrush;
            }

            // Set active chip (YouTube Music style)
            btn.Background = _ytmChipActiveBgBrush;
            btn.Foreground = _ytmChipActiveFgBrush;

            RefreshLibraryList();
        }

        private void RefreshLibraryList()
        {
            LibraryUnifiedList.ItemsSource = null;
            _libraryItems.Clear();

            bool showAll = _libraryFilter == "all";

            if (LibQuickSection != null)
            {
                LibQuickSection.Visibility = showAll ? Visibility.Visible : Visibility.Collapsed;
            }

            if (LibMostPlayedSection != null && !showAll)
            {
                LibMostPlayedSection.Visibility = Visibility.Collapsed;
            }

            if (LibSectionTitle != null)
            {
                switch (_libraryFilter)
                {
                    case "playlists":
                        LibSectionTitle.Text = "Playlists";
                        break;
                    case "songs":
                        LibSectionTitle.Text = "Songs";
                        break;
                    case "artists":
                        LibSectionTitle.Text = "Artists";
                        break;
                    case "downloads":
                        LibSectionTitle.Text = "Downloads";
                        break;
                    default:
                        LibSectionTitle.Text = (_librarySortMode == "activity") ? "Recent activity" :
                                              (_librarySortMode == "played") ? "Recently played" : "Recently added";
                        break;
                }
            }

            if (showAll)
            {
                var ignored = LoadMostPlayedShelfAsync();
            }

            // 1. Recently Played (if sort is "played")
            if (_librarySortMode == "played")
            {
                if (historyTracks.Count > 0 && (showAll || _libraryFilter == "playlists"))
                {
                    _libraryItems.Add(new LibraryItem
                    {
                        Title = "Recently Played",
                        Subtitle = "Playlist • " + historyTracks.Count + " songs",
                        IconGlyph = "🕐",
                        ThumbnailUrl = null,
                        IsCircle = false,
                        ItemType = "recent",
                        Tag = null
                    });
                }
            }

            // 2. Liked Songs
            if ((showAll || _libraryFilter == "playlists") && !YTMusicWP.InnerTubeClient.HasCookieAuth)
            {
                _libraryItems.Add(new LibraryItem
                {
                    Title = "Liked Songs",
                    Subtitle = "Playlist • " + favoriteTracks.Count + " songs",
                    IconGlyph = "♥",
                    ThumbnailUrl = null,
                    IsCircle = false,
                    ItemType = "favorites",
                    Tag = null
                });
            }

            // 3. Songs filter chip
            if (_libraryFilter == "songs")
            {
                var songs = new List<YouTubeTrack>();
                if (favoriteTracks != null) songs.AddRange(favoriteTracks);
                if (downloadedTracks != null)
                {
                    foreach (var d in downloadedTracks)
                    {
                        if (!songs.Any(s => s.VideoId == d.VideoId))
                            songs.Add(d);
                    }
                }

                if (_librarySortMode == "played" && historyTracks != null && historyTracks.Count > 0)
                {
                    var playedMap = new Dictionary<string, int>();
                    for (int i = 0; i < historyTracks.Count; i++)
                    {
                        var vid = historyTracks[i].VideoId;
                        if (!string.IsNullOrEmpty(vid) && !playedMap.ContainsKey(vid))
                        {
                            playedMap[vid] = i;
                        }
                    }
                    songs = songs.OrderBy(s => playedMap.ContainsKey(s.VideoId) ? playedMap[s.VideoId] : 9999).ToList();
                }

                foreach (var track in songs)
                {
                    _libraryItems.Add(new LibraryItem
                    {
                        Title = track.Title,
                        Subtitle = track.ChannelName,
                        ThumbnailUrl = track.ThumbnailUrl,
                        IconGlyph = "🎵",
                        IsCircle = false,
                        ItemType = "song",
                        Tag = track
                    });
                }
            }

            // 4. YT Playlists
            if (showAll || _libraryFilter == "playlists")
            {
                IEnumerable<YouTubePlaylistInfo> sortedPlaylists = _youtubeUserPlaylists;
                if (_librarySortMode == "played")
                {
                    sortedPlaylists = _youtubeUserPlaylists.OrderByDescending(p => p.PlaylistId == "LM" ? 1 : 0);
                }
                else
                {
                    sortedPlaylists = _youtubeUserPlaylists.OrderByDescending(p => p.PlaylistId == "LM" ? 1 : 0);
                }

                foreach (var ytpl in sortedPlaylists)
                {
                    _libraryItems.Add(new LibraryItem
                    {
                        Title = ytpl.Title,
                        Subtitle = "Playlist • " + ytpl.TrackCount + " tracks",
                        ThumbnailUrl = ytpl.ThumbnailUrl,
                        IconGlyph = null,
                        IsCircle = false,
                        ItemType = "ytplaylist",
                        Tag = ytpl
                    });
                }
            }

            // 5. Downloads
            if (showAll || _libraryFilter == "downloads")
            {
                if (downloadedTracks.Count > 0)
                {
                    _libraryItems.Add(new LibraryItem
                    {
                        Title = "Downloaded Songs",
                        Subtitle = "Playlist • " + downloadedTracks.Count + " songs",
                        IconGlyph = "⬇",
                        ThumbnailUrl = null,
                        IsCircle = false,
                        ItemType = "downloads",
                        Tag = null
                    });
                }
            }

            // 6. Subscriptions (artists)
            if (showAll || _libraryFilter == "artists")
            {
                IEnumerable<YouTubeSubscription> subs = _youtubeSubscriptions;
                if (_librarySortMode == "added")
                {
                    subs = _youtubeSubscriptions.Reverse();
                }

                foreach (var sub in subs)
                {
                    _libraryItems.Add(new LibraryItem
                    {
                        Title = sub.Title,
                        Subtitle = "Artist",
                        ThumbnailUrl = sub.ThumbnailUrl,
                        IconGlyph = null,
                        IsCircle = true,
                        ItemType = "artist",
                        Tag = sub
                    });
                }
            }

            // 7. Recently Played (if showAll and not already added at top)
            if (_librarySortMode != "played" && showAll)
            {
                if (historyTracks.Count > 0)
                {
                    _libraryItems.Add(new LibraryItem
                    {
                        Title = "Recently Played",
                        Subtitle = "Playlist • " + historyTracks.Count + " songs",
                        IconGlyph = "🕐",
                        ThumbnailUrl = null,
                        IsCircle = false,
                        ItemType = "recent",
                        Tag = null
                    });
                }
            }

            LibraryUnifiedList.ItemsSource = _libraryItems;
            LibraryEmptyState.Visibility = _libraryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetPlaylistViewTracks(IEnumerable<YouTubeTrack> tracks, string trackCountText = null)
        {
            _currentPlaylistFullTracks = tracks != null ? tracks.ToList() : new List<YouTubeTrack>();
            ResetPlaylistFilter();
            PlaylistSongsList.ItemsSource = tracks;
            if (!string.IsNullOrEmpty(trackCountText))
                PlaylistDetailsTrackCount.Text = trackCountText;
            else
                PlaylistDetailsTrackCount.Text = _currentPlaylistFullTracks.Count + " tracks";
        }

        private void ResetPlaylistFilter()
        {
            if (PlaylistFilterBox != null) PlaylistFilterBox.Text = "";
            if (PlaylistFilterContainer != null) PlaylistFilterContainer.Visibility = Visibility.Collapsed;
        }

        private void PlaylistSearchToggle_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistFilterContainer == null) return;
            if (PlaylistFilterContainer.Visibility == Visibility.Collapsed)
            {
                PlaylistFilterContainer.Visibility = Visibility.Visible;
                if (PlaylistFilterBox != null)
                {
                    PlaylistFilterBox.Focus(FocusState.Programmatic);
                }
            }
            else
            {
                PlaylistFilterClear_Click(null, null);
                PlaylistFilterContainer.Visibility = Visibility.Collapsed;
            }
        }

        private void PlaylistFilterClear_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistFilterBox != null)
            {
                PlaylistFilterBox.Text = "";
            }
            if (_currentPlaylistFullTracks != null)
            {
                PlaylistSongsList.ItemsSource = _currentPlaylistFullTracks;
                PlaylistDetailsTrackCount.Text = _currentPlaylistFullTracks.Count + " tracks";
            }
        }

        private void PlaylistFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (PlaylistFilterBox == null) return;

            if (_playlistFilterTimer == null)
            {
                _playlistFilterTimer = new DispatcherTimer();
                _playlistFilterTimer.Interval = TimeSpan.FromMilliseconds(300);
                _playlistFilterTimer.Tick += (s, args) =>
                {
                    _playlistFilterTimer.Stop();
                    ApplyPlaylistFilter();
                };
            }

            _playlistFilterTimer.Stop();
            if (string.IsNullOrEmpty(PlaylistFilterBox.Text))
            {
                ApplyPlaylistFilter();
            }
            else
            {
                _playlistFilterTimer.Start();
            }
        }

        private void ApplyPlaylistFilter()
        {
            if (PlaylistFilterBox == null) return;

            if (_currentPlaylistFullTracks == null || _currentPlaylistFullTracks.Count == 0)
            {
                var source = PlaylistSongsList.ItemsSource as IEnumerable<YouTubeTrack>;
                if (source != null)
                {
                    _currentPlaylistFullTracks = source.ToList();
                }
            }

            if (_currentPlaylistFullTracks == null) return;

            string query = PlaylistFilterBox.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                PlaylistSongsList.ItemsSource = _currentPlaylistFullTracks;
                PlaylistDetailsTrackCount.Text = _currentPlaylistFullTracks.Count + " tracks";
                return;
            }

            query = query.ToLowerInvariant();
            var filtered = _currentPlaylistFullTracks.Where(t =>
                (!string.IsNullOrEmpty(t.Title) && t.Title.ToLowerInvariant().Contains(query)) ||
                (!string.IsNullOrEmpty(t.ChannelName) && t.ChannelName.ToLowerInvariant().Contains(query))
            ).ToList();

            PlaylistSongsList.ItemsSource = filtered;
            PlaylistDetailsTrackCount.Text = string.Format("{0} of {1} tracks", filtered.Count, _currentPlaylistFullTracks.Count);
        }

        private void LibraryUnified_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as LibraryItem;
            if (item == null) return;

            switch (item.ItemType)
            {
                case "favorites":
                    OpenLikedSongsView();
                    break;

                case "playlist":
                    var pl = item.Tag as UserPlaylist;
                    _currentViewingPlaylist = pl;
                    _currentViewingYtPlaylistId = null;
                    _playlistContinuationToken = null;
                    _isViewingLikedSongs = false;
                    if (pl != null)
                    {
                        PlaylistDetailsTitle.Text = pl.Name;
                        if (pl.Tracks != null && pl.Tracks.Count > 0 && !string.IsNullOrEmpty(pl.Tracks[0].ThumbnailUrl))
                        {
                            var plCoverBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            plCoverBmp.DecodePixelWidth = 220;
                            plCoverBmp.UriSource = new Uri(GetSquareThumbnail(pl.Tracks[0].ThumbnailUrl), UriKind.Absolute);
                            PlaylistDetailsCoverBrush.ImageSource = plCoverBmp;
                            PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                        }
                        SetPlaylistViewTracks(pl.Tracks, (pl.Tracks != null ? pl.Tracks.Count : 0) + " tracks");
                        PlaylistDetailsView.Visibility = Visibility.Visible;
                        PlaylistSlideInStoryboard.Begin();
                    }
                    break;

                case "downloads":
                    _currentViewingPlaylist = null;
                    _currentViewingYtPlaylistId = null;
                    PlaylistDetailsTitle.Text = "Downloaded Songs";
                    PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                    SetPlaylistViewTracks(downloadedTracks, downloadedTracks.Count + " tracks");
                    PlaylistDetailsView.Visibility = Visibility.Visible;
                    PlaylistSlideInStoryboard.Begin();
                    break;

                case "recent":
                    _currentViewingPlaylist = null;
                    _currentViewingYtPlaylistId = null;
                    PlaylistDetailsTitle.Text = "Recently Played";
                    PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                    SetPlaylistViewTracks(historyTracks, historyTracks.Count + " tracks");
                    PlaylistDetailsView.Visibility = Visibility.Visible;
                    PlaylistSlideInStoryboard.Begin();
                    break;

                case "ytplaylist":
                    var ytpl = item.Tag as YouTubePlaylistInfo;
                    if (ytpl != null)
                        OpenYouTubePlaylist(ytpl.PlaylistId, ytpl.Title, ytpl.ThumbnailUrl);
                    break;

                case "artist":
                    var sub = item.Tag as YouTubeSubscription;
                    if (sub != null)
                        OpenArtistProfile(sub.ChannelId, sub.Title, true);
                    break;

                case "song":
                    var song = item.Tag as YouTubeTrack;
                    if (song != null)
                        PlayTrack(song);
                    break;
            }
        }

        private void LibSortOption_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuFlyoutItem;
            if (item == null) return;
            string tag = item.Tag as string ?? "activity";
            _librarySortMode = tag;

            if (LibSortLabel != null)
            {
                switch (tag)
                {
                    case "added":
                        LibSortLabel.Text = "Recently added";
                        break;
                    case "played":
                        LibSortLabel.Text = "Recently played";
                        break;
                    default:
                        LibSortLabel.Text = "Recent activity";
                        break;
                }
            }

            if (LibSortItemActivity != null) LibSortItemActivity.Text = (tag == "activity" ? "✓ " : "  ") + "Recent activity";
            if (LibSortItemAdded != null) LibSortItemAdded.Text = (tag == "added" ? "✓ " : "  ") + "Recently added";
            if (LibSortItemPlayed != null) LibSortItemPlayed.Text = (tag == "played" ? "✓ " : "  ") + "Recently played";

            RefreshLibraryList();
        }

        private void LibTileFavorite_Click(object sender, RoutedEventArgs e)
        {
            OpenLikedSongsView();
        }

        private void LibTileFollowed_Click(object sender, RoutedEventArgs e)
        {
            LibChip_Click(LibChipArtists, null);
        }

        private async void LibTileMostPlayed_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mostPlayed = await Services.DatabaseHelper.GetMostPlayedAsync(50);
                if (mostPlayed != null && mostPlayed.Count > 0)
                {
                    _currentViewingPlaylist = null;
                    _currentViewingYtPlaylistId = null;
                    _playlistContinuationToken = null;
                    _isViewingLikedSongs = false;
                    PlaylistDetailsTitle.Text = "Most Played";
                    PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                    SetPlaylistViewTracks(mostPlayed, mostPlayed.Count + " tracks");
                    PlaylistDetailsView.Visibility = Visibility.Visible;
                    PlaylistSlideInStoryboard.Begin();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LibTileMostPlayed_Click error: " + ex.Message);
            }
        }

        private void LibTileDownloaded_Click(object sender, RoutedEventArgs e)
        {
            _currentViewingPlaylist = null;
            _currentViewingYtPlaylistId = null;
            _playlistContinuationToken = null;
            _isViewingLikedSongs = false;
            PlaylistDetailsTitle.Text = "Downloaded Songs";
            PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
            SetPlaylistViewTracks(downloadedTracks, downloadedTracks.Count + " tracks");
            PlaylistDetailsView.Visibility = Visibility.Visible;
            PlaylistSlideInStoryboard.Begin();
        }

        private async Task LoadMostPlayedShelfAsync()
        {
            if (LibMostPlayedSection == null || LibMostPlayedCarousel == null) return;
            try
            {
                var mostPlayed = await Services.DatabaseHelper.GetMostPlayedAsync(10);
                if (mostPlayed != null && mostPlayed.Count > 0)
                {
                    LibMostPlayedCarousel.ItemsSource = mostPlayed;
                    if (_libraryFilter == "all")
                    {
                        LibMostPlayedSection.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    LibMostPlayedSection.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadMostPlayedShelfAsync Error: " + ex.Message);
                LibMostPlayedSection.Visibility = Visibility.Collapsed;
            }
        }

        public void OpenLikedSongsView()
        {
            _currentViewingPlaylist = null;
            _currentViewingYtPlaylistId = null;
            _isViewingLikedSongs = true;
            PlaylistDetailsTitle.Text = "Liked Songs";
            if (favoriteTracks.Count > 0 && !string.IsNullOrEmpty(favoriteTracks[0].ThumbnailUrl))
            {
                var likedCoverBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                likedCoverBmp.DecodePixelWidth = 220;
                likedCoverBmp.UriSource = new Uri(GetSquareThumbnail(favoriteTracks[0].ThumbnailUrl), UriKind.Absolute);
                PlaylistDetailsCoverBrush.ImageSource = likedCoverBmp;
                PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
            }
            else
            {
                PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
            }
            SetPlaylistViewTracks(favoriteTracks, favoriteTracks.Count + (HasMoreLikedSongs ? "+" : "") + " songs");
            PlaylistDetailsView.Visibility = Visibility.Visible;
            PlaylistSlideInStoryboard.Begin();
            HookPlaylistSongsScroll();
        }

        public void OpenUserPlaylist(UserPlaylist pl)
        {
            if (pl == null) return;
            _currentViewingPlaylist = pl;
            _currentViewingYtPlaylistId = null;
            _playlistContinuationToken = null;
            _isViewingLikedSongs = false;
            PlaylistDetailsTitle.Text = pl.Name;
            if (pl.Tracks != null && pl.Tracks.Count > 0 && !string.IsNullOrEmpty(pl.Tracks[0].ThumbnailUrl))
            {
                var plCoverBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                plCoverBmp.DecodePixelWidth = 220;
                plCoverBmp.UriSource = new Uri(GetSquareThumbnail(pl.Tracks[0].ThumbnailUrl), UriKind.Absolute);
                PlaylistDetailsCoverBrush.ImageSource = plCoverBmp;
                PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
            }
            else
            {
                PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
            }
            SetPlaylistViewTracks(pl.Tracks, (pl.Tracks != null ? pl.Tracks.Count : 0) + " tracks");
            PlaylistDetailsView.Visibility = Visibility.Visible;
            PlaylistSlideInStoryboard.Begin();
        }

        private async void PlaylistPinToStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string title = PlaylistDetailsTitle.Text ?? "Playlist";
                string tileId;
                string args;
                List<YouTubeTrack> tracks = null;

                if (_isViewingLikedSongs || title == "Liked Songs")
                {
                    tileId = "ytmusic_favs";
                    args = "playlist:liked";
                    tracks = favoriteTracks.ToList();
                }
                else if (_currentViewingPlaylist != null)
                {
                    tileId = "ytmusic_pl_" + _currentViewingPlaylist.Name.Replace(" ", "_");
                    args = "playlist:" + _currentViewingPlaylist.Name;
                    tracks = _currentViewingPlaylist.Tracks != null ? _currentViewingPlaylist.Tracks.ToList() : new List<YouTubeTrack>();
                }
                else if (!string.IsNullOrEmpty(_currentViewingYtPlaylistId))
                {
                    tileId = "ytmusic_ytpl_" + _currentViewingYtPlaylistId;
                    args = "ytplaylist:" + _currentViewingYtPlaylistId;
                    var list = PlaylistSongsList.ItemsSource as IEnumerable<YouTubeTrack>;
                    if (list != null) tracks = list.ToList();
                }
                else
                {
                    tileId = "ytmusic_pl_" + title.Replace(" ", "_");
                    args = "playlist:" + title;
                    var list = PlaylistSongsList.ItemsSource as IEnumerable<YouTubeTrack>;
                    if (list != null) tracks = list.ToList();
                }

                if (YTMusicWP.Services.TileService.IsSecondaryTilePinned(tileId))
                {
                    bool unpinned = await YTMusicWP.Services.TileService.UnpinSecondaryTileAsync(tileId);
                    if (unpinned) ShowToast("Unpinned from Start");
                }
                else
                {
                    bool pinned = await YTMusicWP.Services.TileService.PinSecondaryTileAsync(tileId, title, args, tracks);
                    if (pinned) ShowToast("📌 Pinned to Start Screen!");
                }
            }
            catch { }
        }

        private void CancelCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            CreatePlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private async void ConfirmCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            string name = NewPlaylistNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            CreatePlaylistDialog.Visibility = Visibility.Collapsed;

            try
            {
                string plId = await CreateYouTubePlaylistAsync(name);
                _youtubeUserPlaylists.Add(new YouTubePlaylistInfo
                {
                    PlaylistId = plId,
                    Title = name,
                    TrackCount = 0,
                    ThumbnailUrl = ""
                });
                SaveYouTubePlaylistsCacheAsync();
                RefreshLibraryList();
                ShowToast("Playlist created!");
            }
            catch (Exception ex)
            {
                ShowToast("Failed to create playlist: " + ex.Message);
            }
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
            // This handler is kept for compatibility but now unused for YT playlists
            ShowToast("Use YouTube to manage playlists");
        }

        private async void MenuDeletePlaylistInside_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewingYtPlaylistId != null)
            {
                try
                {
                    ShowToast("Deleting playlist...");
                    bool success = await DeleteYouTubePlaylistAsync(_currentViewingYtPlaylistId);
                    if (success)
                    {
                        var pl = _youtubeUserPlaylists.FirstOrDefault(p => p.PlaylistId == _currentViewingYtPlaylistId);
                        if (pl != null) _youtubeUserPlaylists.Remove(pl);
                        RefreshLibraryList();
                        ShowToast("Playlist deleted!");
                        PlaylistSlideOutStoryboard.Begin();
                    }
                    else
                    {
                        ShowToast("Failed to delete playlist");
                    }
                }
                catch (Exception ex)
                {
                    ShowToast("Error deleting playlist: " + ex.Message);
                }
            }
        }

        private async void MenuRemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null && _currentViewingYtPlaylistId != null)
            {
                try
                {
                    bool success = await RemoveFromYouTubePlaylistAsync(_currentViewingYtPlaylistId, track.VideoId, track.SetVideoId);
                    if (success)
                    {
                        // Remove from local cache for local playlists
                        if (_currentViewingYtPlaylistId.StartsWith("LOCAL_"))
                        {
                            var localTracks = await LoadLocalPlaylistTracksAsync(_currentViewingYtPlaylistId);
                            localTracks.RemoveAll(t => t.VideoId == track.VideoId);
                            await SaveLocalPlaylistTracksAsync(_currentViewingYtPlaylistId, localTracks);
                            var pl = _youtubeUserPlaylists.FirstOrDefault(p => p.PlaylistId == _currentViewingYtPlaylistId);
                            if (pl != null) { pl.TrackCount = localTracks.Count; SaveYouTubePlaylistsCacheAsync(); }
                        }
                        var ytTracks = PlaylistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>;
                        if (ytTracks != null) ytTracks.Remove(track);
                        ShowToast("Removed from playlist");
                    }
                    else
                    {
                        ShowToast("Failed to remove");
                    }
                }
                catch (Exception ex)
                {
                    ShowToast("Error removing track: " + ex.Message);
                }
            }
        }

        private void PlaylistsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            _currentViewingPlaylist = e.ClickedItem as UserPlaylist;
            if (_currentViewingPlaylist != null)
            {
                PlaylistDetailsTitle.Text = _currentViewingPlaylist.Name;
                if (_currentViewingPlaylist.Tracks != null && _currentViewingPlaylist.Tracks.Count > 0 && !string.IsNullOrEmpty(_currentViewingPlaylist.Tracks[0].ThumbnailUrl))
                {
                    var plCoverBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    plCoverBmp.DecodePixelWidth = 220;
                    plCoverBmp.UriSource = new Uri(GetSquareThumbnail(_currentViewingPlaylist.Tracks[0].ThumbnailUrl), UriKind.Absolute);
                    PlaylistDetailsCoverBrush.ImageSource = plCoverBmp;
                    PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                }
                else
                {
                    PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                }
                PlaylistSongsList.ItemsSource = _currentViewingPlaylist.Tracks;
                PlaylistDetailsTrackCount.Text = (_currentViewingPlaylist.Tracks != null ? _currentViewingPlaylist.Tracks.Count : 0) + " tracks";
                PlaylistDetailsView.Visibility = Visibility.Visible;
                PlaylistSlideInStoryboard.Begin();
            }
        }

        private void ClosePlaylistDetails_Click(object sender, RoutedEventArgs e)
        {
            PlaylistSlideOutStoryboard.Begin();
        }

        private void PlaylistSlideOutStoryboard_Completed(object sender, object e)
        {
            PlaylistDetailsView.Visibility = Visibility.Collapsed;
            PlaylistDetailsCoverBrush.ImageSource = null;
            PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
            _isViewingLikedSongs = false;
            ResetPlaylistFilter();
            _currentPlaylistFullTracks = null;
        }

        private void PlayAllPlaylist_Click(object sender, RoutedEventArgs e)
        {
            // If filtered or active in list, play first track of displayed items
            var source = PlaylistSongsList.ItemsSource as System.Collections.IEnumerable;
            if (source != null)
            {
                var firstTrack = source.Cast<object>().FirstOrDefault() as YouTubeTrack;
                if (firstTrack != null)
                {
                    PlayTrack(firstTrack);
                    return;
                }
            }

            // Fallback: _currentViewingPlaylist
            if (_currentViewingPlaylist != null && _currentViewingPlaylist.Tracks.Count > 0)
            {
                PlayTrack(_currentViewingPlaylist.Tracks[0]);
                return;
            }

            ShowToast("Playlist is empty!");
        }

        private void MenuAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            _trackPendingForPlaylist = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (_trackPendingForPlaylist == null) return;

            DialogPlaylistList.ItemsSource = _youtubeUserPlaylists;
            AddToPlaylistDialog.Visibility = Visibility.Visible;
        }

        private void CancelAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            _trackPendingForPlaylist = null;
            AddToPlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private async void DialogPlaylistList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var ytPlaylist = e.ClickedItem as YouTubePlaylistInfo;
            var track = _trackPendingForPlaylist;
            _trackPendingForPlaylist = null;
            AddToPlaylistDialog.Visibility = Visibility.Collapsed;

            if (ytPlaylist != null && track != null)
            {
                try
                {
                    ShowToast("Adding to " + ytPlaylist.Title + "...");

                    bool success = (await AddToYouTubePlaylistAsync(ytPlaylist.PlaylistId, track.VideoId)) != null;
                    if (success)
                    {
                        // Save track to local cache for local playlists
                        if (ytPlaylist.PlaylistId.StartsWith("LOCAL_"))
                        {
                            await AddTrackToLocalPlaylistAsync(ytPlaylist.PlaylistId, track);
                            // Use first track's thumbnail as playlist cover
                            if (string.IsNullOrEmpty(ytPlaylist.ThumbnailUrl) && !string.IsNullOrEmpty(track.ThumbnailUrl))
                                ytPlaylist.ThumbnailUrl = track.ThumbnailUrl;
                        }
                        ytPlaylist.TrackCount++;
                        SaveYouTubePlaylistsCacheAsync();
                        ShowToast("Added to " + ytPlaylist.Title);
                    }
                    else
                    {
                        ShowToast("Failed to add to playlist");
                    }
                }
                catch (Exception ex)
                {
                    ShowToast("Error adding to playlist: " + ex.Message);
                }
            }
        }

        private void MenuShare_Click(object sender, RoutedEventArgs e)
        {
            _trackToShare = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (_trackToShare != null)
            {
                DataTransferManager.ShowShareUI();
            }
        }

        private void MainPage_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (!string.IsNullOrEmpty(_shareRoomCode))
            {
                args.Request.Data.Properties.Title = "Join my music room";
                args.Request.Data.SetText("Join my music room with code " + _shareRoomCode + " on YTMusicWP, SimpMusic or Metrolist!");
                _shareRoomCode = null;
                return;
            }

            if (LiveDebugDialog != null && LiveDebugDialog.Visibility == Visibility.Visible)
            {
                args.Request.Data.Properties.Title = "Live Stream Logs - YTMusicWP";
                args.Request.Data.SetText(LiveDebugTextBox != null ? (LiveDebugTextBox.Text ?? "") : "");
                return;
            }

            if (_trackToShare != null)
            {
                if (_trackToShare.VideoId.StartsWith("LOCAL:"))
                {
                    args.Request.Data.Properties.Title = "YTMusicWP";
                    args.Request.Data.Properties.Description = _trackToShare.Title;
                    args.Request.Data.SetText("🎵 " + _trackToShare.Title + " — " + _trackToShare.ChannelName);
                }
                else
                {
                    args.Request.Data.Properties.Title = "YTMusicWP - Share Music";
                    args.Request.Data.Properties.Description = "Listen to " + _trackToShare.Title;
                    string url = "https://www.youtube.com/watch?v=" + _trackToShare.VideoId;
                    args.Request.Data.SetWebLink(new Uri(url));
                }
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
            // Stop existing timer BEFORE changing mode to prevent stale tick
            _sleepTimer.Stop();

            _sleepTimerMode++;
            if (_sleepTimerMode > 3) _sleepTimerMode = 0;

            if (_sleepTimerMode == 0)
            {
                _sleepMinutesLeft = 0;
                MenuSleepTimerStatus.Text = "Off";
                ShowToast("Sleep Timer: Off");
                try { BackgroundMediaPlayer.SendMessageToBackground(new Windows.Foundation.Collections.ValueSet { { "SetSleepTimer", 0 } }); } catch { }
            }
            else
            {
                if (_sleepTimerMode == 1) _sleepMinutesLeft = 15;
                else if (_sleepTimerMode == 2) _sleepMinutesLeft = 30;
                else if (_sleepTimerMode == 3) _sleepMinutesLeft = 60;

                MenuSleepTimerStatus.Text = _sleepMinutesLeft + " min left";
                _sleepTimer.Start();
                ShowToast("Sleep Timer set for " + _sleepMinutesLeft + " minutes");
                try { BackgroundMediaPlayer.SendMessageToBackground(new Windows.Foundation.Collections.ValueSet { { "SetSleepTimer", _sleepMinutesLeft } }); } catch { }
            }
        }

        private async void ClearRecentHistory_Click(object sender, RoutedEventArgs e)
        {
            historyTracks.Clear();
            historyQuickGridTracks.Clear();
            homeHistoryCarouselTracks.Clear();
            HomeHistorySection.Visibility = Visibility.Collapsed;
            await SaveHistoryAsyncTask();
            await YTMusicWP.Services.DatabaseHelper.ClearHistoryAsync();
            await UpdateStorageDisplayAsync();
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
                    if (!string.IsNullOrEmpty(t.ChannelId)) obj["ChannelId"] = t.ChannelId;
                    array.Add(obj);
                }
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("history.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, array.ToString());
            }
            catch { }
        }

        private async void SaveFavoritesAsync()
        {
            try
            {
                var snapshot = favoriteTracks.ToList();
                JArray array = new JArray();
                foreach (var t in snapshot)
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

        private void MenuPlay_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null) PlayTrack(track);
        }

        private void MenuAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track == null) return;

            _bottomSheetTrack = track;
            BottomSheetAddToQueue_Click(null, null);
        }

        private void MenuGoToRadio_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track == null) return;

            _bottomSheetTrack = track;
            BottomSheetGoToRadio_Click(null, null);
        }

        private async void MenuDownload_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null) await DownloadTrackAsync(track);
        }

        private async void MenuFavorite_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track == null) return;

            // Require login to like songs
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth)
            {
                ShowToast("Sign in to like songs");
                return;
            }

            var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
            bool isAdding = (existing == null);
            if (existing != null)
            {
                favoriteTracks.Remove(existing);
                var _ = YTMusicWP.Services.DatabaseHelper.RemoveFavoriteAsync(track.VideoId);
            }
            else
            {
                favoriteTracks.Insert(0, track);
                var _ = YTMusicWP.Services.DatabaseHelper.AddFavoriteAsync(track);
            }
            SaveFavoritesAsync();
            ShowToast(isAdding ? "Added to Favorites" : "Removed from Favorites");

            if (currentTrack != null && currentTrack.VideoId == track.VideoId)
            {
                BigHeartBtn.Content = isAdding ? "♥" : "♡";
                BigHeartBtn.Foreground = isAdding ? _greenBrush : _whiteBrush;
            }

            // Sync to YouTube (skip LOCAL tracks that can't be rated)
            if (!track.VideoId.StartsWith("LOCAL:"))
            {
                string rating = isAdding ? "like" : "none";
                await RateVideoAsync(track.VideoId, rating);
            }
        }

        private async void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null && track.VideoId.StartsWith("LOCAL:"))
            {
                try
                {
                    string fileName = track.VideoId.Substring(6);
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(fileName);
                    await file.DeleteAsync();

                    string thumbName = "thumb_" + System.IO.Path.GetFileNameWithoutExtension(fileName) + ".jpg";
                    try
                    {
                        var thumbFile = await ApplicationData.Current.LocalFolder.GetFileAsync(thumbName);
                        if (thumbFile != null) await thumbFile.DeleteAsync();
                    }
                    catch { }

                    await YTMusicWP.Services.DatabaseHelper.RemoveDownloadedAsync(fileName);

                    downloadedTracks.Remove(track);

                    var fav = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                    if (fav != null) 
                    { 
                        favoriteTracks.Remove(fav); 
                        SaveFavoritesAsync(); 
                        var _ = YTMusicWP.Services.DatabaseHelper.RemoveFavoriteAsync(track.VideoId);
                    }

                    var hist = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                    if (hist != null)
                    {
                        historyTracks.Remove(hist);
                        var ignoredHist = SaveHistoryAsyncTask();
                        var _ = YTMusicWP.Services.DatabaseHelper.RemoveHistoryAsync(track.VideoId);
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

        private async void MenuExportToMusic_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track == null || string.IsNullOrEmpty(track.VideoId) || !track.VideoId.StartsWith("LOCAL:")) return;

            string fileName = track.VideoId.Substring(6);
            try
            {
                var localFile = await ApplicationData.Current.LocalFolder.GetFileAsync(fileName);
                var musicFolder = KnownFolders.MusicLibrary;
                await localFile.CopyAsync(musicFolder, fileName, NameCollisionOption.ReplaceExisting);
                ShowToast("Exported to Music folder: " + track.Title);
            }
            catch (Exception ex)
            {
                ShowToast("Failed to export: " + ex.Message);
            }
        }

        private async void ExportAllToMusicFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var files = await ApplicationData.Current.LocalFolder.GetFilesAsync();
                var musicFolder = KnownFolders.MusicLibrary;
                int count = 0;
                foreach (var file in files)
                {
                    if (file.Name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) && !file.Name.StartsWith("temp_play_"))
                    {
                        try
                        {
                            await file.CopyAsync(musicFolder, file.Name, NameCollisionOption.ReplaceExisting);
                            count++;
                        }
                        catch { }
                    }
                }
                ShowToast(count > 0 ? "Exported " + count + " song(s) to Music folder" : "No downloaded songs found");
            }
            catch (Exception ex)
            {
                ShowToast("Export failed: " + ex.Message);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e) { if (currentTrack != null) await DownloadTrackAsync(currentTrack); }

        public async Task CleanStaleDownloadsAsync()
        {
            try
            {
                var downloads = await Windows.Networking.BackgroundTransfer.BackgroundDownloader.GetCurrentDownloadsAsync();
                foreach (var download in downloads)
                {
                    try
                    {
                        var resFile = download.ResultFile;
                        download.AttachAsync().Cancel();
                        if (resFile != null)
                        {
                            var props = await resFile.GetBasicPropertiesAsync();
                            if (props.Size == 0)
                            {
                                await resFile.DeleteAsync();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task DownloadTrackAsync(YouTubeTrack track, bool isSilent = false)
        {
            if (track == null || string.IsNullOrEmpty(track.VideoId) || track.VideoId.StartsWith("LOCAL:")) return;
            if (!IsInternetAvailable())
            {
                if (!isSilent) ShowToast("Internet required to download");
                return;
            }

            StorageFile destinationFile = null;
            try
            {
                if (!isSilent)
                {
                    DownloadStatusBar.Visibility = Visibility.Visible;
                    DownloadStatusText.Text = "Resolving: " + track.Title;
                    DownloadProgressBar.Value = 0;
                    DownloadProgressBar.IsIndeterminate = true;
                }

                // Dùng InnerTube để lấy stream URL thay vì proxy (proxy đã bị chặn)
                string streamUrl = await InnerTubeClient.ResolveStreamUrlAsync(track.VideoId);
                if (string.IsNullOrEmpty(streamUrl))
                {
                    if (!isSilent)
                    {
                        DownloadStatusBar.Visibility = Visibility.Collapsed;
                        ShowToast("Cannot resolve audio URL for this track");
                    }
                    return;
                }

                string safeTitle = string.Join("", track.Title.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrEmpty(safeTitle)) safeTitle = track.VideoId;
                destinationFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(safeTitle + ".m4a", CreationCollisionOption.ReplaceExisting);

                BackgroundDownloader downloader = new BackgroundDownloader();
                DownloadOperation download = downloader.CreateDownload(new Uri(streamUrl), destinationFile);

                if (!isSilent)
                {
                    DownloadStatusText.Text = "Downloading: " + track.Title;
                }

                var progressCallback = new Progress<DownloadOperation>(op =>
                {
                    if (!isSilent && op.Progress.TotalBytesToReceive > 0)
                    {
                        DownloadProgressBar.IsIndeterminate = false;
                        double progress = (double)op.Progress.BytesReceived / op.Progress.TotalBytesToReceive * 100;
                        DownloadProgressBar.Value = progress;
                        DownloadStatusText.Text = "Downloading: " + track.Title + " (" + (int)progress + "%)";
                    }
                });

                await download.StartAsync().AsTask(progressCallback);

                if (!isSilent)
                {
                    DownloadProgressBar.IsIndeterminate = true;
                    DownloadStatusText.Text = "Saving tags & artwork: " + track.Title;
                }

                // 1. Download & persist cover artwork offline
                byte[] coverBytes = null;
                string localThumbUri = null;
                if (!string.IsNullOrEmpty(track.ThumbnailUrl))
                {
                    try
                    {
                        string cleanUrl = GetSquareThumbnail(track.ThumbnailUrl);
                        coverBytes = await _apiClient.GetByteArrayAsync(cleanUrl);
                        if (coverBytes != null && coverBytes.Length > 0)
                        {
                            string thumbFileName = "thumb_" + safeTitle + ".jpg";
                            var thumbFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(thumbFileName, CreationCollisionOption.ReplaceExisting);
                            await FileIO.WriteBytesAsync(thumbFile, coverBytes);
                            localThumbUri = "ms-appdata:///local/" + thumbFileName;
                        }
                    }
                    catch { }
                }

                // 2. Tag M4A metadata (Title, Artist, Album, Cover Art)
                try
                {
                    await Services.M4aMetadataWriter.WriteMetadataAsync(destinationFile, track.Title, track.ChannelName, track.AlbumName, coverBytes);
                }
                catch { }

                // 3. Persist to SQLite DownloadedEntity
                await Services.DatabaseHelper.AddOrUpdateDownloadedAsync(destinationFile.Name, track, localThumbUri);

                if (!isSilent)
                {
                    DownloadProgressBar.IsIndeterminate = false;
                    DownloadProgressBar.Value = 100;
                    DownloadStatusText.Text = "Download complete: " + track.Title;
                }

                await LoadDownloadsAsync();

                if (!isSilent)
                {
                    await Task.Delay(3000);
                    DownloadStatusBar.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                try { if (destinationFile != null) await destinationFile.DeleteAsync(); } catch { }
                if (!isSilent)
                {
                    DownloadStatusBar.Visibility = Visibility.Collapsed;
                    ShowToast("Download failed or cancelled.");
                }
            }
        }

        private bool IsWifiConnected()
        {
            try
            {
                var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                if (profile != null && profile.GetNetworkConnectivityLevel() == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess)
                {
                    if (profile.IsWlanConnectionProfile) return true;
                    if (profile.NetworkAdapter != null && profile.NetworkAdapter.IanaInterfaceType == 71) return true;
                }
            }
            catch { }
            return false;
        }

        private bool _isSmartDownloading = false;

        public async Task TriggerSmartDownloadsAsync()
        {
            if (_isSmartDownloading) return;

            var settings = ApplicationData.Current.LocalSettings.Values;
            bool enabled = SafeGetBool(settings, "SmartDownloads", false);
            if (!enabled || !IsWifiConnected()) return;

            _isSmartDownloading = true;
            try
            {
                List<YouTubeTrack> favsToDownload;
                lock (favoriteTracks)
                {
                    favsToDownload = favoriteTracks.Where(t => t != null && !string.IsNullOrEmpty(t.VideoId) && !t.VideoId.StartsWith("LOCAL:")).ToList();
                }

                if (favsToDownload.Count == 0) return;

                var downloadedMap = await Services.DatabaseHelper.GetDownloadedMapAsync();
                var downloadedIds = new HashSet<string>(downloadedMap.Values.Select(v => v.VideoId).Where(id => !string.IsNullOrEmpty(id)));

                int downloadedCount = 0;
                foreach (var track in favsToDownload)
                {
                    if (!SafeGetBool(settings, "SmartDownloads", false) || !IsWifiConnected())
                        break;

                    if (downloadedIds.Contains(track.VideoId)) continue;

                    string safeTitle = string.Join("", track.Title.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
                    if (string.IsNullOrEmpty(safeTitle)) safeTitle = track.VideoId;
                    string targetFileName = safeTitle + ".m4a";

                    if (downloadedMap.ContainsKey(targetFileName))
                    {
                        downloadedIds.Add(track.VideoId);
                        continue;
                    }

                    await DownloadTrackAsync(track, isSilent: true);
                    downloadedIds.Add(track.VideoId);
                    downloadedCount++;

                    await Task.Delay(1500);

                    if (downloadedCount >= 25) break;
                }

                if (downloadedCount > 0)
                {
                    ShowToast("Smart Downloads: " + downloadedCount + " new song(s) downloaded");
                }
            }
            catch { }
            finally
            {
                _isSmartDownloading = false;
            }
        }

        // ══════════════════════════════════════════
        // YouTube Playlists & Subscriptions click
        // ══════════════════════════════════════════
        private void YouTubePlaylist_ItemClick(object sender, ItemClickEventArgs e)
        {
            var info = e.ClickedItem as YouTubePlaylistInfo;
            if (info != null && !string.IsNullOrEmpty(info.PlaylistId))
            {
                OpenYouTubePlaylist(info.PlaylistId, info.Title, info.ThumbnailUrl);
            }
        }

        private void Subscription_ItemClick(object sender, ItemClickEventArgs e)
        {
            var sub = e.ClickedItem as YouTubeSubscription;
            if (sub != null && !string.IsNullOrEmpty(sub.ChannelId))
            {
                OpenArtistProfile(sub.ChannelId, sub.Title, true);
            }
        }

        // ══════════════════════════════════════════
        // ENHANCE PLAYLIST — Add similar songs
        // ══════════════════════════════════════════
        private async void EnhancePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewingPlaylist == null || _currentViewingPlaylist.Tracks.Count == 0)
            {
                ShowToast("Add some songs first!");
                return;
            }

            ShowToast("✨ Enhancing playlist...");

            try
            {
                // Pick a random track from playlist as seed
                var random = new Random();
                var seedTrack = _currentViewingPlaylist.Tracks[random.Next(_currentViewingPlaylist.Tracks.Count)];

                // Search for similar songs with Music filter
                string query = seedTrack.Title + " " + seedTrack.ChannelName;
                var results = await InnerTubeClient.SearchAsync(query, 15, "EgWKAQIIAWoKEAMQBBAKEAkQBQ%3D%3D");

                if (results == null || results.Count == 0)
                {
                    // Fallback without filter
                    results = await InnerTubeClient.SearchAsync(query, 15);
                }

                if (results != null && results.Count > 0)
                {
                    int added = 0;
                    var existingIds = new System.Collections.Generic.HashSet<string>();
                    foreach (var t in _currentViewingPlaylist.Tracks) existingIds.Add(t.VideoId);

                    foreach (var t in results)
                    {
                        if (t.VideoId.StartsWith("CHANNEL:") || t.VideoId.StartsWith("PLAYLIST:")) continue;
                        if (existingIds.Contains(t.VideoId)) continue;

                        _currentViewingPlaylist.Tracks.Add(t);
                        existingIds.Add(t.VideoId);
                        added++;
                        if (added >= 5) break; // Add up to 5 songs
                    }

                    if (added > 0)
                    {
                        PlaylistDetailsTrackCount.Text = _currentViewingPlaylist.Tracks.Count + " tracks";
                        ShowToast("✨ Added " + added + " songs!");
                    }
                    else
                    {
                        ShowToast("No new songs found to add");
                    }
                }
                else
                {
                    ShowToast("No similar songs found");
                }
            }
            catch
            {
                ShowToast("Enhance failed");
            }
        }

        private async void LibrarySync_Click(object sender, RoutedEventArgs e)
        {
            LibrarySyncBtn.IsEnabled = false;
            try
            {
                string accessToken = await GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(accessToken) || YTMusicWP.InnerTubeClient.HasCookieAuth)
                {
                    ShowToast("Syncing...");
                    await SyncAllAsync(accessToken);
                    RefreshLibraryList();
                    ShowToast("Synced!");
                }
                else
                {
                    ShowToast("Sign in first to sync");
                }
            }
            catch
            {
                ShowToast("Sync failed");
            }
            finally
            {
                LibrarySyncBtn.IsEnabled = true;
            }
        }

        // ══════════════════════════════════════════
        // INFINITE SCROLL — Load more liked songs
        // ══════════════════════════════════════════
        private ScrollViewer _playlistSongsScrollViewer;
        private bool _scrollHooked = false;

        private void HookPlaylistSongsScroll()
        {
            if (_scrollHooked) return;

            // Try immediately if already loaded
            _playlistSongsScrollViewer = FindChildOfType<ScrollViewer>(PlaylistSongsList);
            if (_playlistSongsScrollViewer != null)
            {
                _playlistSongsScrollViewer.ViewChanged -= PlaylistSongsScroll_ViewChanged;
                _playlistSongsScrollViewer.ViewChanged += PlaylistSongsScroll_ViewChanged;
                _scrollHooked = true;
                return;
            }

            // Deferred: use named handler so we can unsubscribe after first success
            PlaylistSongsList.Loaded -= PlaylistSongsList_LoadedForScroll;
            PlaylistSongsList.Loaded += PlaylistSongsList_LoadedForScroll;
        }

        private void PlaylistSongsList_LoadedForScroll(object sender, RoutedEventArgs e)
        {
            PlaylistSongsList.Loaded -= PlaylistSongsList_LoadedForScroll; // Unsubscribe immediately
            _playlistSongsScrollViewer = FindChildOfType<ScrollViewer>(PlaylistSongsList);
            if (_playlistSongsScrollViewer != null)
            {
                _playlistSongsScrollViewer.ViewChanged -= PlaylistSongsScroll_ViewChanged;
                _playlistSongsScrollViewer.ViewChanged += PlaylistSongsScroll_ViewChanged;
                _scrollHooked = true;
            }
        }

        private async void PlaylistSongsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null || sv.ScrollableHeight == 0) return;

            // Trigger when within 1500px of the bottom
            if (sv.VerticalOffset >= sv.ScrollableHeight - 1500)
            {
                // 0. Local playlist check — local user playlists never have online continuation
                if (_currentViewingPlaylist != null && (string.IsNullOrEmpty(_currentViewingYtPlaylistId) || _currentViewingYtPlaylistId.StartsWith("LOCAL_")))
                {
                    return;
                }

                // 1. Liked Songs Pagination
                if (_isViewingLikedSongs && HasMoreLikedSongs)
                {
                    await LoadMoreLikedSongsAsync();
                    return;
                }

                // 2. Regular YouTube Playlist Pagination
                if (!_isViewingLikedSongs && !string.IsNullOrEmpty(_playlistContinuationToken) && !_isLoadingMorePlaylist)
                {
                    if (string.IsNullOrEmpty(_currentViewingYtPlaylistId) || _currentViewingYtPlaylistId.StartsWith("LOCAL_")) return;

                    _isLoadingMorePlaylist = true;
                    
                    string token = await GetAccessTokenAsync();
                    var plResult = await InnerTubeClient.BrowsePlaylistAsync(_currentViewingYtPlaylistId, _playlistContinuationToken, token);

                    if (plResult != null)
                    {
                        var tracks = PlaylistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>;
                        if (tracks != null)
                        {
                            foreach (var track in plResult.Tracks)
                            {
                                tracks.Add(track);
                            }
                            PlaylistDetailsTrackCount.Text = tracks.Count + (string.IsNullOrEmpty(plResult.ContinuationToken) ? "" : "+") + " tracks";
                        }
                        _playlistContinuationToken = plResult.ContinuationToken;
                    }
                    
                    _isLoadingMorePlaylist = false;
                }
            }
        }

        private static T FindChildOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var result = child as T;
                if (result != null) return result;
                var found = FindChildOfType<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        // ══════════════════════════════════════════
        // EXPORT / IMPORT PLAYLISTS (WP8.1 continuation pattern)
        // ══════════════════════════════════════════
        private string _pendingExportJson;

        private async void ExportPlaylists_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var export = new JObject();
                var playlistsArr = new JArray();
                foreach (var pl in _youtubeUserPlaylists)
                {
                    var plObj = new JObject
                    {
                        ["PlaylistId"] = pl.PlaylistId,
                        ["Title"] = pl.Title,
                        ["TrackCount"] = pl.TrackCount,
                        ["ThumbnailUrl"] = pl.ThumbnailUrl ?? ""
                    };
                    if (pl.PlaylistId.StartsWith("LOCAL_"))
                    {
                        var tracks = await LoadLocalPlaylistTracksAsync(pl.PlaylistId);
                        var tracksArr = new JArray();
                        foreach (var t in tracks)
                        {
                            tracksArr.Add(new JObject
                            {
                                ["VideoId"] = t.VideoId,
                                ["Title"] = t.Title,
                                ["ChannelName"] = t.ChannelName,
                                ["ThumbnailUrl"] = t.ThumbnailUrl ?? ""
                            });
                        }
                        plObj["Tracks"] = tracksArr;
                    }
                    playlistsArr.Add(plObj);
                }
                export["version"] = 1;
                export["exportDate"] = DateTimeOffset.Now.ToString("o");
                export["playlists"] = playlistsArr;

                _pendingExportJson = export.ToString(Newtonsoft.Json.Formatting.Indented);

                var picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
                picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
                picker.SuggestedFileName = "beatora_playlists";
                picker.PickSaveFileAndContinue();
            }
            catch (Exception ex) { ShowToast("Export failed: " + ex.Message); }
        }

        public async void HandleFileSaveContinuation(StorageFile file)
        {
            try
            {
                if (!string.IsNullOrEmpty(_pendingExportM3u))
                {
                    await FileIO.WriteTextAsync(file, _pendingExportM3u);
                    ShowToast("Exported M3U playlist successfully!");
                    _pendingExportM3u = null;
                }
                else if (!string.IsNullOrEmpty(_pendingExportJson))
                {
                    await FileIO.WriteTextAsync(file, _pendingExportJson);
                    ShowToast("Exported " + _youtubeUserPlaylists.Count + " playlists!");
                    _pendingExportJson = null;
                }
            }
            catch (Exception ex) { ShowToast("Export failed: " + ex.Message); }
        }

        private void ExportPlaylistM3u_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tracks = _currentPlaylistFullTracks;
                if (tracks == null || tracks.Count == 0)
                {
                    var source = PlaylistSongsList.ItemsSource as IEnumerable<YouTubeTrack>;
                    if (source != null) tracks = source.ToList();
                }

                if (tracks == null || tracks.Count == 0)
                {
                    ShowToast("Playlist is empty!");
                    return;
                }

                string plTitle = PlaylistDetailsTitle.Text ?? "Playlist";
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("#EXTM3U");
                sb.AppendLine("#PLAYLIST:" + plTitle);

                foreach (var t in tracks)
                {
                    string name = !string.IsNullOrEmpty(t.ChannelName) ? string.Format("{0} - {1}", t.ChannelName, t.Title) : t.Title;
                    sb.AppendLine(string.Format("#EXTINF:-1,{0}", name));
                    if (t.VideoId != null && t.VideoId.StartsWith("LOCAL:"))
                        sb.AppendLine(t.VideoId.Substring(6));
                    else
                        sb.AppendLine(string.Format("https://www.youtube.com/watch?v={0}", t.VideoId));
                }

                _pendingExportM3u = sb.ToString();

                var picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
                picker.FileTypeChoices.Add("M3U Playlist", new List<string> { ".m3u" });
                picker.SuggestedFileName = SanitizeFileName(plTitle);
                picker.PickSaveFileAndContinue();
            }
            catch (Exception ex)
            {
                ShowToast("Export M3U failed: " + ex.Message);
            }
        }

        private async void ExportAllPlaylistsM3u_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("#EXTM3U");
                sb.AppendLine("#PLAYLIST:All YTMusic Playlists");

                int count = 0;
                if (favoriteTracks.Count > 0)
                {
                    foreach (var t in favoriteTracks)
                    {
                        string name = !string.IsNullOrEmpty(t.ChannelName) ? string.Format("{0} - {1}", t.ChannelName, t.Title) : t.Title;
                        sb.AppendLine(string.Format("#EXTINF:-1,{0}", name));
                        sb.AppendLine(string.Format("https://www.youtube.com/watch?v={0}", t.VideoId));
                        count++;
                    }
                }

                foreach (var pl in _youtubeUserPlaylists)
                {
                    if (pl.PlaylistId.StartsWith("LOCAL_"))
                    {
                        var localTracks = await LoadLocalPlaylistTracksAsync(pl.PlaylistId);
                        foreach (var t in localTracks)
                        {
                            string name = !string.IsNullOrEmpty(t.ChannelName) ? string.Format("{0} - {1}", t.ChannelName, t.Title) : t.Title;
                            sb.AppendLine(string.Format("#EXTINF:-1,{0}", name));
                            if (t.VideoId != null && t.VideoId.StartsWith("LOCAL:"))
                                sb.AppendLine(t.VideoId.Substring(6));
                            else
                                sb.AppendLine(string.Format("https://www.youtube.com/watch?v={0}", t.VideoId));
                            count++;
                        }
                    }
                }

                if (count == 0)
                {
                    ShowToast("No tracks to export!");
                    return;
                }

                _pendingExportM3u = sb.ToString();

                var picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
                picker.FileTypeChoices.Add("M3U Playlist", new List<string> { ".m3u" });
                picker.SuggestedFileName = "all_playlists";
                picker.PickSaveFileAndContinue();
            }
            catch (Exception ex)
            {
                ShowToast("Export M3U failed: " + ex.Message);
            }
        }

        private void ImportM3uPlaylist_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
                picker.FileTypeFilter.Add(".m3u");
                picker.FileTypeFilter.Add(".m3u8");
                picker.PickSingleFileAndContinue();
            }
            catch (Exception ex)
            {
                ShowToast("Import M3U failed: " + ex.Message);
            }
        }

        private async Task ImportM3uFileAsync(StorageFile file)
        {
            try
            {
                var lines = await FileIO.ReadLinesAsync(file);
                string playlistName = file.DisplayName;
                var tracks = new List<YouTubeTrack>();

                string currentTitle = "";
                string currentArtist = "";

                foreach (var rawLine in lines)
                {
                    string line = rawLine != null ? rawLine.Trim() : "";
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("#PLAYLIST:", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = line.Substring(10).Trim();
                        if (!string.IsNullOrEmpty(name)) playlistName = name;
                    }
                    else if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                    {
                        int commaIdx = line.IndexOf(',');
                        if (commaIdx >= 0 && commaIdx + 1 < line.Length)
                        {
                            string info = line.Substring(commaIdx + 1).Trim();
                            int dashIdx = info.IndexOf(" - ");
                            if (dashIdx > 0)
                            {
                                currentArtist = info.Substring(0, dashIdx).Trim();
                                currentTitle = info.Substring(dashIdx + 3).Trim();
                            }
                            else
                            {
                                currentArtist = "";
                                currentTitle = info;
                            }
                        }
                    }
                    else if (!line.StartsWith("#"))
                    {
                        string videoId = ExtractVideoIdFromM3uLine(line);
                        if (!string.IsNullOrEmpty(videoId))
                        {
                            tracks.Add(new YouTubeTrack
                            {
                                VideoId = videoId,
                                Title = !string.IsNullOrEmpty(currentTitle) ? currentTitle : (videoId.StartsWith("LOCAL:") ? videoId.Substring(6) : videoId),
                                ChannelName = currentArtist ?? "",
                                ThumbnailUrl = videoId.StartsWith("LOCAL:") ? "" : string.Format("https://i.ytimg.com/vi/{0}/hqdefault.jpg", videoId)
                            });
                        }
                        currentTitle = "";
                        currentArtist = "";
                    }
                }

                if (tracks.Count == 0)
                {
                    ShowToast("No valid tracks found in M3U file");
                    return;
                }

                string playlistId = "LOCAL_" + Guid.NewGuid().ToString("N");
                var plInfo = new YouTubePlaylistInfo
                {
                    PlaylistId = playlistId,
                    Title = playlistName,
                    TrackCount = tracks.Count,
                    ThumbnailUrl = tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.ThumbnailUrl))?.ThumbnailUrl ?? ""
                };

                _youtubeUserPlaylists.Insert(0, plInfo);
                SaveYouTubePlaylistsCacheAsync();
                await SaveLocalPlaylistTracksAsync(playlistId, tracks);

                RefreshLibraryList();
                ShowToast(string.Format("Imported '{0}' ({1} songs)!", playlistName, tracks.Count));
            }
            catch (Exception ex)
            {
                ShowToast("Failed to import M3U: " + ex.Message);
            }
        }

        private static string ExtractVideoIdFromM3uLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            int vIdx = line.IndexOf("v=");
            if (vIdx >= 0)
            {
                string id = line.Substring(vIdx + 2);
                int ampIdx = id.IndexOf('&');
                if (ampIdx >= 0) id = id.Substring(0, ampIdx);
                return id.Trim();
            }

            int beIdx = line.IndexOf("youtu.be/");
            if (beIdx >= 0)
            {
                string id = line.Substring(beIdx + 9);
                int qIdx = id.IndexOf('?');
                if (qIdx >= 0) id = id.Substring(0, qIdx);
                return id.Trim();
            }

            if (line.StartsWith("LOCAL:", StringComparison.OrdinalIgnoreCase))
                return line;

            if (line.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".aac", StringComparison.OrdinalIgnoreCase))
            {
                return "LOCAL:" + System.IO.Path.GetFileName(line);
            }

            if (line.Length == 11 && !line.Contains(" ") && !line.Contains("/") && !line.Contains(":"))
                return line;

            return null;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "playlist";
            char[] invalids = System.IO.Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (System.Array.IndexOf(invalids, c) < 0)
                    sb.Append(c);
            }
            string res = sb.ToString().Trim();
            return string.IsNullOrEmpty(res) ? "playlist" : res;
        }

        private void ImportPlaylists_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
                picker.FileTypeFilter.Add(".json");
                picker.PickSingleFileAndContinue();
            }
            catch (Exception ex) { ShowToast("Import failed: " + ex.Message); }
        }

        public async void HandleFileOpenContinuation(StorageFile file)
        {
            try
            {
                if (file.FileType.Equals(".m3u", StringComparison.OrdinalIgnoreCase) ||
                    file.FileType.Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
                {
                    await ImportM3uFileAsync(file);
                    return;
                }

                string json = await FileIO.ReadTextAsync(file);
                var data = JObject.Parse(json);
                var playlists = data["playlists"] as JArray;
                if (playlists == null) { ShowToast("Invalid file"); return; }

                int imported = 0;
                foreach (var item in playlists)
                {
                    string plId = item["PlaylistId"]?.ToString() ?? "";
                    string title = item["Title"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(plId)) continue;
                    if (_youtubeUserPlaylists.Any(p => p.PlaylistId == plId)) continue;

                    _youtubeUserPlaylists.Add(new YouTubePlaylistInfo
                    {
                        PlaylistId = plId,
                        Title = title,
                        TrackCount = item["TrackCount"]?.Value<int>() ?? 0,
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString() ?? ""
                    });

                    var tracks = item["Tracks"] as JArray;
                    if (tracks != null && plId.StartsWith("LOCAL_"))
                    {
                        var trackList = new List<YouTubeTrack>();
                        foreach (var t in tracks)
                        {
                            trackList.Add(new YouTubeTrack
                            {
                                VideoId = t["VideoId"]?.ToString() ?? "",
                                Title = t["Title"]?.ToString() ?? "",
                                ChannelName = t["ChannelName"]?.ToString() ?? "",
                                ThumbnailUrl = t["ThumbnailUrl"]?.ToString() ?? ""
                            });
                        }
                        await SaveLocalPlaylistTracksAsync(plId, trackList);
                    }
                    imported++;
                }

                SaveYouTubePlaylistsCacheAsync();
                RefreshLibraryList();
                ShowToast("Imported " + imported + " playlists!");
            }
            catch (Exception ex) { ShowToast("Import failed: " + ex.Message); }
        }

    }
}

