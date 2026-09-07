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
                var _ = YTMusicWP.Services.DatabaseHelper.RemoveFavoriteAsync(currentTrack.VideoId);
            }
            else 
            { 
                favoriteTracks.Insert(0, currentTrack); 
                BigHeartBtn.Content = "♥"; 
                BigHeartBtn.Foreground = _greenBrush; 
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
                            currentLyrics[oldIndex].ColorBrush = _lyricInactiveBrush;

                            if (!isFullscreen)
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
                                else { currentLyrics[oldIndex].Opacity = 0.35; }
                            }
                            else
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
                    _appMediaPlayer.Position = TimeSpan.FromSeconds(Math.Min(MusicSlider.Value, Math.Max(0, _appMediaPlayer.NaturalDuration.TotalSeconds - 2)));
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

        // ── Dynamic & Genre-Based Animated Gradient for Now Playing ──
        private DispatcherTimer _gradientPulseTimer;
        private Windows.UI.Color _currentGradientColor = Windows.UI.Color.FromArgb(255, 30, 50, 70);
        private bool _gradientPulseUp = true;
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
                        // Ensure pleasant luminance for background (white text remains clear)
                        double lum = 0.299 * bestR + 0.587 * bestG + 0.114 * bestB;
                        if (lum > 140)
                        {
                            double factor = 140.0 / lum;
                            bestR = (byte)(bestR * factor);
                            bestG = (byte)(bestG * factor);
                            bestB = (byte)(bestB * factor);
                        }
                        else if (lum < 40)
                        {
                            double factor = 40.0 / Math.Max(lum, 1.0);
                            bestR = (byte)Math.Min(255, (int)(bestR * factor));
                            bestG = (byte)Math.Min(255, (int)(bestG * factor));
                            bestB = (byte)Math.Min(255, (int)(bestB * factor));
                        }

                        var resultColor = Windows.UI.Color.FromArgb(255, bestR, bestG, bestB);
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

        private static Windows.UI.Color CalculateLyricsBottomFadeColor(Windows.UI.Color targetColor)
        {
            byte midR = (byte)(targetColor.R * 0.35);
            byte midG = (byte)(targetColor.G * 0.35);
            byte midB = (byte)(targetColor.B * 0.35);

            byte fadeR = (byte)Math.Max(13, (int)(midR * 0.58 + 13 * 0.42));
            byte fadeG = (byte)Math.Max(13, (int)(midG * 0.58 + 13 * 0.42));
            byte fadeB = (byte)Math.Max(13, (int)(midB * 0.58 + 13 * 0.42));

            return Windows.UI.Color.FromArgb(255, fadeR, fadeG, fadeB);
        }

        private static Windows.UI.Color CalculateLyricsBottomFadeTransparent(Windows.UI.Color fadeColor)
        {
            return Windows.UI.Color.FromArgb(0, fadeColor.R, fadeColor.G, fadeColor.B);
        }

        private void AnimateGradientTo(Windows.UI.Color targetColor)
        {
            _currentGradientColor = targetColor;
            try
            {
                var midColor = Windows.UI.Color.FromArgb(
                    255,
                    (byte)(targetColor.R * 0.35),
                    (byte)(targetColor.G * 0.35),
                    (byte)(targetColor.B * 0.35));

                var lyricsFadeColor = CalculateLyricsBottomFadeColor(targetColor);
                var lyricsFadeTransparent = CalculateLyricsBottomFadeTransparent(lyricsFadeColor);

                // If views are collapsed or before animation starts, guarantee base color assignment
                if (NowPlayingGradientTop != null)
                {
                    if (NowPlayingView == null || NowPlayingView.Visibility != Visibility.Visible)
                        NowPlayingGradientTop.Color = targetColor;
                }
                if (NowPlayingGradientMid != null)
                {
                    if (NowPlayingView == null || NowPlayingView.Visibility != Visibility.Visible)
                        NowPlayingGradientMid.Color = midColor;
                }
                if (LyricsFadeBottomStop0 != null)
                {
                    if (NowPlayingView == null || NowPlayingView.Visibility != Visibility.Visible)
                        LyricsFadeBottomStop0.Color = lyricsFadeColor;
                }
                if (LyricsFadeBottomStop1 != null)
                {
                    if (NowPlayingView == null || NowPlayingView.Visibility != Visibility.Visible)
                        LyricsFadeBottomStop1.Color = lyricsFadeTransparent;
                }
                if (FullscreenLyricsGradientTop != null)
                {
                    if (FullscreenLyricsView == null || FullscreenLyricsView.Visibility != Visibility.Visible)
                        FullscreenLyricsGradientTop.Color = targetColor;
                }
                if (FullscreenLyricsGradientMid != null)
                {
                    if (FullscreenLyricsView == null || FullscreenLyricsView.Visibility != Visibility.Visible)
                        FullscreenLyricsGradientMid.Color = midColor;
                }

                var storyboard = new Windows.UI.Xaml.Media.Animation.Storyboard();
                var ease = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseInOut };

                // 1. NowPlaying Top
                if (NowPlayingGradientTop != null)
                {
                    var colorAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                    {
                        From = NowPlayingGradientTop.Color,
                        To = targetColor,
                        Duration = new Duration(TimeSpan.FromMilliseconds(800)),
                        EasingFunction = ease,
                        EnableDependentAnimation = true
                    };
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(colorAnim, NowPlayingGradientTop);
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(colorAnim, "Color");
                    storyboard.Children.Add(colorAnim);
                }

                // 2. NowPlaying Mid
                if (NowPlayingGradientMid != null)
                {
                    var midAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                    {
                        From = NowPlayingGradientMid.Color,
                        To = midColor,
                        Duration = new Duration(TimeSpan.FromMilliseconds(800)),
                        EasingFunction = ease,
                        EnableDependentAnimation = true
                    };
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(midAnim, NowPlayingGradientMid);
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(midAnim, "Color");
                    storyboard.Children.Add(midAnim);
                }

                // 3. Fullscreen Lyrics Top
                if (FullscreenLyricsGradientTop != null)
                {
                    var lyricsAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                    {
                        From = FullscreenLyricsGradientTop.Color,
                        To = targetColor,
                        Duration = new Duration(TimeSpan.FromMilliseconds(800)),
                        EasingFunction = ease,
                        EnableDependentAnimation = true
                    };
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(lyricsAnim, FullscreenLyricsGradientTop);
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(lyricsAnim, "Color");
                    storyboard.Children.Add(lyricsAnim);
                }

                // 4. Fullscreen Lyrics Mid
                if (FullscreenLyricsGradientMid != null)
                {
                    var lyricsMidAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                    {
                        From = FullscreenLyricsGradientMid.Color,
                        To = midColor,
                        Duration = new Duration(TimeSpan.FromMilliseconds(800)),
                        EasingFunction = ease,
                        EnableDependentAnimation = true
                    };
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(lyricsMidAnim, FullscreenLyricsGradientMid);
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(lyricsMidAnim, "Color");
                    storyboard.Children.Add(lyricsMidAnim);
                }

                // 5. Lyrics Bottom Fade Opaque Stop
                if (LyricsFadeBottomStop0 != null)
                {
                    var fadeAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                    {
                        From = LyricsFadeBottomStop0.Color,
                        To = lyricsFadeColor,
                        Duration = new Duration(TimeSpan.FromMilliseconds(800)),
                        EasingFunction = ease,
                        EnableDependentAnimation = true
                    };
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeAnim, LyricsFadeBottomStop0);
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeAnim, "Color");
                    storyboard.Children.Add(fadeAnim);
                }

                // 6. Lyrics Bottom Fade Transparent Stop
                if (LyricsFadeBottomStop1 != null)
                {
                    var fadeTransAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                    {
                        From = LyricsFadeBottomStop1.Color,
                        To = lyricsFadeTransparent,
                        Duration = new Duration(TimeSpan.FromMilliseconds(800)),
                        EasingFunction = ease,
                        EnableDependentAnimation = true
                    };
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeTransAnim, LyricsFadeBottomStop1);
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeTransAnim, "Color");
                    storyboard.Children.Add(fadeTransAnim);
                }

                storyboard.Completed += (s, e) =>
                {
                    if (NowPlayingGradientTop != null) NowPlayingGradientTop.Color = targetColor;
                    if (NowPlayingGradientMid != null) NowPlayingGradientMid.Color = midColor;
                    if (LyricsFadeBottomStop0 != null) LyricsFadeBottomStop0.Color = lyricsFadeColor;
                    if (LyricsFadeBottomStop1 != null) LyricsFadeBottomStop1.Color = lyricsFadeTransparent;
                    if (FullscreenLyricsGradientTop != null) FullscreenLyricsGradientTop.Color = targetColor;
                    if (FullscreenLyricsGradientMid != null) FullscreenLyricsGradientMid.Color = midColor;
                };

                storyboard.Begin();
                StartGradientPulse();
            }
            catch
            {
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
                            AnimateGradientTo(color.Value);
                        });
                    }
                });
            }
        }

        private void StartGradientPulse()
        {
            if (NowPlayingView.Visibility != Visibility.Visible && FullscreenLyricsView.Visibility != Visibility.Visible)
            {
                return;
            }

            if (_gradientPulseTimer != null)
            {
                _gradientPulseTimer.Stop();
                _gradientPulseTimer.Tick -= GradientPulse_Tick;
            }
            // Invalidate cached gradient storyboard so it re-targets after color change
            _gradientPulseSb = null;
            _gradientPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _gradientPulseTimer.Tick += GradientPulse_Tick;
            _gradientPulseTimer.Start();
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
                _lyricOutOpAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 0.35, Duration = _dur500, EasingFunction = easeInOut };
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
                _lyricInOpAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { From = 0.35, To = 1.0, Duration = _dur450, EasingFunction = easeOut };
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

        // [OPT-6] Cached gradient pulse objects
        private Windows.UI.Xaml.Media.Animation.Storyboard _gradientPulseSb;
        private Windows.UI.Xaml.Media.Animation.ColorAnimation _gradientPulseAnim;
        private Windows.UI.Xaml.Media.Animation.ColorAnimation _gradientPulseMidAnim;
        private Windows.UI.Xaml.Media.Animation.ColorAnimation _gradientPulseFadeAnim;
        private Windows.UI.Xaml.Media.Animation.ColorAnimation _gradientPulseFadeTransAnim;

        private void GradientPulse_Tick(object sender, object e)
        {
            try
            {
                if (NowPlayingView.Visibility != Visibility.Visible && FullscreenLyricsView.Visibility != Visibility.Visible)
                {
                    _gradientPulseTimer?.Stop();
                    return;
                }

                var baseColor = _currentGradientColor;
                int shift = _gradientPulseUp ? 15 : -15;
                byte r = (byte)Math.Max(0, Math.Min(255, baseColor.R + shift));
                byte g = (byte)Math.Max(0, Math.Min(255, baseColor.G + shift));
                byte b = (byte)Math.Max(0, Math.Min(255, baseColor.B + shift));
                var targetColor = Windows.UI.Color.FromArgb(255, r, g, b);

                var targetMidColor = Windows.UI.Color.FromArgb(
                    255,
                    (byte)(targetColor.R * 0.35),
                    (byte)(targetColor.G * 0.35),
                    (byte)(targetColor.B * 0.35));

                var targetFadeColor = CalculateLyricsBottomFadeColor(targetColor);
                var targetFadeTrans = CalculateLyricsBottomFadeTransparent(targetFadeColor);

                // Reuse storyboard + animation — only update To value
                if (_gradientPulseSb == null)
                {
                    _gradientPulseAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                    {
                        Duration = new Duration(TimeSpan.FromSeconds(3)),
                        EasingFunction = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseInOut },
                        EnableDependentAnimation = true
                    };
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_gradientPulseAnim, NowPlayingGradientTop);
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_gradientPulseAnim, "Color");
                    _gradientPulseSb = new Windows.UI.Xaml.Media.Animation.Storyboard();
                    _gradientPulseSb.Children.Add(_gradientPulseAnim);

                    if (NowPlayingGradientMid != null)
                    {
                        _gradientPulseMidAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                        {
                            Duration = new Duration(TimeSpan.FromSeconds(3)),
                            EasingFunction = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseInOut },
                            EnableDependentAnimation = true
                        };
                        Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_gradientPulseMidAnim, NowPlayingGradientMid);
                        Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_gradientPulseMidAnim, "Color");
                        _gradientPulseSb.Children.Add(_gradientPulseMidAnim);
                    }

                    if (LyricsFadeBottomStop0 != null)
                    {
                        _gradientPulseFadeAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                        {
                            Duration = new Duration(TimeSpan.FromSeconds(3)),
                            EasingFunction = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseInOut },
                            EnableDependentAnimation = true
                        };
                        Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_gradientPulseFadeAnim, LyricsFadeBottomStop0);
                        Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_gradientPulseFadeAnim, "Color");
                        _gradientPulseSb.Children.Add(_gradientPulseFadeAnim);
                    }

                    if (LyricsFadeBottomStop1 != null)
                    {
                        _gradientPulseFadeTransAnim = new Windows.UI.Xaml.Media.Animation.ColorAnimation
                        {
                            Duration = new Duration(TimeSpan.FromSeconds(3)),
                            EasingFunction = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseInOut },
                            EnableDependentAnimation = true
                        };
                        Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(_gradientPulseFadeTransAnim, LyricsFadeBottomStop1);
                        Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(_gradientPulseFadeTransAnim, "Color");
                        _gradientPulseSb.Children.Add(_gradientPulseFadeTransAnim);
                    }
                }
                _gradientPulseSb.Stop();
                if (NowPlayingGradientTop != null) _gradientPulseAnim.From = NowPlayingGradientTop.Color;
                _gradientPulseAnim.To = targetColor;
                if (_gradientPulseMidAnim != null && NowPlayingGradientMid != null)
                {
                    _gradientPulseMidAnim.From = NowPlayingGradientMid.Color;
                    _gradientPulseMidAnim.To = targetMidColor;
                }
                if (_gradientPulseFadeAnim != null && LyricsFadeBottomStop0 != null)
                {
                    _gradientPulseFadeAnim.From = LyricsFadeBottomStop0.Color;
                    _gradientPulseFadeAnim.To = targetFadeColor;
                }
                if (_gradientPulseFadeTransAnim != null && LyricsFadeBottomStop1 != null)
                {
                    _gradientPulseFadeTransAnim.From = LyricsFadeBottomStop1.Color;
                    _gradientPulseFadeTransAnim.To = targetFadeTrans;
                }
                _gradientPulseSb.Begin();

                _gradientPulseUp = !_gradientPulseUp;
            }
            catch { }
        }


    }
}
