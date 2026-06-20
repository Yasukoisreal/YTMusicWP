using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private string _libraryFilter = "all";
        private ObservableCollection<LibraryItem> _libraryItems = new ObservableCollection<LibraryItem>();
        private bool _isViewingLikedSongs = false;

        private static readonly SolidColorBrush _libChipActiveTextBrush = new SolidColorBrush(Windows.UI.Colors.Black);

        private void LibChip_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            _libraryFilter = btn.Tag as string ?? "all";

            // Reset all chips to inactive
            var chips = new[] { LibChipAll, LibChipPlaylists, LibChipArtists, LibChipDownloads, LibChipRecent };
            foreach (var chip in chips)
            {
                chip.Background = _chipInactiveBrush;
                chip.Foreground = _whiteBrush;
            }

            // Set active chip
            btn.Background = _chipActiveBrush;
            btn.Foreground = _libChipActiveTextBrush;

            RefreshLibraryList();
        }

        private void RefreshLibraryList()
        {
            _libraryItems.Clear();

            bool showAll = _libraryFilter == "all";

            // Liked Songs
            if (showAll || _libraryFilter == "playlists")
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

            // (Local playlists removed — all playlists are now YouTube-synced)

            // Downloads
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

            // Recent History
            if (showAll || _libraryFilter == "recent")
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

            // YT Playlists
            if (showAll || _libraryFilter == "playlists")
            {
                foreach (var ytpl in _youtubeUserPlaylists)
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

            // Subscriptions (artists)
            if (showAll || _libraryFilter == "artists")
            {
                foreach (var sub in _youtubeSubscriptions)
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

            LibraryUnifiedList.ItemsSource = _libraryItems;
            LibraryEmptyState.Visibility = _libraryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LibraryUnified_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as LibraryItem;
            if (item == null) return;

            switch (item.ItemType)
            {
                case "favorites":
                    // Open a pseudo-playlist view showing favorites
                    _currentViewingPlaylist = null;
                    _currentViewingYtPlaylistId = null;
                    _isViewingLikedSongs = true;
                    PlaylistDetailsTitle.Text = "Liked Songs";
                    if (favoriteTracks.Count > 0 && !string.IsNullOrEmpty(favoriteTracks[0].ThumbnailUrl))
                    {
                        PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetSquareThumbnail(favoriteTracks[0].ThumbnailUrl), UriKind.Absolute)) { DecodePixelWidth = 220 };
                        PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                    }
                    PlaylistSongsList.ItemsSource = favoriteTracks;
                    PlaylistDetailsTrackCount.Text = favoriteTracks.Count + (HasMoreLikedSongs ? "+" : "") + " songs";
                    PlaylistDetailsView.Visibility = Visibility.Visible;
                    PlaylistSlideInStoryboard.Begin();
                    HookPlaylistSongsScroll();
                    break;

                case "playlist":
                    var pl = item.Tag as UserPlaylist;
                    _currentViewingPlaylist = pl;
                    if (pl != null)
                    {
                        PlaylistDetailsTitle.Text = pl.Name;
                        if (pl.Tracks != null && pl.Tracks.Count > 0 && !string.IsNullOrEmpty(pl.Tracks[0].ThumbnailUrl))
                        {
                            PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetSquareThumbnail(pl.Tracks[0].ThumbnailUrl), UriKind.Absolute)) { DecodePixelWidth = 220 };
                            PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                        }
                        PlaylistSongsList.ItemsSource = pl.Tracks;
                        PlaylistDetailsTrackCount.Text = (pl.Tracks != null ? pl.Tracks.Count : 0) + " tracks";
                        PlaylistDetailsView.Visibility = Visibility.Visible;
                        PlaylistSlideInStoryboard.Begin();
                    }
                    break;

                case "downloads":
                    _currentViewingPlaylist = null;
                    _currentViewingYtPlaylistId = null;
                    PlaylistDetailsTitle.Text = "Downloaded Songs";
                    PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                    PlaylistSongsList.ItemsSource = downloadedTracks;
                    PlaylistDetailsTrackCount.Text = downloadedTracks.Count + " tracks";
                    PlaylistDetailsView.Visibility = Visibility.Visible;
                    PlaylistSlideInStoryboard.Begin();
                    break;

                case "recent":
                    _currentViewingPlaylist = null;
                    _currentViewingYtPlaylistId = null;
                    PlaylistDetailsTitle.Text = "Recently Played";
                    PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                    PlaylistSongsList.ItemsSource = historyTracks;
                    PlaylistDetailsTrackCount.Text = historyTracks.Count + " tracks";
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
            }
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
        }

        private async void MenuRemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null && _currentViewingYtPlaylistId != null)
            {
                bool success = await RemoveFromYouTubePlaylistAsync(_currentViewingYtPlaylistId, track.VideoId);
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
        }

        private void PlaylistsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            _currentViewingPlaylist = e.ClickedItem as UserPlaylist;
            if (_currentViewingPlaylist != null)
            {
                PlaylistDetailsTitle.Text = _currentViewingPlaylist.Name;
                if (_currentViewingPlaylist.Tracks != null && _currentViewingPlaylist.Tracks.Count > 0 && !string.IsNullOrEmpty(_currentViewingPlaylist.Tracks[0].ThumbnailUrl))
                {
                    PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetSquareThumbnail(_currentViewingPlaylist.Tracks[0].ThumbnailUrl), UriKind.Absolute)) { DecodePixelWidth = 220 };
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

        private async void MenuAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            _trackPendingForPlaylist = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (_trackPendingForPlaylist == null) return;

            // Require login
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                ShowToast("Sign in to add to playlist");
                return;
            }

            DialogPlaylistList.ItemsSource = _youtubeUserPlaylists;
            AddToPlaylistDialog.Visibility = Visibility.Visible;
        }

        private void CancelAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            AddToPlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private async void DialogPlaylistList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var ytPlaylist = e.ClickedItem as YouTubePlaylistInfo;
            if (ytPlaylist != null && _trackPendingForPlaylist != null)
            {
                AddToPlaylistDialog.Visibility = Visibility.Collapsed;
                ShowToast("Adding to " + ytPlaylist.Title + "...");

                bool success = await AddToYouTubePlaylistAsync(ytPlaylist.PlaylistId, _trackPendingForPlaylist.VideoId);
                if (success)
                {
                    // Save track to local cache for local playlists
                    if (ytPlaylist.PlaylistId.StartsWith("LOCAL_"))
                    {
                        await AddTrackToLocalPlaylistAsync(ytPlaylist.PlaylistId, _trackPendingForPlaylist);
                        // Use first track's thumbnail as playlist cover
                        if (string.IsNullOrEmpty(ytPlaylist.ThumbnailUrl) && !string.IsNullOrEmpty(_trackPendingForPlaylist.ThumbnailUrl))
                            ytPlaylist.ThumbnailUrl = _trackPendingForPlaylist.ThumbnailUrl;
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
            else
            {
                AddToPlaylistDialog.Visibility = Visibility.Collapsed;
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
            if (_trackToShare != null)
            {
                if (_trackToShare.VideoId.StartsWith("LOCAL:"))
                {
                    args.Request.Data.Properties.Title = "Beatora";
                    args.Request.Data.Properties.Description = _trackToShare.Title;
                    args.Request.Data.SetText("🎵 " + _trackToShare.Title + " — " + _trackToShare.ChannelName);
                }
                else
                {
                    args.Request.Data.Properties.Title = "Beatora - Share Music";
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
            if (string.IsNullOrEmpty(token))
            {
                ShowToast("Sign in to like songs");
                return;
            }

            var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
            bool isAdding = (existing == null);
            if (existing != null) favoriteTracks.Remove(existing);
            else favoriteTracks.Insert(0, track);
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
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(track.VideoId.Substring(6));
                    await file.DeleteAsync();

                    downloadedTracks.Remove(track);

                    var fav = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                    if (fav != null) { favoriteTracks.Remove(fav); SaveFavoritesAsync(); }

                    var hist = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                    if (hist != null)
                    {
                        historyTracks.Remove(hist);
                        var ignoredHist = SaveHistoryAsyncTask();
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
            if (track == null || string.IsNullOrEmpty(track.VideoId) || track.VideoId.StartsWith("LOCAL:")) return;
            if (!IsInternetAvailable()) { ShowToast("Internet required to download"); return; }

            try
            {
                DownloadStatusBar.Visibility = Visibility.Visible;
                DownloadStatusText.Text = "Resolving: " + track.Title;
                DownloadProgressBar.Value = 0;
                DownloadProgressBar.IsIndeterminate = true;

                // Dùng InnerTube để lấy stream URL thay vì proxy (proxy đã bị chặn)
                string streamUrl = await InnerTubeClient.ResolveStreamUrlAsync(track.VideoId);
                if (string.IsNullOrEmpty(streamUrl))
                {
                    DownloadStatusBar.Visibility = Visibility.Collapsed;
                    ShowToast("Cannot resolve audio URL for this track");
                    return;
                }

                string safeTitle = string.Join("", track.Title.Split(System.IO.Path.GetInvalidFileNameChars()));
                StorageFile destinationFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(safeTitle + ".m4a", CreationCollisionOption.ReplaceExisting);

                BackgroundDownloader downloader = new BackgroundDownloader();
                DownloadOperation download = downloader.CreateDownload(new Uri(streamUrl), destinationFile);

                DownloadStatusText.Text = "Downloading: " + track.Title;

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
                        SavePlaylistsAsync();
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
                if (!string.IsNullOrEmpty(accessToken))
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

            // Find ScrollViewer inside ListView (deferred since it's created by template)
            PlaylistSongsList.Loaded += (s, e) =>
            {
                _playlistSongsScrollViewer = FindChildOfType<ScrollViewer>(PlaylistSongsList);
                if (_playlistSongsScrollViewer != null)
                {
                    _playlistSongsScrollViewer.ViewChanged += PlaylistSongsScroll_ViewChanged;
                    _scrollHooked = true;
                }
            };

            // Try immediately if already loaded
            _playlistSongsScrollViewer = FindChildOfType<ScrollViewer>(PlaylistSongsList);
            if (_playlistSongsScrollViewer != null)
            {
                _playlistSongsScrollViewer.ViewChanged += PlaylistSongsScroll_ViewChanged;
                _scrollHooked = true;
            }
        }

        private async void PlaylistSongsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!_isViewingLikedSongs || !HasMoreLikedSongs) return;
            if (e.IsIntermediate) return; // Wait until scroll settles

            var sv = sender as ScrollViewer;
            if (sv == null) return;

            // Trigger when within 200px of the bottom
            if (sv.VerticalOffset >= sv.ScrollableHeight - 200)
            {
                await LoadMoreLikedSongsAsync();
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
        // EXPORT / IMPORT PLAYLISTS
        // ══════════════════════════════════════════
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
                    // Include tracks for local playlists
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

                // Save to Music Library (app has musicLibrary capability)
                var folder = KnownFolders.MusicLibrary;
                var file = await folder.CreateFileAsync("beatora_playlists.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, export.ToString(Newtonsoft.Json.Formatting.Indented));
                ShowToast("Exported to Music\\beatora_playlists.json");
            }
            catch (Exception ex) { ShowToast("Export failed: " + ex.Message); }
        }

        private async void ImportPlaylists_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Read from Music Library where we exported
                var folder = KnownFolders.MusicLibrary;
                var file = await folder.GetFileAsync("beatora_playlists.json");

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

                    // Skip if already exists
                    if (_youtubeUserPlaylists.Any(p => p.PlaylistId == plId)) continue;

                    _youtubeUserPlaylists.Add(new YouTubePlaylistInfo
                    {
                        PlaylistId = plId,
                        Title = title,
                        TrackCount = item["TrackCount"]?.Value<int>() ?? 0,
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString() ?? ""
                    });

                    // Restore tracks for local playlists
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
