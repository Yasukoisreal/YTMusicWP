using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private void MenuAddToPlaylistNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack != null)
            {
                _trackPendingForPlaylist = currentTrack;
                NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
                AddToPlaylistDialog.Visibility = Visibility.Visible;
            }
        }

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

        private async void MenuWatchLaterNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack == null || currentTrack.VideoId.StartsWith("LOCAL:")) return;
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;

            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
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

        private void NowPlayingPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = NowPlayingPivot.SelectedIndex;
            DotPlayer.Opacity = idx == 0 ? 1.0 : 0.3;
            DotLyrics.Opacity = idx == 1 ? 1.0 : 0.3;
            DotQueue.Opacity  = idx == 2 ? 1.0 : 0.3;
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
            try {
                BottomSheetCover.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(track.ThumbnailUrl)) { DecodePixelWidth = 100 };
            } catch {}

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
                AddToPlaylistDialog.Visibility = Visibility.Visible;
            }
        }

        private void BottomSheetGoToArtist_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomSheet_Click(null, null);
            if (_bottomSheetTrack != null) OpenArtistProfile(_bottomSheetTrack.ChannelId, _bottomSheetTrack.ChannelName);
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

        private void MenuGoToArtistNowPlaying_Click(object sender, RoutedEventArgs e)
        {
            NowPlayingMenuDialog.Visibility = Visibility.Collapsed;
            NowPlayingView.Visibility = Visibility.Collapsed;
            
            if (currentTrack != null)
            {
                OpenArtistProfile(currentTrack.ChannelId, currentTrack.ChannelName);
            }
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
                // Search with Music filter for similar songs
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
            ShowToast("Removed from queue");
        }

    }
}
