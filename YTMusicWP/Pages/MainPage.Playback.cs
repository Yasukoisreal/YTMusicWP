using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private async void PlayTrack(YouTubeTrack track)
        {
            if (track == null || string.IsNullOrEmpty(track.VideoId)) return;
            if (track.VideoId.StartsWith("CHANNEL:"))
            {
                OpenArtistProfile(track.VideoId.Substring(8), track.Title ?? track.ChannelName, true);
                return;
            }
            if (track.VideoId.StartsWith("PLAYLIST:"))
            {
                OpenYouTubePlaylist(track.VideoId.Substring(9), track.Title, track.ThumbnailUrl);
                return;
            }

            try { _appMediaPlayer.Pause(); } catch { }

            if (!track.VideoId.StartsWith("LOCAL:") && !IsInternetAvailable()) { ShowToast("No Internet connection"); return; }

            currentTrack = track;
            MiniTitle.Text = track.Title; BigTitle.Text = track.Title;
            // Start marquee only if NowPlaying is already open (otherwise it starts when panel opens)
            if (NowPlayingView.Visibility == Visibility.Visible)
            {
                var ignored2 = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => StartTitleMarquee());
            }
            MiniArtist.Text = track.ChannelName; BigArtist.Text = track.ChannelName;
            SetPlayPauseIcon(true);
            MenuTitle.Text = track.Title;
            MenuArtist.Text = track.ChannelName;

            if (!string.IsNullOrEmpty(track.ThumbnailUrl))
            {
                // [OPT-M3] Dùng chung 1 BitmapImage cho BigCover + MenuCover (cùng src, cùng DecodePixelWidth)
                var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetNowPlayingThumbnail(track.ThumbnailUrl), UriKind.Absolute));
                bigBmp.DecodePixelWidth = 480;
                BigCoverImage.ImageSource  = bigBmp;
                AlbumArtEntranceStoryboard.Begin();
                MenuCoverImage.ImageSource = bigBmp;

                var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetSquareThumbnail(track.ThumbnailUrl), UriKind.Absolute));
                miniBmp.DecodePixelWidth = 100;
                MiniCoverImage.ImageSource = miniBmp;
            }

            bool isFav = favoriteTracks.Any(t => t.VideoId == track.VideoId);
            BigHeartBtn.Content = isFav ? "♥" : "♡";
            BigHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;

            var ignored = UpdateLyricsAsync(track.Title, track.ChannelName);
            UpdateNowPlayingGradient(track.Title, track.ChannelName, track.ThumbnailUrl);
            if (_isAppleMusicStyle)
            {
                UpdateAppleMusicCompactHeaders();
            }

            YTMusicWP.Services.TileService.UpdateNowPlayingWithQueue(track.Title, track.ChannelName, track.ThumbnailUrl, null);

            // BUG FIX: Xác định activeList TRƯỚC khi insert vào history.
            // Nếu detect sau khi insert, historyTracks.Contains(track) sẽ luôn true,
            // gây mất nguồn context thực sự (search, playlist, favorites...).
            ObservableCollection<YouTubeTrack> activeList = homeTracks;
            if (currentQueueTracks.Contains(track)) activeList = currentQueueTracks;
            else if (searchResults.Contains(track)) activeList = searchResults;
            else if (favoriteTracks.Contains(track)) activeList = favoriteTracks;
            else if (downloadedTracks.Contains(track)) activeList = downloadedTracks;
            else if (homeHistoryCarouselTracks.Contains(track)) activeList = homeHistoryCarouselTracks;
            else if (historyQuickGridTracks.Contains(track)) activeList = historyQuickGridTracks;
            else if (podcastTracks.Contains(track)) activeList = podcastTracks;
            else if (audiobookTracks.Contains(track)) activeList = audiobookTracks;
            else if (PlaylistDetailsView != null && PlaylistDetailsView.Visibility == Visibility.Visible && PlaylistSongsList != null && PlaylistSongsList.ItemsSource != null && ((IEnumerable<YouTubeTrack>)PlaylistSongsList.ItemsSource).Contains(track))
            {
                activeList = new ObservableCollection<YouTubeTrack>((IEnumerable<YouTubeTrack>)PlaylistSongsList.ItemsSource);
            }
            else if (historyTracks.Contains(track)) activeList = historyTracks;
            else if (_currentViewingPlaylist != null && _currentViewingPlaylist.Tracks.Contains(track)) activeList = _currentViewingPlaylist.Tracks;
            else if (ArtistSongsList.ItemsSource != null) { var artistList = ArtistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>; if (artistList != null && artistList.Contains(track)) activeList = artistList; }
            else if (HomeDynamicSections.ItemsSource != null)
            {
                var sections = HomeDynamicSections.ItemsSource as System.Collections.Generic.IEnumerable<YTMusicWP.InnerTubeClient.HomeSection>;
                if (sections != null)
                {
                    var foundSection = System.Linq.Enumerable.FirstOrDefault(sections, s => s.Tracks.Contains(track));
                    if (foundSection != null)
                        activeList = new ObservableCollection<YouTubeTrack>(foundSection.Tracks);
                }
            }

            // SAFETY FALLBACK: Prevents IndexOutOfRangeException if track is not in activeList
            if (activeList == null || !activeList.Contains(track))
            {
                activeList = new ObservableCollection<YouTubeTrack> { track };
            }

            // Cập nhật lịch sử SAU khi đã chọn activeList
            var existingHistory = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
            if (existingHistory != null) historyTracks.Remove(existingHistory);
            historyTracks.Insert(0, track);
            if (historyTracks.Count > 50) historyTracks.RemoveAt(historyTracks.Count - 1);
            var ignoredHistory = YTMusicWP.Services.DatabaseHelper.AddOrUpdateHistoryAsync(track);
            RefreshHomeHistorySections();

            // Resolve URL cho bài hiện tại từ foreground (HttpClient mạnh hơn AudioTask)
            string resolvedUrl = "";
            if (!track.VideoId.StartsWith("LOCAL:"))
            {
                try
                {
                    resolvedUrl = await InnerTubeClient.ResolveStreamUrlAsync(track.VideoId) ?? "";
                }
                catch { resolvedUrl = ""; }
            }

            // OPTIMIZATION: Giới hạn mảng gửi sang BackgroundTask để tránh lỗi IPC Payload quá tải (RAM 512MB)
            int maxItems = 100;
            int half = maxItems / 2;
            int sliceStart = Math.Max(0, activeList.IndexOf(track) - half);
            int sliceEnd = Math.Min(activeList.Count - 1, activeList.IndexOf(track) + half);
            int count = sliceEnd - sliceStart + 1;
            int relativeStartIndex = activeList.IndexOf(track) - sliceStart;
            if (relativeStartIndex < 0) relativeStartIndex = 0;

            string[] urls = new string[count];
            string[] titles = new string[count];
            string[] artists = new string[count];
            string[] videoIds = new string[count];
            string[] thumbnails = new string[count];

            if (activeList != currentQueueTracks)
            {
                QueueListView.ItemsSource = null;
                currentQueueTracks.Clear();
                for (int i = 0; i < activeList.Count; i++) currentQueueTracks.Add(activeList[i]);
                QueueListView.ItemsSource = currentQueueTracks;
            }

            for (int i = 0; i < count; i++)
            {
                var t = activeList[sliceStart + i];
                if (i == relativeStartIndex && !string.IsNullOrEmpty(resolvedUrl))
                    urls[i] = resolvedUrl; 
                else
                    urls[i] = t.VideoId.StartsWith("LOCAL:")
                        ? "ms-appdata:///local/" + t.VideoId.Substring(6)
                        : ""; 
                titles[i] = t.Title ?? "";
                artists[i] = t.ChannelName ?? "";
                videoIds[i] = t.VideoId ?? "";
                thumbnails[i] = t.ThumbnailUrl ?? "";
            }

            // Update Live Tile with upcoming queue
            YTMusicWP.Services.TileService.UpdateNowPlayingWithQueue(track.Title, track.ChannelName, track.ThumbnailUrl, activeList.Skip(activeList.IndexOf(track) + 1));

            var message = new ValueSet {
                { "UpdatePlaylist", "" }, { "Urls", urls }, { "Titles", titles }, { "Artists", artists },
                { "VideoIds", videoIds }, { "Thumbnails", thumbnails }, { "StartIndex", relativeStartIndex }, { "FastUrl", urls[relativeStartIndex] }
            };
            try { BackgroundMediaPlayer.SendMessageToBackground(message); } catch { }

            UpdateQueueActiveState();
            TriggerAutoplayIfNearingEndAsync(track);
        }

        private void SyncQueueToBackground(bool playImmediate = false)
        {
            if (currentQueueTracks.Count == 0) return;
            
            // Tìm currentIndex thực sự trong mảng UI
            int actualCurrentIndex = -1;
            if (currentTrack != null) {
                for (int i = 0; i < currentQueueTracks.Count; i++) {
                    if (currentQueueTracks[i].VideoId == currentTrack.VideoId) { actualCurrentIndex = i; break; }
                }
            }
            if (actualCurrentIndex == -1) actualCurrentIndex = 0;

            int maxItems = 100;
            int half = maxItems / 2;
            int sliceStart = Math.Max(0, actualCurrentIndex - half);
            int sliceEnd = Math.Min(currentQueueTracks.Count - 1, actualCurrentIndex + half);
            int count = sliceEnd - sliceStart + 1;
            int relativeStartIndex = actualCurrentIndex - sliceStart;

            string[] urls = new string[count];
            string[] titles = new string[count];
            string[] artists = new string[count];
            string[] videoIds = new string[count];
            string[] thumbnails = new string[count];

            for (int i = 0; i < count; i++)
            {
                var t = currentQueueTracks[sliceStart + i];
                urls[i] = t.VideoId.StartsWith("LOCAL:")
                    ? "ms-appdata:///local/" + t.VideoId.Substring(6)
                    : "";
                titles[i] = t.Title ?? "";
                artists[i] = t.ChannelName ?? "";
                videoIds[i] = t.VideoId ?? "";
                thumbnails[i] = t.ThumbnailUrl ?? "";
            }

            var message = new ValueSet {
                { playImmediate ? "UpdatePlaylist" : "UpdateQueueOnly", "" },
                { "Urls", urls },
                { "Titles", titles },
                { "Artists", artists },
                { "VideoIds", videoIds },
                { "Thumbnails", thumbnails }
            };

            if (playImmediate)
            {
                message.Add("StartIndex", relativeStartIndex);
            }
            else
            {
                message.Add("CurrentIndex", relativeStartIndex);
            }

            try { BackgroundMediaPlayer.SendMessageToBackground(message); } catch { }

            if (currentTrack != null)
            {
                YTMusicWP.Services.TileService.UpdateNowPlayingWithQueue(currentTrack.Title, currentTrack.ChannelName, currentTrack.ThumbnailUrl, currentQueueTracks.Skip(actualCurrentIndex + 1));
            }
        }

        public void UpdateQueueActiveState()
        {
            if (currentQueueTracks == null) return;
            string currentVid = currentTrack?.VideoId;
            for (int i = 0; i < currentQueueTracks.Count; i++)
            {
                var item = currentQueueTracks[i];
                item.IsPlaying = (!string.IsNullOrEmpty(currentVid) && item.VideoId == currentVid);
            }
            if (QueueCountText != null)
            {
                QueueCountText.Text = currentQueueTracks.Count > 0 ? "(" + currentQueueTracks.Count + ")" : "";
            }
        }

        private bool _isFetchingAutoplay = false;

        private async void TriggerAutoplayIfNearingEndAsync(YouTubeTrack track)
        {
            if (track == null || string.IsNullOrEmpty(track.VideoId) || track.VideoId.StartsWith("LOCAL:")) return;
            if (_isFetchingAutoplay) return;

            var settings = ApplicationData.Current.LocalSettings.Values;
            bool autoplay = settings.ContainsKey("Autoplay") ? (bool)settings["Autoplay"] : true;
            if (!autoplay) return;

            int idx = -1;
            for (int i = 0; i < currentQueueTracks.Count; i++)
            {
                if (currentQueueTracks[i].VideoId == track.VideoId)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < currentQueueTracks.Count - 2) return;

            _isFetchingAutoplay = true;
            try
            {
                var radioTracks = await InnerTubeClient.GetRadioTracksAsync(track.VideoId);
                if (radioTracks == null || radioTracks.Count == 0)
                {
                    radioTracks = await InnerTubeClient.SearchAsync(track.Title + " " + track.ChannelName, 20);
                }

                if (radioTracks != null && radioTracks.Count > 0)
                {
                    int added = 0;
                    foreach (var item in radioTracks)
                    {
                        if (item.VideoId.StartsWith("CHANNEL:") || item.VideoId.StartsWith("PLAYLIST:")) continue;
                        if (!currentQueueTracks.Any(t => t.VideoId == item.VideoId))
                        {
                            currentQueueTracks.Add(item);
                            added++;
                            if (added >= 15) break;
                        }
                    }

                    if (added > 0)
                    {
                        UpdateQueueActiveState();
                        SyncQueueToBackground(false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Autoplay] Failed to fetch radio tracks: " + ex.Message);
            }
            finally
            {
                _isFetchingAutoplay = false;
            }
        }

        private async void HeartButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack == null) return;

            // Require login to like songs
            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth)
            {
                ShowToast("Sign in to like songs");
                return;
            }

            var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == currentTrack.VideoId);
            bool isAdding = (existing == null);

            if (existing != null) 
            { 
                favoriteTracks.Remove(existing); 
                BigHeartBtn.Content = "♡"; 
                BigHeartBtn.Foreground = _whiteBrush; 
                if (AppleMusicMainHeartBtn != null) { AppleMusicMainHeartBtn.Content = "☆"; AppleMusicMainHeartBtn.Foreground = _whiteBrush; }
                if (AppleMusicLyricsHeartBtn != null) { AppleMusicLyricsHeartBtn.Content = "♡"; AppleMusicLyricsHeartBtn.Foreground = _whiteBrush; }
                if (AppleMusicQueueHeartBtn != null) { AppleMusicQueueHeartBtn.Content = "♡"; AppleMusicQueueHeartBtn.Foreground = _whiteBrush; }
                var _ = YTMusicWP.Services.DatabaseHelper.RemoveFavoriteAsync(currentTrack.VideoId);
            }
            else 
            { 
                favoriteTracks.Insert(0, currentTrack); 
                BigHeartBtn.Content = "♥"; 
                BigHeartBtn.Foreground = _greenBrush; 
                if (AppleMusicMainHeartBtn != null) { AppleMusicMainHeartBtn.Content = "★"; AppleMusicMainHeartBtn.Foreground = _whiteBrush; }
                if (AppleMusicLyricsHeartBtn != null) { AppleMusicLyricsHeartBtn.Content = "♥"; AppleMusicLyricsHeartBtn.Foreground = _greenBrush; }
                if (AppleMusicQueueHeartBtn != null) { AppleMusicQueueHeartBtn.Content = "♥"; AppleMusicQueueHeartBtn.Foreground = _greenBrush; }
                var _ = YTMusicWP.Services.DatabaseHelper.AddFavoriteAsync(currentTrack);
            }

            // Sync to YouTube (skip LOCAL tracks that can't be rated)
            if (!currentTrack.VideoId.StartsWith("LOCAL:"))
            {
                string rating = isAdding ? "like" : "none";
                await RateVideoAsync(currentTrack.VideoId, rating);
            }
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;
            bool newState = !(settings.Values.ContainsKey("ShuffleMode") ? (bool)settings.Values["ShuffleMode"] : false);
            settings.Values["ShuffleMode"] = newState;
            ShuffleIcon.Foreground = newState ? _greenBrush : _whiteBrush;
            ShuffleDot.Visibility = newState ? Visibility.Visible : Visibility.Collapsed;
            if (AppleMusicShuffleIcon != null)
                AppleMusicShuffleIcon.Foreground = newState ? _greenBrush : _whiteBrush;
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;
            int newMode = (settings.Values.ContainsKey("RepeatMode") ? (int)settings.Values["RepeatMode"] : 0) + 1;
            if (newMode > 2) newMode = 0;
            settings.Values["RepeatMode"] = newMode;
            UpdateRepeatUI(newMode);
        }

        private void UpdateRepeatUI(int mode)
        {
            if (mode == 0) { RepeatIcon.Glyph = "\uE1CD"; RepeatIcon.Foreground = _whiteBrush; RepeatDot.Visibility = Visibility.Collapsed; }
            else if (mode == 1) { RepeatIcon.Glyph = "\uE1CD"; RepeatIcon.Foreground = _greenBrush; RepeatDot.Visibility = Visibility.Visible; }
            else if (mode == 2) { RepeatIcon.Glyph = "\uE1CC"; RepeatIcon.Foreground = _greenBrush; RepeatDot.Visibility = Visibility.Visible; }

            if (AppleMusicRepeatIcon != null)
            {
                if (mode == 0) { AppleMusicRepeatIcon.Glyph = "\uE1CD"; AppleMusicRepeatIcon.Foreground = _whiteBrush; }
                else if (mode == 1) { AppleMusicRepeatIcon.Glyph = "\uE1CD"; AppleMusicRepeatIcon.Foreground = _greenBrush; }
                else if (mode == 2) { AppleMusicRepeatIcon.Glyph = "\uE1CC"; AppleMusicRepeatIcon.Foreground = _greenBrush; }
            }
        }

        private void SetupTimer()
        {
            try { _appMediaPlayer.CurrentStateChanged += BackgroundMediaPlayer_CurrentStateChanged; } catch { }
            _bgTimer = new Timer(TimerCallback, null, 0, 1000);
        }

        private async void BackgroundMediaPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        SetPlayPauseIcon(sender.CurrentState == MediaPlayerState.Playing);
                    }
                    catch { }
                });
            }
            catch { }
        }

        private async void TimerCallback(object state)
        {
            // [OPT-P1] Bail-out sớm TRƯỚC khi gọi Dispatcher — tiết kiệm thread switch trên WP8.1
            if (_isSliderManipulating) return;
            MediaPlayer session;
            try { session = _appMediaPlayer; } catch { return; }
            if (session == null || session.CurrentState != MediaPlayerState.Playing) return;

            TimeSpan pos, dur;
            try { pos = session.Position; dur = session.NaturalDuration; } catch { return; }
            if (dur.TotalSeconds <= 0) return;

            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    try
                    {
                        MusicSlider.Maximum    = dur.TotalSeconds;
                        MusicSlider.Value      = pos.TotalSeconds;
                        CurrentTimeText.Text   = pos.ToString(@"m\:ss");
                        TotalTimeText.Text     = dur.ToString(@"m\:ss");
                        MiniProgressBar.Maximum = dur.TotalSeconds;
                        MiniProgressBar.Value   = pos.TotalSeconds;

                        if (_isAppleMusicStyle)
                        {
                            if (AppleMusicSlider != null)
                            {
                                AppleMusicSlider.Maximum = dur.TotalSeconds;
                                AppleMusicSlider.Value = pos.TotalSeconds;
                            }
                            if (AppleMusicCurrentTime != null) AppleMusicCurrentTime.Text = CurrentTimeText.Text;
                            if (AppleMusicRemainingTime != null && dur.TotalSeconds > 0)
                            {
                                var remain = Math.Max(0, dur.TotalSeconds - pos.TotalSeconds);
                                AppleMusicRemainingTime.Text = "-" + string.Format("{0}:{1:D2}", (int)remain / 60, (int)remain % 60);
                            }
                        }

                        if (NowPlayingView.Visibility != Visibility.Visible && FullscreenLyricsView.Visibility != Visibility.Visible) return;
                        bool isFullscreen = FullscreenLyricsView.Visibility == Visibility.Visible;
                        if (currentLyrics.Count == 0) return;
                        bool isLyricsUIVisible = isFullscreen || (NowPlayingPivot.SelectedIndex == 1 && LyricsListView.Visibility == Visibility.Visible);
                        bool isMainScreenVisible = (!isFullscreen && NowPlayingPivot.SelectedIndex == 0);
                        if (!isLyricsUIVisible && !isMainScreenVisible) return;

                        int newIndex = -1;
                        for (int i = 0; i < currentLyrics.Count; i++)
                        {
                            if (pos >= currentLyrics[i].Time.Subtract(TimeSpan.FromSeconds(0.2))) newIndex = i;
                            else break;
                        }

                        if (newIndex == currentLyricIndex || newIndex < 0) return;

                        int oldIndex = currentLyricIndex;
                        currentLyricIndex = newIndex;

                        if (isLyricsUIVisible)
                        {
                            // Target ListView = fullscreen or regular
                        var targetListView = isFullscreen ? FullscreenLyricsListView : LyricsListView;

                        // Animate OLD lyric
                        if (oldIndex >= 0 && oldIndex < currentLyrics.Count)
                        {
                            currentLyrics[oldIndex].ColorBrush = _isAppleMusicStyle ? _appleMusicLyricInactiveBrush : _lyricInactiveBrush;

                            if (!isFullscreen && !_isAppleMusicStyle)
                            {
                                var oldContainer = targetListView.ContainerFromIndex(oldIndex) as FrameworkElement;
                                if (oldContainer != null)
                                {
                                    var oldScale = oldContainer.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                                    if (oldScale == null)
                                    {
                                        oldScale = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 1, ScaleY = 1 };
                                        oldContainer.RenderTransformOrigin = new Point(0, 0.5);
                                        oldContainer.RenderTransform = oldScale;
                                    }
                                    // [OPT-1] Reuse cached easing + single storyboard
                                    AnimateLyricOut(oldContainer, oldScale);
                                }
                                else { currentLyrics[oldIndex].Opacity = 0.5; }
                            }
                            else if (isFullscreen)
                            {
                                var oldContainer = targetListView.ContainerFromIndex(oldIndex) as FrameworkElement;
                                if (oldContainer != null) AnimateOpacity(oldContainer, 0.5);
                            }
                        }

                        // Animate NEW lyric
                        currentLyrics[currentLyricIndex].ColorBrush = _lyricActiveBrush;

                        if (isFullscreen)
                        {
                            var fsContainer = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                            if (fsContainer != null) AnimateOpacity(fsContainer, 1.0);
                        }
                        else if (_isAppleMusicStyle)
                        {
                            for (int i = 0; i < currentLyrics.Count; i++)
                            {
                                var container = targetListView.ContainerFromIndex(i) as FrameworkElement;
                                if (container == null) continue;
                                int dist = Math.Abs(i - currentLyricIndex);
                                if (i < currentLyricIndex) dist += 1;
                                container.Opacity = (i == currentLyricIndex) ? 1.0 : Math.Max(0.25, 1.0 - dist * 0.25);
                                container.RenderTransform = null;
                            }
                        }
                        else
                        {
                            var newContainer = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                            if (newContainer != null)
                            {
                                var scaleTransform = newContainer.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                                if (scaleTransform == null)
                                {
                                    scaleTransform = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 0.85, ScaleY = 0.85 };
                                    newContainer.RenderTransformOrigin = new Point(0, 0.5);
                                    newContainer.RenderTransform = scaleTransform;
                                }
                                // [OPT-1] Reuse cached easing + single storyboard
                                AnimateLyricIn(newContainer, scaleTransform);
                            }
                            else { currentLyrics[currentLyricIndex].Opacity = 1.0; }
                        }

                        // Smooth center-scroll (replaces abrupt ScrollIntoView)
                        ScrollViewer scrollViewer;
                        if (isFullscreen)
                        {
                            if (_cachedFullscreenLyricsScrollViewer == null)
                                _cachedFullscreenLyricsScrollViewer = GetScrollViewer(FullscreenLyricsListView);
                            scrollViewer = _cachedFullscreenLyricsScrollViewer;
                        }
                        else
                        {
                            if (_cachedLyricsScrollViewer == null)
                                _cachedLyricsScrollViewer = GetScrollViewer(LyricsListView);
                            scrollViewer = _cachedLyricsScrollViewer;
                        }

                        var activeContainer = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                        if (activeContainer == null)
                        {
                            // Item is virtualized out of view because user seeked far away.
                            targetListView.ScrollIntoView(currentLyrics[currentLyricIndex]);
                            targetListView.UpdateLayout();
                            activeContainer = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                        }
                        
                        if (scrollViewer != null && activeContainer != null)
                        {
                            var transform    = activeContainer.TransformToVisual(scrollViewer);
                            var lyricPos     = transform.TransformPoint(new Point(0, 0));
                            double targetOff = scrollViewer.VerticalOffset + lyricPos.Y
                                            - (scrollViewer.ViewportHeight / 2.0)
                                            + (activeContainer.ActualHeight / 2.0);
                            scrollViewer.ChangeView(null, targetOff, null, false);
                        }
                        }

                        if (isMainScreenVisible)
                        {
                            UpdateMiniLyric(currentLyrics[currentLyricIndex].Text);
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void MusicSlider_PointerPressed(object sender, PointerRoutedEventArgs e) => _isSliderManipulating = true;
        private void MusicSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isSliderManipulating = false;
            try
            {
                if (_appMediaPlayer.CurrentState != MediaPlayerState.Closed)
                {
                    // FIX #7: Clamp giá trị seek để không bị âm với clip ngắn
                    var slider = (sender as Slider) ?? MusicSlider;
                    _appMediaPlayer.Position = TimeSpan.FromSeconds(Math.Min(slider.Value, Math.Max(0, _appMediaPlayer.NaturalDuration.TotalSeconds - 2)));
                    if (_appMediaPlayer.CurrentState == MediaPlayerState.Paused) _appMediaPlayer.Play();
                }
            }
            catch { }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // FIX: Nhận diện OS đã tự đóng Background Task (State = Closed) để ra lệnh phát lại từ đầu
                if (_appMediaPlayer.CurrentState == MediaPlayerState.Closed)
                {
                    if (currentTrack != null) PlayTrack(currentTrack);
                }
                else if (_appMediaPlayer.CurrentState == MediaPlayerState.Playing)
                {
                    _appMediaPlayer.Pause();
                }
                else
                {
                    _appMediaPlayer.Play();
                }
            }
            catch { }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_appMediaPlayer.Position.TotalSeconds > 3)
                {
                    _appMediaPlayer.Position = TimeSpan.Zero;
                    _appMediaPlayer.Play();
                }
                else
                {
                    BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { "PrevTrackMessage", "" } });
                }
            }
            catch { try { BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { "PrevTrackMessage", "" } }); } catch { } }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Always send NextTrackMessage — AudioTask handles Closed state internally
                BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { "NextTrackMessage", "" } });
            }
            catch
            {
                // If background task is truly dead, re-play current track to wake it up
                try { if (currentTrack != null) PlayTrack(currentTrack); } catch { }
            }
        }


        private void SyncBackgroundPlayer()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings.Values;

                if (_appMediaPlayer.CurrentState == MediaPlayerState.Playing || _appMediaPlayer.CurrentState == MediaPlayerState.Paused)
                {
                    bool isPlaying = (_appMediaPlayer.CurrentState == MediaPlayerState.Playing);
                    SetPlayPauseIcon(isPlaying);

                    string title = localSettings.ContainsKey("CurrentTitle") ? localSettings["CurrentTitle"].ToString() : "Unknown";
                    string artist = localSettings.ContainsKey("CurrentArtist") ? localSettings["CurrentArtist"].ToString() : "Unknown";
                    string vid = localSettings.ContainsKey("CurrentVideoId") ? localSettings["CurrentVideoId"].ToString() : "";
                    string thumb = localSettings.ContainsKey("CurrentThumbnail") ? localSettings["CurrentThumbnail"].ToString() : "";

                    // [OPT-M8] Skip nếu track đã sync — tránh tạo BitmapImage mới khi resume
                    if (currentTrack != null && currentTrack.VideoId == vid)
                    {
                        return;
                    }

                    MiniTitle.Text = title; BigTitle.Text = title;
                    MiniArtist.Text = artist; BigArtist.Text = artist;

                    MenuTitle.Text = title;
                    MenuArtist.Text = artist;

                    if (!string.IsNullOrEmpty(thumb))
                    {
                        try
                        {
                            string finalThumbUrl = GetNowPlayingThumbnail(thumb);
                            bool isWide = finalThumbUrl.Contains("w540-h304") || finalThumbUrl.Contains("mqdefault");

                            if (isWide)
                            {
                                BigCoverRectangle.Width = 360;
                                BigCoverRectangle.Height = 202;
                                BigCoverShadow.Width = 350;
                                BigCoverShadow.Height = 192;
                                MiniCoverRectangle.Width = 82;
                            }
                            else
                            {
                                BigCoverRectangle.Width = 300;
                                BigCoverRectangle.Height = 300;
                                BigCoverShadow.Width = 290;
                                BigCoverShadow.Height = 290;
                                MiniCoverRectangle.Width = 46;
                            }

                            var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            bigBmp.DecodePixelWidth = isWide ? 540 : 480;
                            bigBmp.UriSource = new Uri(finalThumbUrl, UriKind.Absolute);
                            BigCoverImage.ImageSource = bigBmp;
                            if (AppleMusicArtwork != null) AppleMusicArtwork.Source = bigBmp;
                            AlbumArtEntranceStoryboard.Begin();
                            MenuCoverImage.ImageSource = bigBmp;

                            var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            miniBmp.DecodePixelWidth = isWide ? 150 : 100;
                            miniBmp.UriSource = new Uri(GetSquareThumbnail(thumb), UriKind.Absolute);
                            MiniCoverImage.ImageSource = miniBmp;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(vid))
                    {
                        currentTrack = new YouTubeTrack { VideoId = vid, Title = title, ChannelName = artist, ThumbnailUrl = thumb };
                        bool isFav = favoriteTracks.Any(t => t.VideoId == vid);
                        BigHeartBtn.Content = isFav ? "♥" : "♡";
                        BigHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;

                        var ignored = UpdateLyricsAsync(title, artist);
                        UpdateNowPlayingGradient(title, artist, thumb);
                        if (_isAppleMusicStyle)
                        {
                            UpdateAppleMusicCompactHeaders();
                        }
                    }
                }
            }
            catch { }
        }

        private async void BackgroundMediaPlayer_MessageReceivedFromBackground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            if (e.Data.ContainsKey("ToastMessage"))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    ShowToast(e.Data["ToastMessage"].ToString());
                });
            }

            if (e.Data.ContainsKey("TrackChanged"))
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    string title = e.Data["NewTitle"]?.ToString() ?? "";
                    string artist = e.Data["NewArtist"]?.ToString() ?? "";
                    string vid = e.Data.ContainsKey("NewVideoId") ? e.Data["NewVideoId"].ToString() : "";
                    string thumb = e.Data.ContainsKey("NewThumbnail") ? e.Data["NewThumbnail"].ToString() : "";

                    MiniTitle.Text = title; BigTitle.Text = title;
                    MiniArtist.Text = artist; BigArtist.Text = artist;

                    MenuTitle.Text = title;
                    MenuArtist.Text = artist;

                    if (!string.IsNullOrEmpty(thumb))
                    {
                        try
                        {
                            string finalThumbUrl = GetNowPlayingThumbnail(thumb);
                            bool isWide = finalThumbUrl.Contains("w540-h304") || finalThumbUrl.Contains("mqdefault");

                            if (isWide)
                            {
                                BigCoverRectangle.Width = 360;
                                BigCoverRectangle.Height = 202;
                                BigCoverShadow.Width = 350;
                                BigCoverShadow.Height = 192;
                                MiniCoverRectangle.Width = 82;
                            }
                            else
                            {
                                BigCoverRectangle.Width = 300;
                                BigCoverRectangle.Height = 300;
                                BigCoverShadow.Width = 290;
                                BigCoverShadow.Height = 290;
                                MiniCoverRectangle.Width = 46;
                            }

                            var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            bigBmp.DecodePixelWidth = isWide ? 540 : 480;
                            bigBmp.UriSource = new Uri(finalThumbUrl, UriKind.Absolute);
                            BigCoverImage.ImageSource = bigBmp;
                            if (AppleMusicArtwork != null) AppleMusicArtwork.Source = bigBmp;
                            AlbumArtEntranceStoryboard.Begin();
                            MenuCoverImage.ImageSource = bigBmp;

                            var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            miniBmp.DecodePixelWidth = isWide ? 150 : 100;
                            miniBmp.UriSource = new Uri(GetSquareThumbnail(thumb), UriKind.Absolute);
                            MiniCoverImage.ImageSource = miniBmp;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(vid))
                    {
                        currentTrack = new YouTubeTrack { VideoId = vid, Title = title, ChannelName = artist, ThumbnailUrl = thumb };

                        bool isFav = favoriteTracks.Any(t => t.VideoId == vid);
                        BigHeartBtn.Content = isFav ? "♥" : "♡";
                        BigHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;

                        var existingHistory = historyTracks.FirstOrDefault(t => t.VideoId == vid);
                        if (existingHistory != null) historyTracks.Remove(existingHistory);
                        historyTracks.Insert(0, currentTrack);
                        if (historyTracks.Count > 50) historyTracks.RemoveAt(historyTracks.Count - 1);
                        var _ = YTMusicWP.Services.DatabaseHelper.AddOrUpdateHistoryAsync(currentTrack);

                        var ignored = UpdateLyricsAsync(title, artist);
                        UpdateNowPlayingGradient(title, artist, thumb);
                        if (_isAppleMusicStyle)
                        {
                            UpdateAppleMusicCompactHeaders();
                        }
                        YTMusicWP.Services.TileService.UpdateNowPlayingWithQueue(title, artist, thumb, currentQueueTracks);
                        UpdateQueueActiveState();
                        TriggerAutoplayIfNearingEndAsync(currentTrack);

                        // Restart marquee if NowPlaying is visible
                        if (NowPlayingView.Visibility == Visibility.Visible)
                        {
                            var ignored2 = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => StartTitleMarquee());
                        }
                    }
                });
            }
        }

        // ── Dynamic & Genre-Based Gradient for Now Playing ──
        private Windows.UI.Color _currentGradientColor = Windows.UI.Color.FromArgb(255, 30, 50, 70);
        private Windows.UI.Color _currentStatusBarColor = Windows.UI.Color.FromArgb(255, 18, 18, 18);
        private CancellationTokenSource _statusBarAnimCts;
        private static readonly Dictionary<string, Windows.UI.Color> _dominantColorCache = new Dictionary<string, Windows.UI.Color>();
        private static readonly HttpClient _dominantHttpClient = CreateDominantHttpClient();
        private int _gradientSequence = 0;

        private static HttpClient CreateDominantHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            try
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            }
            catch { }
            return client;
        }

        private static string GetDominantColorThumbnailUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (url.Contains("googleusercontent.com") || url.Contains("ggpht.com"))
            {
                int eqIdx = url.LastIndexOf("=");
                if (eqIdx > 0)
                    return url.Substring(0, eqIdx) + "=w120-h120-l90-rj";
                return url + "=w120-h120-l90-rj";
            }
            if (url.Contains("ytimg.com") || url.Contains("img.youtube.com"))
            {
                int viIdx = url.IndexOf("/vi/");
                if (viIdx > 0)
                {
                    int endIdx = url.IndexOf("/", viIdx + 4);
                    if (endIdx > 0)
                    {
                        string vidId = url.Substring(viIdx + 4, endIdx - (viIdx + 4));
                        return "https://i.ytimg.com/vi/" + vidId + "/mqdefault.jpg";
                    }
                }
                if (url.Contains("maxresdefault.jpg")) return url.Replace("maxresdefault.jpg", "mqdefault.jpg");
                if (url.Contains("hqdefault.jpg")) return url.Replace("hqdefault.jpg", "mqdefault.jpg");
                if (url.Contains("sddefault.jpg")) return url.Replace("sddefault.jpg", "mqdefault.jpg");
            }
            return url;
        }

        private async Task<Windows.UI.Color?> ExtractDominantColorAsync(string thumbUrl)
        {
            if (string.IsNullOrEmpty(thumbUrl)) return null;
            if (thumbUrl.StartsWith("ms-appx:") || thumbUrl.StartsWith("ms-appdata:")) return null;

            lock (_dominantColorCache)
            {
                Windows.UI.Color cached;
                if (_dominantColorCache.TryGetValue(thumbUrl, out cached))
                    return cached;
            }

            try
            {
                string cleanUrl = GetDominantColorThumbnailUrl(thumbUrl);
                byte[] imgBytes = await _dominantHttpClient.GetByteArrayAsync(cleanUrl);
                if (imgBytes == null || imgBytes.Length == 0) return null;

                using (var inStream = new InMemoryRandomAccessStream())
                {
                    using (var writer = new DataWriter(inStream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(imgBytes);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }
                    inStream.Seek(0);

                    var decoder = await BitmapDecoder.CreateAsync(inStream);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = 24,
                        ScaledHeight = 24,
                        InterpolationMode = BitmapInterpolationMode.Linear
                    };

                    var pixelData = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.ColorManageToSRgb);

                    byte[] pixels = pixelData.DetachPixelData();
                    if (pixels == null || pixels.Length < 4) return null;

                    // Analyze pixels to find dominant vibrant color
                    double bestScore = -1.0;
                    byte bestR = 30, bestG = 50, bestB = 70;
                    long totalR = 0, totalG = 0, totalB = 0;
                    int validCount = 0;

                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];

                        if (a < 128) continue;

                        // Perceived luminance
                        double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                        if (lum < 20 || lum > 235) continue; // Skip near black or near white

                        totalR += r;
                        totalG += g;
                        totalB += b;
                        validCount++;

                        double max = Math.Max(r, Math.Max(g, b));
                        double min = Math.Min(r, Math.Min(g, b));
                        double delta = max - min;
                        double sat = max == 0 ? 0 : delta / max;

                        if (sat < 0.12) continue; // Skip grayish pixels

                        // Score: favors higher saturation, and luminance close to 110
                        double lumDist = Math.Abs(lum - 110.0) / 110.0;
                        double score = (sat * 2.5) + (1.0 - lumDist);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestR = r;
                            bestG = g;
                            bestB = b;
                        }
                    }

                    if (bestScore <= 0 && validCount > 0)
                    {
                        // Fallback: average color if saturation was low (e.g. monochrome artwork)
                        bestR = (byte)(totalR / validCount);
                        bestG = (byte)(totalG / validCount);
                        bestB = (byte)(totalB / validCount);
                        bestScore = 1.0;
                    }

                    if (bestScore > 0)
                    {
                        var rawColor = Windows.UI.Color.FromArgb(255, bestR, bestG, bestB);
                        var resultColor = AdjustAmbientColor(rawColor);
                        lock (_dominantColorCache)
                        {
                            if (_dominantColorCache.Count > 60) _dominantColorCache.Clear();
                            _dominantColorCache[thumbUrl] = resultColor;
                        }
                        return resultColor;
                    }
                }
            }
            catch { }

            return null;
        }

        internal static Windows.UI.Color LerpColor(Windows.UI.Color from, Windows.UI.Color to, double t)
        {
            return Windows.UI.Color.FromArgb(255,
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t));
        }

        private async Task UpdateAppleMusicBackdropAsync(string thumbnailUrl, Windows.UI.Color seedColor)
        {
            if (string.IsNullOrEmpty(thumbnailUrl)) return;

            var black = Windows.UI.Colors.Black;
            if (AppleMusicGradTop != null) AppleMusicGradTop.Color = LerpColor(seedColor, black, 0.05);
            if (AppleMusicGradMid != null) AppleMusicGradMid.Color = LerpColor(seedColor, black, 0.32);
            if (AppleMusicGradBot != null) AppleMusicGradBot.Color = LerpColor(seedColor, black, 0.78);
            if (AppleMusicArtFadeBot != null) AppleMusicArtFadeBot.Color = LerpColor(seedColor, black, 0.78);
            if (AppleMusicArtFadeMid != null) AppleMusicArtFadeMid.Color = LerpColor(seedColor, black, 0.32);

            var cached = Services.LumiaBlurHelper.GetCached(thumbnailUrl);
            if (cached != null)
            {
                if (AppleMusicBackdrop != null) AppleMusicBackdrop.Source = cached;
                return;
            }

            // Fast low-res fallback: load 40px thumbnail immediately so colors show without delay
            if (AppleMusicBackdrop != null)
            {
                try
                {
                    var fastBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetSquareThumbnail(thumbnailUrl), UriKind.Absolute)) { DecodePixelWidth = 40 };
                    AppleMusicBackdrop.Source = fastBmp;
                }
                catch { }
            }

            try
            {
                var bytes = await _dominantHttpClient.GetByteArrayAsync(thumbnailUrl);
                using (var stream = new System.IO.MemoryStream(bytes))
                {
                    var blurred = await Services.LumiaBlurHelper.RenderBlurredAsync(stream, 120, 200, 80);
                    Services.LumiaBlurHelper.PutCache(thumbnailUrl, blurred);
                    if (AppleMusicBackdrop != null) AppleMusicBackdrop.Source = blurred;
                }
            }
            catch { }
        }

        private static Windows.UI.Color AdjustAmbientColor(Windows.UI.Color c)
        {
            double r = c.R, g = c.G, b = c.B;

            // 1. Clamp maximum channel to avoid harsh neon/blinding glare
            double maxChannel = Math.Max(r, Math.Max(g, b));
            if (maxChannel > 140.0)
            {
                double scale = 140.0 / maxChannel;
                r *= scale;
                g *= scale;
                b *= scale;
            }

            // 2. Clamp perceived luminance (Rec. 601) to dark-mode ambient range [35, 65]
            double lum = 0.299 * r + 0.587 * g + 0.114 * b;
            if (lum > 65.0)
            {
                double scale = 65.0 / lum;
                r *= scale;
                g *= scale;
                b *= scale;
            }
            else if (lum < 35.0)
            {
                double scale = 35.0 / Math.Max(lum, 1.0);
                r = Math.Min(255.0, r * scale);
                g = Math.Min(255.0, g * scale);
                b = Math.Min(255.0, b * scale);
            }

            return Windows.UI.Color.FromArgb(255, (byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
        }

        private static Windows.UI.Color CalculateLyricsBottomFadeColor(Windows.UI.Color targetColor)
        {
            // NowPlayingGradient has:
            // - Top (Offset 0.0): targetColor
            // - Mid (Offset 0.55): midColor = targetColor * 0.35
            // - Bottom (Offset 1.0): #0D0D0D (RGB: 13, 13, 13)
            // The lyrics list bottom boundary sits at ~69% of the screen height (offset 0.69).
            // Between offset 0.55 and 1.0:
            // t = (0.69 - 0.55) / (1.0 - 0.55) = 0.14 / 0.45 ≈ 0.31
            // Therefore: 69% weight of midColor + 31% weight of #0D0D0D.
            double midR = targetColor.R * 0.35;
            double midG = targetColor.G * 0.35;
            double midB = targetColor.B * 0.35;

            byte fadeR = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(midR * 0.69 + 13.0 * 0.31)));
            byte fadeG = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(midG * 0.69 + 13.0 * 0.31)));
            byte fadeB = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(midB * 0.69 + 13.0 * 0.31)));

            return Windows.UI.Color.FromArgb(255, fadeR, fadeG, fadeB);
        }

        private static Windows.UI.Color CalculateLyricsBottomFadeTransparent(Windows.UI.Color fadeColor)
        {
            return Windows.UI.Color.FromArgb(0, fadeColor.R, fadeColor.G, fadeColor.B);
        }

        // [PERF] Instant direct assignment — eliminates CPU-bound dependent ColorAnimation lag on WP8.1
        private void AnimateGradientTo(Windows.UI.Color targetColor)
        {
            if (_isAppleMusicStyle)
            {
                _currentGradientColor = targetColor;
                var black = Windows.UI.Colors.Black;
                if (AppleMusicGradTop != null) AppleMusicGradTop.Color = LerpColor(targetColor, black, 0.05);
                if (AppleMusicGradMid != null) AppleMusicGradMid.Color = LerpColor(targetColor, black, 0.32);
                if (AppleMusicGradBot != null) AppleMusicGradBot.Color = LerpColor(targetColor, black, 0.78);
                if (AppleMusicArtFadeBot != null) AppleMusicArtFadeBot.Color = LerpColor(targetColor, black, 0.78);

                var fadeColor = LerpColor(targetColor, black, 0.78);
                if (LyricsFadeBottomStop0 != null) LyricsFadeBottomStop0.Color = fadeColor;
                if (LyricsFadeBottomStop1 != null)
                    LyricsFadeBottomStop1.Color = Windows.UI.Color.FromArgb(0, fadeColor.R, fadeColor.G, fadeColor.B);

                UpdateDockActiveState(NowPlayingPivot != null && NowPlayingPivot.SelectedIndex > 0 ? NowPlayingPivot.SelectedIndex : -1);

                bool isVisible = (NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible);
                if (isVisible) UpdateStatusBarColor(true);
                return;
            }

            targetColor = AdjustAmbientColor(targetColor);
            _currentGradientColor = targetColor;

            var midColor = Windows.UI.Color.FromArgb(
                255,
                (byte)(targetColor.R * 0.35),
                (byte)(targetColor.G * 0.35),
                (byte)(targetColor.B * 0.35));

            var lyricsFadeColor = CalculateLyricsBottomFadeColor(targetColor);
            var lyricsFadeTransparent = CalculateLyricsBottomFadeTransparent(lyricsFadeColor);

            if (NowPlayingGradientTop != null) NowPlayingGradientTop.Color = targetColor;
            if (NowPlayingGradientMid != null) NowPlayingGradientMid.Color = midColor;
            if (LyricsFadeBottomStop0 != null) LyricsFadeBottomStop0.Color = lyricsFadeColor;
            if (LyricsFadeBottomStop1 != null) LyricsFadeBottomStop1.Color = lyricsFadeTransparent;
            if (FullscreenLyricsGradientTop != null) FullscreenLyricsGradientTop.Color = targetColor;
            if (FullscreenLyricsGradientMid != null) FullscreenLyricsGradientMid.Color = midColor;

            bool isNowPlayingVisible = (NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible)
                                    || (FullscreenLyricsView != null && FullscreenLyricsView.Visibility == Visibility.Visible);
            if (isNowPlayingVisible)
            {
                UpdateStatusBarColor(true);
            }
        }

        private async Task AnimateStatusBarColorAsync(Windows.UI.Color targetColor, int durationMs = 350)
        {
            try
            {
                if (_statusBarAnimCts != null)
                {
                    _statusBarAnimCts.Cancel();
                    _statusBarAnimCts = null;
                }

                var cts = new CancellationTokenSource();
                _statusBarAnimCts = cts;

                var statusBar = Windows.UI.ViewManagement.StatusBar.GetForCurrentView();
                if (statusBar == null) return;

                var startColor = _currentStatusBarColor;
                if (startColor.R == targetColor.R && startColor.G == targetColor.G && startColor.B == targetColor.B)
                {
                    statusBar.BackgroundColor = targetColor;
                    statusBar.BackgroundOpacity = 1.0;
                    statusBar.ForegroundColor = Windows.UI.Colors.White;
                    return;
                }

                int steps = 10;
                int stepDelay = Math.Max(15, durationMs / steps);

                for (int i = 1; i <= steps; i++)
                {
                    if (cts.IsCancellationRequested) return;

                    double t = (double)i / steps;
                    // Smooth cubic ease-in-out (smoothstep: 3t^2 - 2t^3)
                    t = t * t * (3.0 - 2.0 * t);

                    byte r = (byte)Math.Max(0, Math.Min(255, Math.Round(startColor.R + (targetColor.R - startColor.R) * t)));
                    byte g = (byte)Math.Max(0, Math.Min(255, Math.Round(startColor.G + (targetColor.G - startColor.G) * t)));
                    byte b = (byte)Math.Max(0, Math.Min(255, Math.Round(startColor.B + (targetColor.B - startColor.B) * t)));

                    var stepColor = Windows.UI.Color.FromArgb(255, r, g, b);
                    statusBar.BackgroundColor = stepColor;
                    statusBar.BackgroundOpacity = 1.0;
                    statusBar.ForegroundColor = Windows.UI.Colors.White;
                    _currentStatusBarColor = stepColor;

                    await Task.Delay(stepDelay);
                }

                if (!cts.IsCancellationRequested)
                {
                    statusBar.BackgroundColor = targetColor;
                    statusBar.BackgroundOpacity = 1.0;
                    statusBar.ForegroundColor = Windows.UI.Colors.White;
                    _currentStatusBarColor = targetColor;
                }
            }
            catch { }
        }

        private void UpdateStatusBarColor(bool isNowPlaying, bool animate = true, int durationMs = 350)
        {
            if (!Dispatcher.HasThreadAccess)
            {
                var ignored = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateStatusBarColor(isNowPlaying, animate, durationMs));
                return;
            }

            var target = isNowPlaying ? _currentGradientColor : Windows.UI.Color.FromArgb(255, 18, 18, 18);
            if (animate)
            {
                var _ = AnimateStatusBarColorAsync(target, durationMs);
            }
            else
            {
                if (_statusBarAnimCts != null)
                {
                    _statusBarAnimCts.Cancel();
                    _statusBarAnimCts = null;
                }
                try
                {
                    var statusBar = Windows.UI.ViewManagement.StatusBar.GetForCurrentView();
                    if (statusBar != null)
                    {
                        statusBar.BackgroundColor = target;
                        statusBar.BackgroundOpacity = 1.0;
                        statusBar.ForegroundColor = Windows.UI.Colors.White;
                        _currentStatusBarColor = target;
                    }
                }
                catch { }
            }
        }

        private void UpdateNowPlayingGradient(string title, string artist, string thumbUrl = null)
        {
            int currentSeq = ++_gradientSequence;

            // If cached dominant color exists for this thumbnail, use it directly
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                lock (_dominantColorCache)
                {
                    Windows.UI.Color cached;
                    if (_dominantColorCache.TryGetValue(thumbUrl, out cached))
                    {
                        AnimateGradientTo(cached);
                        if (_isAppleMusicStyle)
                        {
                            var ignored = UpdateAppleMusicBackdropAsync(thumbUrl, cached);
                        }
                        return;
                    }
                }
            }

            // Fallback: Detect genre from title/artist keywords → Spotify-like gradient colors
            string combined = ((title ?? "") + " " + (artist ?? "")).ToLowerInvariant();
            Windows.UI.Color topColor;

            if (combined.Contains("kpop") || combined.Contains("k-pop") || combined.Contains("bts") || combined.Contains("blackpink") || combined.Contains("twice"))
                topColor = Windows.UI.Color.FromArgb(255, 180, 80, 200);   // Purple-pink
            else if (combined.Contains("jpop") || combined.Contains("j-pop") || combined.Contains("anime"))
                topColor = Windows.UI.Color.FromArgb(255, 225, 17, 140);   // Hot pink
            else if (combined.Contains("lofi") || combined.Contains("chill") || combined.Contains("jazz"))
                topColor = Windows.UI.Color.FromArgb(255, 40, 100, 120);   // Teal
            else if (combined.Contains("edm") || combined.Contains("electronic") || combined.Contains("house") || combined.Contains("dance") || combined.Contains("remix") || combined.Contains("club") || combined.Contains("bass"))
                topColor = Windows.UI.Color.FromArgb(255, 80, 155, 245);   // Electric blue
            else if (combined.Contains("hip hop") || combined.Contains("rap") || combined.Contains("trap"))
                topColor = Windows.UI.Color.FromArgb(255, 140, 25, 50);    // Dark red
            else if (combined.Contains("rock") || combined.Contains("metal") || combined.Contains("punk"))
                topColor = Windows.UI.Color.FromArgb(255, 80, 60, 60);     // Dark brown
            else if (combined.Contains("pop") || combined.Contains("hit") || combined.Contains("top"))
                topColor = Windows.UI.Color.FromArgb(255, 29, 185, 84);    // Spotify green
            else if (combined.Contains("indie") || combined.Contains("alternative"))
                topColor = Windows.UI.Color.FromArgb(255, 180, 156, 200);  // Soft lavender
            else if (combined.Contains("classical") || combined.Contains("piano") || combined.Contains("orchestra"))
                topColor = Windows.UI.Color.FromArgb(255, 50, 50, 100);    // Deep navy
            else if (combined.Contains("latin") || combined.Contains("reggaeton") || combined.Contains("salsa"))
                topColor = Windows.UI.Color.FromArgb(255, 225, 20, 41);    // Vibrant red
            else if (combined.Contains("phonk") || combined.Contains("drift"))
                topColor = Windows.UI.Color.FromArgb(255, 100, 20, 80);    // Deep magenta
            else if (combined.Contains("r&b") || combined.Contains("soul") || combined.Contains("rnb"))
                topColor = Windows.UI.Color.FromArgb(255, 80, 55, 80);     // Plum
            else
                topColor = Windows.UI.Color.FromArgb(255, 30, 50, 70);     // Default dark blue-gray

            // Immediate zero-latency transition to genre preview color
            AnimateGradientTo(topColor);

            // Asynchronously extract true dominant color from album artwork in background
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                Task.Run(async () =>
                {
                    var color = await ExtractDominantColorAsync(thumbUrl);
                    if (color.HasValue)
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            if (currentSeq != _gradientSequence) return;
                            var dominantColor = color.Value;
                            AnimateGradientTo(dominantColor);
                            if (_isAppleMusicStyle && thumbUrl != null)
                            {
                                var ignored = UpdateAppleMusicBackdropAsync(thumbUrl, dominantColor);
                            }
                        });
                    }
                });
            }
        }


        // ═══════════════════════════════════════════════════════
        // Lyrics animation helpers
        // WP8.1: Reuse dedicated Storyboard & Animation objects per animation channel
        // to avoid GC churn and UI stutters on 512MB devices.
        // ═══════════════════════════════════════════════════════
        private static readonly Duration _dur400 = new Duration(TimeSpan.FromMilliseconds(400));
        private static readonly Duration _dur450 = new Duration(TimeSpan.FromMilliseconds(450));
        private static readonly Duration _dur500 = new Duration(TimeSpan.FromMilliseconds(500));

        private Windows.UI.Xaml.Media.Animation.Storyboard _lyricOutSb;
        private Windows.UI.Xaml.Media.Animation.DoubleAnimation _lyricOutOpAnim;
        private Windows.UI.Xaml.Media.Animation.DoubleAnimation _lyricOutSxAnim;
        private Windows.UI.Xaml.Media.Animation.DoubleAnimation _lyricOutSyAnim;

        private Windows.UI.Xaml.Media.Animation.Storyboard _miniLyricMarqueeStoryboard;

        private void ClearMiniLyric()
        {
            MiniLyricText.Text = "";
            MiniLyricText2.Text = "";
            MiniLyricText2.Visibility = Visibility.Collapsed;
            if (_miniLyricMarqueeStoryboard != null)
            {
                _miniLyricMarqueeStoryboard.Stop();
                _miniLyricMarqueeStoryboard = null;
            }
            MiniLyricTranslate.X = 0;
        }

        public void ForceUpdateLyricUI()
        {
            if (currentLyricIndex < 0 || currentLyricIndex >= currentLyrics.Count) return;
            
            bool isFullscreen = FullscreenLyricsView.Visibility == Visibility.Visible;
            bool isLyricsUIVisible = isFullscreen || (NowPlayingPivot.SelectedIndex == 1 && LyricsListView.Visibility == Visibility.Visible);
            bool isMainScreenVisible = (!isFullscreen && NowPlayingPivot.SelectedIndex == 0);
            
            if (isLyricsUIVisible)
            {
                var targetListView = isFullscreen ? FullscreenLyricsListView : LyricsListView;
                currentLyrics[currentLyricIndex].ColorBrush = _lyricActiveBrush;
                
                if (isFullscreen)
                {
                    var fsContainer = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                    if (fsContainer != null) AnimateOpacity(fsContainer, 1.0);
                }
                else if (_isAppleMusicStyle)
                {
                    for (int i = 0; i < currentLyrics.Count; i++)
                    {
                        var container = targetListView.ContainerFromIndex(i) as FrameworkElement;
                        if (container == null) continue;
                        int dist = Math.Abs(i - currentLyricIndex);
                        if (i < currentLyricIndex) dist += 1;
                        container.Opacity = (i == currentLyricIndex) ? 1.0 : Math.Max(0.25, 1.0 - dist * 0.25);
                        container.RenderTransform = null;
                    }
                }
                else
                {
                    var newContainer = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                    if (newContainer != null)
                    {
                        var scaleTransform = newContainer.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                        if (scaleTransform == null)
                        {
                            scaleTransform = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 0.85, ScaleY = 0.85 };
                            newContainer.RenderTransformOrigin = new Point(0, 0.5);
                            newContainer.RenderTransform = scaleTransform;
                        }
                        AnimateLyricIn(newContainer, scaleTransform);
                    }
                    else { currentLyrics[currentLyricIndex].Opacity = 1.0; }
                }
                
                ScrollViewer scrollViewer = null;
                if (isFullscreen)
                {
                    if (_cachedFullscreenLyricsScrollViewer == null) _cachedFullscreenLyricsScrollViewer = GetScrollViewer(FullscreenLyricsListView);
                    scrollViewer = _cachedFullscreenLyricsScrollViewer;
                }
                else
                {
                    if (_cachedLyricsScrollViewer == null) _cachedLyricsScrollViewer = GetScrollViewer(LyricsListView);
                    scrollViewer = _cachedLyricsScrollViewer;
                }
                
                var activeContainer = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                if (activeContainer == null)
                {
                    targetListView.ScrollIntoView(currentLyrics[currentLyricIndex]);
                    targetListView.UpdateLayout();
                    activeContainer = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                }
                
                if (scrollViewer != null && activeContainer != null)
                {
                    var transform = activeContainer.TransformToVisual(scrollViewer);
                    var lyricPos = transform.TransformPoint(new Point(0, 0));
                    double targetOff = scrollViewer.VerticalOffset + lyricPos.Y - (scrollViewer.ViewportHeight / 2.0) + (activeContainer.ActualHeight / 2.0);
                    scrollViewer.ChangeView(null, targetOff, null, false);
                }
            }
            
            if (isMainScreenVisible)
            {
                UpdateMiniLyric(currentLyrics[currentLyricIndex].Text, force: true);
            }
            else
            {
                if (_miniLyricMarqueeStoryboard != null)
                {
                    _miniLyricMarqueeStoryboard.Stop();
                    _miniLyricMarqueeStoryboard = null;
                }
            }
        }

        private async void UpdateMiniLyric(string newLyric, bool force = false)
        {
            if (MiniLyricText.Text == newLyric && !force) return;

            var fadeOut = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeOut, MiniLyricStack);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeOut, "Opacity");
            var sbOut = new Windows.UI.Xaml.Media.Animation.Storyboard();
            sbOut.Children.Add(fadeOut);
            
            var tcsOut = new System.Threading.Tasks.TaskCompletionSource<bool>();
            sbOut.Completed += (s, e) => tcsOut.SetResult(true);
            sbOut.Begin();
            await tcsOut.Task;

            MiniLyricText.Text = newLyric;
            
            if (_miniLyricMarqueeStoryboard != null)
            {
                _miniLyricMarqueeStoryboard.Stop();
                _miniLyricMarqueeStoryboard = null;
            }
            MiniLyricTranslate.X = 0;

            var fadeIn = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200) };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, MiniLyricStack);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
            var sbIn = new Windows.UI.Xaml.Media.Animation.Storyboard();
            sbIn.Children.Add(fadeIn);
            sbIn.Begin();

            MiniLyricText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = MiniLyricText.DesiredSize.Width;
            double canvasWidth = MiniLyricCanvas.ActualWidth;

            if (textWidth > canvasWidth && canvasWidth > 0)
            {
                MiniLyricText2.Text = newLyric;
                MiniLyricText2.Visibility = Visibility.Visible;

                double distance = textWidth + 50; // text width + left margin
                double durationSec = distance / 30.0;
                
                var move = new Windows.UI.Xaml.Media.Animation.DoubleAnimation 
                {
                    From = 0,
                    To = -distance,
                    Duration = TimeSpan.FromSeconds(durationSec),
                    AutoReverse = false,
                    RepeatBehavior = Windows.UI.Xaml.Media.Animation.RepeatBehavior.Forever
                };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(move, MiniLyricTranslate);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(move, "X");
                
                _miniLyricMarqueeStoryboard = new Windows.UI.Xaml.Media.Animation.Storyboard();
                _miniLyricMarqueeStoryboard.Children.Add(move);
                _miniLyricMarqueeStoryboard.BeginTime = TimeSpan.FromSeconds(1.5);
                _miniLyricMarqueeStoryboard.Begin();
            }
            else
            {
                MiniLyricText2.Visibility = Visibility.Collapsed;
            }
        }

        private void AnimateLyricOut(FrameworkElement container, Windows.UI.Xaml.Media.ScaleTransform scale)
        {
            if (_lyricOutSb == null)
            {
                var easeInOut = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseInOut };
                _lyricOutOpAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 0.5, Duration = _dur500, EasingFunction = easeInOut };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_lyricOutOpAnim, "Opacity");

                _lyricOutSxAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 0.85, Duration = _dur450, EasingFunction = easeInOut };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_lyricOutSxAnim, "ScaleX");

                _lyricOutSyAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 0.85, Duration = _dur450, EasingFunction = easeInOut };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_lyricOutSyAnim, "ScaleY");

                _lyricOutSb = new Windows.UI.Xaml.Media.Animation.Storyboard();
                _lyricOutSb.Children.Add(_lyricOutOpAnim);
                _lyricOutSb.Children.Add(_lyricOutSxAnim);
                _lyricOutSb.Children.Add(_lyricOutSyAnim);
            }
            _lyricOutSb.Stop();
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_lyricOutOpAnim, container);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_lyricOutSxAnim, scale);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_lyricOutSyAnim, scale);
            _lyricOutSb.Begin();
        }

        private Windows.UI.Xaml.Media.Animation.Storyboard _lyricInSb;
        private Windows.UI.Xaml.Media.Animation.DoubleAnimation _lyricInOpAnim;
        private Windows.UI.Xaml.Media.Animation.DoubleAnimation _lyricInSxAnim;
        private Windows.UI.Xaml.Media.Animation.DoubleAnimation _lyricInSyAnim;

        private void AnimateLyricIn(FrameworkElement container, Windows.UI.Xaml.Media.ScaleTransform scale)
        {
            if (_lyricInSb == null)
            {
                var easeOut = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut };
                _lyricInOpAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { From = 0.5, To = 1.0, Duration = _dur450, EasingFunction = easeOut };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_lyricInOpAnim, "Opacity");

                _lyricInSxAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.0, Duration = _dur400, EasingFunction = easeOut };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_lyricInSxAnim, "ScaleX");

                _lyricInSyAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.0, Duration = _dur400, EasingFunction = easeOut };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_lyricInSyAnim, "ScaleY");

                _lyricInSb = new Windows.UI.Xaml.Media.Animation.Storyboard();
                _lyricInSb.Children.Add(_lyricInOpAnim);
                _lyricInSb.Children.Add(_lyricInSxAnim);
                _lyricInSb.Children.Add(_lyricInSyAnim);
            }
            _lyricInSb.Stop();
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_lyricInOpAnim, container);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_lyricInSxAnim, scale);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_lyricInSyAnim, scale);
            _lyricInSb.Begin();
        }

        private Windows.UI.Xaml.Media.Animation.Storyboard _lyricFsOpSb;
        private Windows.UI.Xaml.Media.Animation.DoubleAnimation _lyricFsOpAnim;

        private void AnimateOpacity(FrameworkElement target, double toValue)
        {
            if (_lyricFsOpSb == null)
            {
                var easeInOut = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseInOut };
                _lyricFsOpAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { Duration = _dur400, EasingFunction = easeInOut };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_lyricFsOpAnim, "Opacity");

                _lyricFsOpSb = new Windows.UI.Xaml.Media.Animation.Storyboard();
                _lyricFsOpSb.Children.Add(_lyricFsOpAnim);
            }
            _lyricFsOpSb.Stop();
            _lyricFsOpAnim.To = toValue;
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_lyricFsOpAnim, target);
            _lyricFsOpSb.Begin();
        }




    }
}
