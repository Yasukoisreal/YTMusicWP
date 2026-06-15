using Newtonsoft.Json.Linq;
using System;
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

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private void CancelCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            CreatePlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private void ConfirmCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            string name = NewPlaylistNameTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                userPlaylists.Insert(0, new UserPlaylist { Name = name });
                SavePlaylistsAsync();
                ShowToast("Playlist created.");
            }
            CreatePlaylistDialog.Visibility = Visibility.Collapsed;
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
            var playlist = (sender as MenuFlyoutItem)?.DataContext as UserPlaylist;
            if (playlist != null)
            {
                userPlaylists.Remove(playlist);
                SavePlaylistsAsync();
                ShowToast("Playlist deleted.");
            }
        }

        private void MenuDeletePlaylistInside_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewingPlaylist != null)
            {
                userPlaylists.Remove(_currentViewingPlaylist);
                SavePlaylistsAsync();
                ShowToast("Playlist deleted.");
                PlaylistSlideOutStoryboard.Begin();
            }
        }

        private void MenuRemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null && _currentViewingPlaylist != null)
            {
                _currentViewingPlaylist.Tracks.Remove(track);
                SavePlaylistsAsync();
                ShowToast("Removed from playlist");
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
                    PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(_currentViewingPlaylist.Tracks[0].ThumbnailUrl), UriKind.Absolute)) { DecodePixelWidth = 150 };
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

        private void MenuAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            _trackPendingForPlaylist = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (_trackPendingForPlaylist != null)
            {
                AddToPlaylistDialog.Visibility = Visibility.Visible;
            }
        }

        private void CancelAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            AddToPlaylistDialog.Visibility = Visibility.Collapsed;
        }

        private void DialogPlaylistList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var playlist = e.ClickedItem as UserPlaylist;
            if (playlist != null && _trackPendingForPlaylist != null)
            {
                if (!playlist.Tracks.Any(t => t.VideoId == _trackPendingForPlaylist.VideoId))
                {
                    playlist.Tracks.Insert(0, _trackPendingForPlaylist);
                    SavePlaylistsAsync();
                    ShowToast("Added to " + playlist.Name);
                }
                else
                {
                    ShowToast("Song already in playlist.");
                }
            }
            AddToPlaylistDialog.Visibility = Visibility.Collapsed;
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
                args.Request.Data.Properties.Title = "Youtify - Share Music";
                args.Request.Data.Properties.Description = "Listen to " + _trackToShare.Title;
                string url = "https://www.youtube.com/watch?v=" + _trackToShare.VideoId;
                args.Request.Data.SetWebLink(new Uri(url));
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
            _sleepTimerMode++;
            if (_sleepTimerMode > 3) _sleepTimerMode = 0;

            if (_sleepTimerMode == 0)
            {
                _sleepTimer.Stop();
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

        private async void MenuDownload_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null) await DownloadTrackAsync(track);
        }

        private void MenuFavorite_Click(object sender, RoutedEventArgs e)
        {
            var track = (sender as MenuFlyoutItem)?.DataContext as YouTubeTrack;
            if (track != null)
            {
                var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                if (existing != null) favoriteTracks.Remove(existing);
                else favoriteTracks.Insert(0, track);
                SaveFavoritesAsync();
                ShowToast(existing != null ? "Removed from Favorites" : "Added to Favorites");

                if (currentTrack != null && currentTrack.VideoId == track.VideoId)
                {
                    BigHeartBtn.Content = existing == null ? "♥" : "♡";
                    BigHeartBtn.Foreground = existing == null ? _greenBrush : _whiteBrush;
                }
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

    }
}
