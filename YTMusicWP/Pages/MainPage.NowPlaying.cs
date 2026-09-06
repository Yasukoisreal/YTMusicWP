using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        

        private void MenuShareNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack != null)
            {
                _trackToShare = currentTrack;
                NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
                DataTransferManager.ShowShareUI();
            }
        }

        private void MenuSleepTimerNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            SleepTimer_Click(null, null);
        }

        private async void MenuLikeNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack == null || currentTrack.VideoId.StartsWith("LOCAL:")) return;
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;

            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth)
            {
                ShowToast("Login required to Like");
                return;
            }

            bool success = await InnerTubeClient.LikeVideoAsync(currentTrack.VideoId, token);
            ShowToast(success ? "Added to Liked Songs!" : "Failed to Like song");
        }

        private void MenuAddToPlaylistNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack == null) return;
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;

            _trackPendingForPlaylist = currentTrack;
            DialogPlaylistList.ItemsSource = _youtubeUserPlaylists;
            AddToPlaylistDialog.Visibility = Visibility.Visible;
        }

        private async void MenuWatchLaterNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack == null || currentTrack.VideoId.StartsWith("LOCAL:")) return;
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;

            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth)
            {
                ShowToast("Login required for Watch Later");
                return;
            }

            bool success = await AddToWatchLaterAsync(currentTrack.VideoId);
            ShowToast(success ? "Added to Watch Later!" : "Failed to add to Watch Later");
        }

        private void IncreaseLyricsSize_Click(object sender, RoutedEventArgs e)
        {
            if (_lyricFontSize < 36)
            {
                _lyricFontSize += 2;
                RefreshLyricsSize();
                ShowToast("A+ (" + _lyricFontSize + "px)");
            }
        }

        private void DecreaseLyricsSize_Click(object sender, RoutedEventArgs e)
        {
            if (_lyricFontSize > 14)
            {
                _lyricFontSize -= 2;
                RefreshLyricsSize();
                ShowToast("A- (" + _lyricFontSize + "px)");
            }
        }

        private void RefreshLyricsSize()
        {
            for (int i = 0; i < currentLyrics.Count; i++)
            {
                currentLyrics[i].FontSize = _lyricFontSize;
            }
        }

        private void QueueListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var track = e.ClickedItem as YouTubeTrack;
            if (track != null)
            {
                PlayTrack(track);
            }
        }

        private void MiniPlayer_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (MiniPlayerTranslate == null) return;
            double newX = MiniPlayerTranslate.X + e.Delta.Translation.X;
            if (newX > 80) newX = 80;
            if (newX < -80) newX = -80;
            MiniPlayerTranslate.X = newX;
        }

        private void MiniPlayer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (MiniPlayerTranslate == null) return;
            double totalX = e.Cumulative.Translation.X;
            double velX = e.Velocities.Linear.X;

            MiniPlayerTranslate.X = 0;

            if (totalX < -40 || velX < -0.15)
            {
                NextButton_Click(null, null);
            }
            else if (totalX > 40 || velX > 0.15)
            {
                PrevButton_Click(null, null);
            }
        }

        private void MiniPlayer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            NowPlayingView.Visibility = Visibility.Visible;
            if (this.Resources.ContainsKey("SlideUpStoryboard"))
            {
                var storyboard = (Windows.UI.Xaml.Media.Animation.Storyboard)this.Resources["SlideUpStoryboard"];
                storyboard.Begin();
            }
            // Start marquee after panel is visible and laid out
            var ignored3 = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => StartTitleMarquee());

            if (currentTrack != null)
            {
                UpdateNowPlayingGradient(currentTrack.Title, currentTrack.ChannelName, currentTrack.ThumbnailUrl);
            }
        }
        private async void RestoreSearchBoxFocus()
        {
            await Task.Delay(500);
            if (SearchBox != null) SearchBox.IsTabStop = true;
        }

        private void CloseNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            // Block SearchBox focus BEFORE any panel changes
            if (SearchBox != null) SearchBox.IsTabStop = false;
            
            // Stop gradient animation timer
            if (_gradientPulseTimer != null) { _gradientPulseTimer.Stop(); }
            StopTitleMarquee();

            if (this.Resources.ContainsKey("SlideDownStoryboard"))
            {
                var storyboard = (Windows.UI.Xaml.Media.Animation.Storyboard)this.Resources["SlideDownStoryboard"];
                storyboard.Begin();
            }
            else
            {
                NowPlayingView.Visibility = Visibility.Collapsed;
                RestoreSearchBoxFocus();
            }
        }

        private void OpenNowPlayingMenu_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack != null)
            {
                NowPlayingDownloadBtn.Visibility = currentTrack.VideoId.StartsWith("LOCAL:") ? Visibility.Collapsed : Visibility.Visible;
            }

            NowPlayingMenuDialog.Visibility = Visibility.Visible;
            if (this.Resources.ContainsKey("MenuSlideUpStoryboard"))
            {
                var storyboard = (Windows.UI.Xaml.Media.Animation.Storyboard)this.Resources["MenuSlideUpStoryboard"];
                storyboard.Begin();
            }
        }

        private void CloseNowPlayingMenu_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null) SearchBox.IsTabStop = false;
            
            if (this.Resources.ContainsKey("MenuSlideDownStoryboard"))
            {
                var storyboard = (Windows.UI.Xaml.Media.Animation.Storyboard)this.Resources["MenuSlideDownStoryboard"];
                storyboard.Begin();
            }
            else
            {
                NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
                RestoreSearchBoxFocus();
            }
        }

        private void MenuSlideDownStoryboard_Completed(object sender, object e)
        {
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
            RestoreSearchBoxFocus();
        }

        private async void MenuDownloadNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            CloseNowPlayingMenu_Click(null, null);
            if (currentTrack != null) await DownloadTrackAsync(currentTrack);
        }

        private void DotPlayer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            NowPlayingPivot.SelectedIndex = 0;
        }

        private void DotLyrics_Tapped(object sender, TappedRoutedEventArgs e)
        {
            NowPlayingPivot.SelectedIndex = 1;
        }

        private void DotQueue_Tapped(object sender, TappedRoutedEventArgs e)
        {
            NowPlayingPivot.SelectedIndex = 2;
        }

        private void QueueButton_Click(object sender, RoutedEventArgs e)
        {
            if (NowPlayingPivot.SelectedIndex == 2)
            {
                NowPlayingPivot.SelectedIndex = 0;
            }
            else
            {
                NowPlayingPivot.SelectedIndex = 2;
            }
        }

        private void QueueClear_Click(object sender, RoutedEventArgs e)
        {
            if (currentQueueTracks.Count <= 1) return;
            var playing = currentQueueTracks.FirstOrDefault(t => t.VideoId == currentTrack?.VideoId) ?? currentTrack;
            currentQueueTracks.Clear();
            if (playing != null) currentQueueTracks.Add(playing);
            UpdateQueueActiveState();
            SyncQueueToBackground(false);
            ShowToast("Queue cleared");
        }

        private void NowPlayingPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = NowPlayingPivot.SelectedIndex;
            DotPlayer.Opacity = idx == 0 ? 1.0 : 0.3;
            DotLyrics.Opacity = idx == 1 ? 1.0 : 0.3;
            DotQueue.Opacity  = idx == 2 ? 1.0 : 0.3;
            MiniLyricCanvas.Opacity = idx == 0 ? 1.0 : 0.0;

            if (QueueIcon != null)
            {
                QueueIcon.Foreground = idx == 2 ? _greenBrush : _whiteBrush;
            }

            if (idx == 2)
            {
                UpdateQueueActiveState();
                var playing = currentQueueTracks.FirstOrDefault(t => t.IsPlaying);
                if (playing != null)
                {
                    try { QueueListView.ScrollIntoView(playing); } catch { }
                }
            }
            
            ForceUpdateLyricUI();
        }

        private void SlideDownStoryboard_Completed(object sender, object e)
        {
            NowPlayingView.Visibility = Visibility.Collapsed;
            RestoreSearchBoxFocus();
        }

        private void SongItem_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                var el = sender as FrameworkElement;
                if (el != null)
                {
                    var flyout = FlyoutBase.GetAttachedFlyout(el);
                    if (flyout != null)
                    {
                        flyout.ShowAt(el);
                    }
                }
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var track = btn.DataContext as YouTubeTrack;
            if (track == null) return;

            _bottomSheetTrack = track;
            BottomSheetTitle.Text = track.Title;
            BottomSheetArtist.Text = track.ChannelName;
            if (!string.IsNullOrEmpty(track.ThumbnailUrl))
            {
                try {
                    BottomSheetCover.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(track.ThumbnailUrl)) { DecodePixelWidth = 100 };
                } catch {}
            }

            // Show delete button only for downloaded (LOCAL:) tracks
            BottomSheetDeleteBtn.Visibility = (track.VideoId != null && track.VideoId.StartsWith("LOCAL:"))
                ? Visibility.Visible : Visibility.Collapsed;

            CustomBottomSheet.Visibility = Visibility.Visible;
            BottomSheetSlideUpStoryboard.Begin();
        }

        private void CloseBottomSheet_Click(object sender, RoutedEventArgs e)
        {
            BottomSheetSlideDownStoryboard.Begin();
        }

        private void BottomSheetSlideDownStoryboard_Completed(object sender, object e)
        {
            CustomBottomSheet.Visibility = Visibility.Collapsed;
        }

        private void BottomSheetContent_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void BottomSheetPlay_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack != null) PlayTrack(_bottomSheetTrack);
        }

        private void BottomSheetAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack != null)
            {
                _trackPendingForPlaylist = _bottomSheetTrack;
                DialogPlaylistList.ItemsSource = _youtubeUserPlaylists;
                AddToPlaylistDialog.Visibility = Visibility.Visible;
            }
        }

        private void BottomSheetGoToArtist_Click(object sender, RoutedEventArgs e)
        {
            var track = _bottomSheetTrack;
            CloseBottomSheet_Click(null, null);

            if (track != null)
            {
                string artistStr = track.ChannelName;
                if (string.IsNullOrEmpty(artistStr) && !string.IsNullOrEmpty(track.ChannelId))
                    artistStr = track.ChannelId;

                if (string.IsNullOrEmpty(artistStr))
                {
                    ShowToast("Artist info not available");
                    return;
                }

                var artists = SplitArtistNames(artistStr);
                if (artists.Count > 1)
                {
                    ShowArtistPicker(artists);
                }
                else
                {
                    OpenArtistProfile(track.ChannelId, artists.Count == 1 ? artists[0] : artistStr);
                }
            }
        }

        private void BottomSheetSleepTimer_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            SleepTimer_Click(null, null);
        }

        private void BottomSheetShare_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack != null)
            {
                _trackToShare = _bottomSheetTrack;
                DataTransferManager.ShowShareUI();
            }
        }

        private async void BottomSheetDelete_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            var track = _bottomSheetTrack;
            if (track == null || !track.VideoId.StartsWith("LOCAL:")) return;

            try
            {
                // If deleting the currently playing track, stop/skip first and wait for player to release file lock
                bool wasPlaying = (currentTrack != null && currentTrack.VideoId == track.VideoId);
                if (wasPlaying)
                {
                    try
                    {
                        if (currentQueueTracks.Count > 1)
                        {
                            BackgroundMediaPlayer.SendMessageToBackground(new Windows.Foundation.Collections.ValueSet { { "NextTrackMessage", "" } });
                        }
                        else
                        {
                            _appMediaPlayer.Pause();
                        }
                    }
                    catch
                    {
                        try { _appMediaPlayer.Pause(); } catch { }
                    }
                    currentTrack = null;
                    await Task.Delay(600);
                }

                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(track.VideoId.Substring(6));
                try
                {
                    await file.DeleteAsync();
                }
                catch
                {
                    // Retry once if file was still briefly locked by player
                    await Task.Delay(500);
                    await file.DeleteAsync();
                }

                downloadedTracks.Remove(track);

                // Also remove from queue if present and sync
                var inQueue = currentQueueTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                if (inQueue != null)
                {
                    currentQueueTracks.Remove(inQueue);
                    SyncQueueToBackground();
                }

                var fav = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                if (fav != null) 
                { 
                    favoriteTracks.Remove(fav); 
                    var _ = YTMusicWP.Services.DatabaseHelper.RemoveFavoriteAsync(track.VideoId); 
                }

                var hist = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                if (hist != null)
                {
                    historyTracks.Remove(hist);
                    var _ = YTMusicWP.Services.DatabaseHelper.ClearHistoryAsync(); // Just a workaround, normally shouldn't remove history on download delete, but kept for parity.
                    foreach (var h in historyTracks) { var __ = YTMusicWP.Services.DatabaseHelper.AddOrUpdateHistoryAsync(h); }
                    RefreshHomeHistorySections();
                }

                foreach (var playlist in userPlaylists)
                {
                    var pt = playlist.Tracks.FirstOrDefault(t => t.VideoId == track.VideoId);
                    if (pt != null) playlist.Tracks.Remove(pt);
                }
                SavePlaylistsAsync();

                // Update track count in Downloaded Songs view if currently viewing it
                if (PlaylistDetailsView.Visibility == Visibility.Visible && PlaylistDetailsTitle.Text == "Downloaded Songs")
                {
                    PlaylistDetailsTrackCount.Text = downloadedTracks.Count + " tracks";
                }

                ShowToast("File deleted from device");
            }
            catch
            {
                ShowToast("Failed to delete file");
            }
        }

        private void MenuGoToArtistNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;

            if (currentTrack != null)
            {
                string artistStr = currentTrack.ChannelName;
                var artists = SplitArtistNames(artistStr);

                if (artists.Count > 1)
                {
                    ShowArtistPicker(artists);
                }
                else
                {
                    NowPlayingView.Visibility = Visibility.Collapsed;
                    OpenArtistProfile(currentTrack.ChannelId, artists.Count == 1 ? artists[0] : artistStr);
                }
            }
        }

        private void ShowArtistPicker(System.Collections.Generic.List<string> artists)
        {
            ArtistPickerList.ItemsSource = artists;
            ArtistPickerBottomSheet.Visibility = Visibility.Visible;
        }

        private void CloseArtistPicker_Click(object sender, RoutedEventArgs e)
        {
            ArtistPickerBottomSheet.Visibility = Visibility.Collapsed;
        }

        private void ArtistPickerList_ItemClick(object sender, ItemClickEventArgs e)
        {
            string artistName = e.ClickedItem as string;
            CloseArtistPicker_Click(null, null);

            if (!string.IsNullOrEmpty(artistName))
            {
                NowPlayingView.Visibility = Visibility.Collapsed;

                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                string cachedChId = localSettings["AvatarChId_" + artistName.ToLowerInvariant()] as string;

                if (!string.IsNullOrEmpty(cachedChId))
                {
                    OpenArtistProfile(cachedChId, artistName, true);
                }
                else
                {
                    OpenArtistProfile("", artistName);
                }
            }
        }

        private static double[] _playbackSpeeds = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
        private static int _playbackSpeedIndex = 2; // Default 1.0

        private void MenuPlaybackSpeedNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            _playbackSpeedIndex = (_playbackSpeedIndex + 1) % _playbackSpeeds.Length;
            double speed = _playbackSpeeds[_playbackSpeedIndex];
            MenuPlaybackSpeedStatus.Text = speed.ToString("0.0#") + "x";

            var msg = new Windows.Foundation.Collections.ValueSet();
            msg.Add("SetPlaybackRate", speed);
            try { BackgroundMediaPlayer.SendMessageToBackground(msg); } catch { }
        }

        // ══════════════════════════════════════════
        // ADD TO QUEUE — Insert after current track
        // ══════════════════════════════════════════
        private void BottomSheetAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack == null) return;

            // Find current track index in queue
            int currentIdx = -1;
            for (int i = 0; i < currentQueueTracks.Count; i++)
            {
                if (currentTrack != null && currentQueueTracks[i].VideoId == currentTrack.VideoId)
                {
                    currentIdx = i;
                    break;
                }
            }

            // Insert after current track (or at end if not found)
            int insertIdx = currentIdx >= 0 ? currentIdx + 1 : currentQueueTracks.Count;
            
            // Avoid duplicates in queue
            var existing = currentQueueTracks.FirstOrDefault(t => t.VideoId == _bottomSheetTrack.VideoId);
            if (existing != null) currentQueueTracks.Remove(existing);

            if (insertIdx > currentQueueTracks.Count) insertIdx = currentQueueTracks.Count;
            currentQueueTracks.Insert(insertIdx, _bottomSheetTrack);
            UpdateQueueActiveState();
            SyncQueueToBackground(false);

            ShowToast("Added to queue: " + _bottomSheetTrack.Title);
        }

        // ══════════════════════════════════════════
        // GO TO RADIO — Search similar songs & auto-play
        // ══════════════════════════════════════════
        private async void BottomSheetGoToRadio_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            var track = _bottomSheetTrack;
            if (track == null || track.VideoId.StartsWith("LOCAL:")) return;

            ShowToast("Loading radio for " + track.Title + "...");

            try
            {
                var radioResults = await InnerTubeClient.GetRadioTracksAsync(track.VideoId);
                if (radioResults != null && radioResults.Count > 0)
                {
                    searchResults.Clear();
                    searchResults.Add(track);
                    foreach (var t in radioResults)
                    {
                        if (t.VideoId != track.VideoId)
                            searchResults.Add(t);
                    }

                    PlayTrack(track);
                    ShowToast("Radio: " + searchResults.Count + " songs");
                    return;
                }

                // Fallback: search with Music filter for similar songs
                string query = track.Title + " " + track.ChannelName + " similar songs";
                // Use Music/Songs filter param
                var results = await InnerTubeClient.SearchAsync(query, 25, "EgWKAQIIAWoKEAMQBBAKEAkQBQ%3D%3D");
                
                if (results == null || results.Count == 0)
                {
                    // Fallback: search without filter
                    results = await InnerTubeClient.SearchAsync(track.Title + " " + track.ChannelName, 25);
                }

                if (results != null && results.Count > 0)
                {
                    // Filter out non-music results
                    var musicResults = results.Where(t => !t.VideoId.StartsWith("CHANNEL:") && !t.VideoId.StartsWith("PLAYLIST:")).ToList();
                    
                    if (musicResults.Count > 0)
                    {
                        // Put the original track first, then radio results (excluding duplicates)
                        searchResults.Clear();
                        searchResults.Add(track);
                        foreach (var t in musicResults)
                        {
                            if (t.VideoId != track.VideoId)
                                searchResults.Add(t);
                        }

                        // Auto-play from the original track
                        PlayTrack(track);
                        ShowToast("Radio: " + searchResults.Count + " songs");
                    }
                    else
                    {
                        ShowToast("No radio results found");
                    }
                }
                else
                {
                    ShowToast("No radio results found");
                }
            }
            catch
            {
                ShowToast("Failed to load radio");
            }
        }

        // ══════════════════════════════════════════
        // Now Playing Radio — from Now Playing menu
        // ══════════════════════════════════════════
        private void MenuGoToRadioNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
            if (currentTrack != null)
            {
                _bottomSheetTrack = currentTrack;
                BottomSheetGoToRadio_Click(null, null);
            }
        }

        // ══════════════════════════════════════════
        // QUEUE MANAGEMENT — Move Up / Move Down / Remove
        // ══════════════════════════════════════════
        private void QueueMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var track = btn.DataContext as YouTubeTrack;
            if (track == null) return;

            int idx = currentQueueTracks.IndexOf(track);
            if (idx > 0)
            {
                currentQueueTracks.Move(idx, idx - 1);
                UpdateQueueActiveState();
                SyncQueueToBackground(false);
            }
        }

        private void QueueMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var track = btn.DataContext as YouTubeTrack;
            if (track == null) return;

            int idx = currentQueueTracks.IndexOf(track);
            if (idx >= 0 && idx < currentQueueTracks.Count - 1)
            {
                currentQueueTracks.Move(idx, idx + 1);
                UpdateQueueActiveState();
                SyncQueueToBackground(false);
            }
        }

        private void QueueRemove_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var track = btn.DataContext as YouTubeTrack;
            if (track == null) return;

            // Don't remove currently playing track
            if (currentTrack != null && track.VideoId == currentTrack.VideoId)
            {
                ShowToast("Can't remove current track");
                return;
            }

            currentQueueTracks.Remove(track);
            UpdateQueueActiveState();
            SyncQueueToBackground(false);
            ShowToast("Removed from queue");
        }

        // ══════════════════════════════════════════
        // SONG CREDITS DIALOG
        // ══════════════════════════════════════════
        private async void MenuSongCreditsNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
            if (currentTrack == null) return;

            SongCreditsDialog.Visibility = Visibility.Visible;
            SongCreditsLoading.Visibility = Visibility.Visible;
            SongCreditsContent.Visibility = Visibility.Collapsed;

            SongCreditsTrackTitle.Text = currentTrack.Title ?? "";
            CreditsPerformedBySection.Visibility = Visibility.Collapsed;
            CreditsWrittenBySection.Visibility = Visibility.Collapsed;
            CreditsProducedBySection.Visibility = Visibility.Collapsed;
            CreditsProvidedBySection.Visibility = Visibility.Collapsed;
            CreditsViewsText.Text = "--";
            CreditsDateText.Text = "--";
            CreditsAlbumText.Text = !string.IsNullOrEmpty(currentTrack.AlbumName) ? currentTrack.AlbumName : "Single / Album";
            CreditsAudioText.Text = "Opus / AAC";

            try
            {
                var credits = await InnerTubeClient.GetSongCreditsAsync(
                    currentTrack.VideoId,
                    currentTrack.Title,
                    currentTrack.ChannelName,
                    currentTrack.CreditsBrowseId,
                    currentTrack.AlbumName);

                if (credits != null)
                {
                    if (!string.IsNullOrEmpty(credits.PerformedBy))
                    {
                        CreditsPerformedByText.Text = credits.PerformedBy;
                        CreditsPerformedBySection.Visibility = Visibility.Visible;
                    }
                    else if (!string.IsNullOrEmpty(currentTrack.ChannelName))
                    {
                        CreditsPerformedByText.Text = currentTrack.ChannelName;
                        CreditsPerformedBySection.Visibility = Visibility.Visible;
                    }

                    if (!string.IsNullOrEmpty(credits.WrittenBy))
                    {
                        CreditsWrittenByText.Text = credits.WrittenBy;
                        CreditsWrittenBySection.Visibility = Visibility.Visible;
                    }

                    if (!string.IsNullOrEmpty(credits.ProducedBy))
                    {
                        CreditsProducedByText.Text = credits.ProducedBy;
                        CreditsProducedBySection.Visibility = Visibility.Visible;
                    }

                    if (!string.IsNullOrEmpty(credits.ProvidedBy))
                    {
                        CreditsProvidedByText.Text = credits.ProvidedBy;
                        CreditsProvidedBySection.Visibility = Visibility.Visible;
                    }

                    if (!string.IsNullOrEmpty(credits.ViewCount))
                    {
                        CreditsViewsText.Text = credits.ViewCount;
                    }

                    if (!string.IsNullOrEmpty(credits.PublishDate))
                    {
                        CreditsDateText.Text = credits.PublishDate;
                    }

                    if (!string.IsNullOrEmpty(credits.Album))
                    {
                        CreditsAlbumText.Text = credits.Album;
                    }

                    if (!string.IsNullOrEmpty(credits.AudioFormat))
                    {
                        CreditsAudioText.Text = credits.AudioFormat;
                    }
                }
            }
            catch { }
            finally
            {
                SongCreditsLoading.Visibility = Visibility.Collapsed;
                SongCreditsContent.Visibility = Visibility.Visible;
            }
        }

        private void CloseSongCreditsDialog_Click(object sender, RoutedEventArgs e)
        {
            SongCreditsDialog.Visibility = Visibility.Collapsed;
        }

    }
}



