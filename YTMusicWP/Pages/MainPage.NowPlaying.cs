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

        private void IncreaseLyricsSize_Click(object sender, RoutedEventArgs e)
        {
            if (_baseLyricSize < 30)
            {
                _baseLyricSize += 2;
                _highlightLyricSize += 2;
                RefreshLyricsSize();
                ShowToast("Lyrics size increased");
            }
        }

        private void DecreaseLyricsSize_Click(object sender, RoutedEventArgs e)
        {
            if (_baseLyricSize > 12)
            {
                _baseLyricSize -= 2;
                _highlightLyricSize -= 2;
                RefreshLyricsSize();
                ShowToast("Lyrics size decreased");
            }
        }

        private void RefreshLyricsSize()
        {
            for (int i = 0; i < currentLyrics.Count; i++)
            {
                currentLyrics[i].FontSize = (i == currentLyricIndex) ? _highlightLyricSize : _baseLyricSize;
            }
        }

        private void SyncMinus_Click(object sender, RoutedEventArgs e)
        {
            foreach (var line in currentLyrics)
            {
                if (line.Text != "") line.Time = line.Time.Add(TimeSpan.FromSeconds(0.5));
            }
            currentLyricIndex = -1;
            ShowToast("Lyrics delayed by 0.5s");
        }

        private void SyncPlus_Click(object sender, RoutedEventArgs e)
        {
            foreach (var line in currentLyrics)
            {
                if (line.Text != "" && line.Time.TotalSeconds >= 0.5)
                    line.Time = line.Time.Subtract(TimeSpan.FromSeconds(0.5));
            }
            currentLyricIndex = -1;
            ShowToast("Lyrics advanced by 0.5s");
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

    }
}
