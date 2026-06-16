using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Playback;
using Windows.Storage;
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
                OpenArtistProfile(track.VideoId.Substring(8), track.Title ?? track.ChannelName);
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
            // Start marquee after layout completes
            var ignored2 = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => StartTitleMarquee());
            MiniArtist.Text = track.ChannelName; BigArtist.Text = track.ChannelName;
            SetPlayPauseIcon(true);
            MenuTitle.Text = track.Title;
            MenuArtist.Text = track.ChannelName;

            if (!string.IsNullOrEmpty(track.ThumbnailUrl))
            {
                // [OPT-M3] Dùng chung 1 BitmapImage cho BigCover + MenuCover (cùng src, cùng DecodePixelWidth)
                var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(track.ThumbnailUrl), UriKind.Absolute));
                bigBmp.DecodePixelWidth = 360;
                BigCoverImage.ImageSource  = bigBmp;
                AlbumArtEntranceStoryboard.Begin();
                MenuCoverImage.ImageSource = bigBmp;

                var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(track.ThumbnailUrl, UriKind.Absolute));
                miniBmp.DecodePixelWidth = 100;
                MiniCoverImage.ImageSource = miniBmp;
            }

            bool isFav = favoriteTracks.Any(t => t.VideoId == track.VideoId);
            BigHeartBtn.Content = isFav ? "♥" : "♡";
            BigHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;

            var ignored = UpdateLyricsAsync(track.Title, track.ChannelName);
            UpdateNowPlayingGradient(track.Title, track.ChannelName);

            // BUG FIX: Xác định activeList TRƯỚC khi insert vào history.
            // Nếu detect sau khi insert, historyTracks.Contains(track) sẽ luôn true,
            // gây mất nguồn context thực sự (search, playlist, favorites...).
            ObservableCollection<YouTubeTrack> activeList = homeTracks;
            if (searchResults.Contains(track)) activeList = searchResults;
            else if (favoriteTracks.Contains(track)) activeList = favoriteTracks;
            else if (downloadedTracks.Contains(track)) activeList = downloadedTracks;
            else if (homeHistoryCarouselTracks.Contains(track)) activeList = homeHistoryCarouselTracks;
            else if (historyQuickGridTracks.Contains(track)) activeList = historyQuickGridTracks;
            else if (popTracks.Contains(track)) activeList = popTracks;
            else if (lofiTracks.Contains(track)) activeList = lofiTracks;
            else if (workoutTracks.Contains(track)) activeList = workoutTracks;
            else if (historyTracks.Contains(track)) activeList = historyTracks;
            else if (_currentViewingPlaylist != null && _currentViewingPlaylist.Tracks.Contains(track)) activeList = _currentViewingPlaylist.Tracks;
            else if (ArtistSongsList.ItemsSource != null) { var artistList = ArtistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>; if (artistList != null && artistList.Contains(track)) activeList = artistList; }

            // Cập nhật lịch sử SAU khi đã chọn activeList
            var existingHistory = historyTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
            if (existingHistory != null) historyTracks.Remove(existingHistory);
            historyTracks.Insert(0, track);
            if (historyTracks.Count > 20) historyTracks.RemoveAt(historyTracks.Count - 1);
            var ignoredHistory = SaveHistoryAsyncTask();
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

            // OPTIMIZATION: Gộp 2 vòng lặp activeList thành 1 duy nhất
            int count = activeList.Count;
            string[] urls = new string[count];
            string[] titles = new string[count];
            string[] artists = new string[count];
            string[] videoIds = new string[count];
            string[] thumbnails = new string[count];

            int startIndex = Math.Max(0, activeList.IndexOf(track));

            currentQueueTracks.Clear();
            for (int i = 0; i < count; i++)
            {
                var t = activeList[i];
                currentQueueTracks.Add(t);
                if (i == startIndex && !string.IsNullOrEmpty(resolvedUrl))
                    urls[i] = resolvedUrl; // Dùng URL đã resolve
                else
                    urls[i] = t.VideoId.StartsWith("LOCAL:")
                        ? "ms-appdata:///local/" + t.VideoId.Substring(6)
                        : ""; // AudioTask sẽ tự resolve cho các bài khác khi đến lượt
                titles[i] = t.Title;
                artists[i] = t.ChannelName;
                videoIds[i] = t.VideoId;
                thumbnails[i] = t.ThumbnailUrl;
            }

            var message = new ValueSet {
                { "UpdatePlaylist", "" }, { "Urls", urls }, { "Titles", titles }, { "Artists", artists },
                { "VideoIds", videoIds }, { "Thumbnails", thumbnails }, { "StartIndex", startIndex }, { "FastUrl", urls[startIndex] }
            };
            try { BackgroundMediaPlayer.SendMessageToBackground(message); } catch { }
        }

        private void HeartButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentTrack == null) return;
            var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == currentTrack.VideoId);
            if (existing != null) { favoriteTracks.Remove(existing); BigHeartBtn.Content = "♡"; BigHeartBtn.Foreground = _whiteBrush; }
            else { favoriteTracks.Insert(0, currentTrack); BigHeartBtn.Content = "♥"; BigHeartBtn.Foreground = _greenBrush; }
            SaveFavoritesAsync();
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

                        if (NowPlayingView.Visibility != Visibility.Visible) return;
                        if (NowPlayingPivot.SelectedIndex != 1) return;
                        if (currentLyrics.Count == 0 || LyricsListView.Visibility != Visibility.Visible) return;

                        int newIndex = -1;
                        for (int i = 0; i < currentLyrics.Count; i++)
                        {
                            if (pos >= currentLyrics[i].Time.Subtract(TimeSpan.FromSeconds(0.2))) newIndex = i;
                            else break;
                        }

                        if (newIndex == currentLyricIndex || newIndex < 0) return;

                        int oldIndex = currentLyricIndex;
                        currentLyricIndex = newIndex;

                        // Animate OLD lyric → smooth fade out + scale down
                        if (oldIndex >= 0 && oldIndex < currentLyrics.Count)
                        {
                            currentLyrics[oldIndex].ColorBrush = _lyricInactiveBrush;

                            var oldContainer = LyricsListView.ContainerFromIndex(oldIndex) as FrameworkElement;
                            if (oldContainer != null)
                            {
                                var oldScale = oldContainer.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                                if (oldScale == null)
                                {
                                    oldScale = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 1, ScaleY = 1 };
                                    oldContainer.RenderTransformOrigin = new Point(0, 0.5);
                                    oldContainer.RenderTransform = oldScale;
                                }

                                var easeInOut = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseInOut };
                                var fadeOut = new Windows.UI.Xaml.Media.Animation.Storyboard();

                                var opAnim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                                { To = 0.35, Duration = new Duration(TimeSpan.FromMilliseconds(500)), EasingFunction = easeInOut };
                                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(opAnim, oldContainer);
                                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(opAnim, "Opacity");

                                var sxOut = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                                { To = 0.85, Duration = new Duration(TimeSpan.FromMilliseconds(450)), EasingFunction = easeInOut };
                                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(sxOut, oldScale);
                                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(sxOut, "ScaleX");

                                var syOut = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                                { To = 0.85, Duration = new Duration(TimeSpan.FromMilliseconds(450)), EasingFunction = easeInOut };
                                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(syOut, oldScale);
                                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(syOut, "ScaleY");

                                fadeOut.Children.Add(opAnim);
                                fadeOut.Children.Add(sxOut);
                                fadeOut.Children.Add(syOut);
                                fadeOut.Begin();
                            }
                            else { currentLyrics[oldIndex].Opacity = 0.35; }
                        }

                        // Animate NEW lyric → smooth scale up + fade in (NO FontSize/FontWeight change)
                        currentLyrics[currentLyricIndex].ColorBrush = _lyricActiveBrush;

                        var newContainer = LyricsListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                        if (newContainer != null)
                        {
                            var scaleTransform = newContainer.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                            if (scaleTransform == null)
                            {
                                scaleTransform = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 0.85, ScaleY = 0.85 };
                                newContainer.RenderTransformOrigin = new Point(0, 0.5);
                                newContainer.RenderTransform = scaleTransform;
                            }

                            var easeOut = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut };
                            var entrance = new Windows.UI.Xaml.Media.Animation.Storyboard();

                            var opIn = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                            { From = 0.35, To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(450)), EasingFunction = easeOut };
                            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(opIn, newContainer);
                            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(opIn, "Opacity");

                            var sxIn = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                            { To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(400)), EasingFunction = easeOut };
                            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(sxIn, scaleTransform);
                            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(sxIn, "ScaleX");

                            var syIn = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                            { To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(400)), EasingFunction = easeOut };
                            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(syIn, scaleTransform);
                            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(syIn, "ScaleY");

                            entrance.Children.Add(opIn);
                            entrance.Children.Add(sxIn);
                            entrance.Children.Add(syIn);
                            entrance.Begin();
                        }
                        else { currentLyrics[currentLyricIndex].Opacity = 1.0; }

                        LyricsListView.ScrollIntoView(currentLyrics[currentLyricIndex]);

                        if (_cachedLyricsScrollViewer == null)
                            _cachedLyricsScrollViewer = GetScrollViewer(LyricsListView);

                        var activeContainer = (newContainer ?? LyricsListView.ContainerFromIndex(currentLyricIndex)) as FrameworkElement;
                        if (_cachedLyricsScrollViewer != null && activeContainer != null)
                        {
                            var transform    = activeContainer.TransformToVisual(_cachedLyricsScrollViewer);
                            var lyricPos     = transform.TransformPoint(new Point(0, 0));
                            double targetOff = _cachedLyricsScrollViewer.VerticalOffset + lyricPos.Y
                                            - (_cachedLyricsScrollViewer.ViewportHeight / 2.0)
                                            + (activeContainer.ActualHeight / 2.0);
                            _cachedLyricsScrollViewer.ChangeView(null, targetOff, null, false);
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
                // FIX: Đánh thức background task nếu nó đã chết thay vì gửi message mù
                if (_appMediaPlayer.CurrentState == MediaPlayerState.Closed && currentTrack != null)
                {
                    PlayTrack(currentTrack);
                    return;
                }
                BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { "NextTrackMessage", "" } });
            }
            catch { }
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
                            var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            bigBmp.DecodePixelWidth = 360;
                            bigBmp.UriSource = new Uri(GetHighResThumbnail(thumb), UriKind.Absolute);
                            BigCoverImage.ImageSource = bigBmp;
                            AlbumArtEntranceStoryboard.Begin();
                            MenuCoverImage.ImageSource = bigBmp;

                            var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            miniBmp.DecodePixelWidth = 100;
                            miniBmp.UriSource = new Uri(thumb, UriKind.Absolute);
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
                        UpdateNowPlayingGradient(title, artist);
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
                            var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            bigBmp.DecodePixelWidth = 360;
                            bigBmp.UriSource = new Uri(GetHighResThumbnail(thumb), UriKind.Absolute);
                            BigCoverImage.ImageSource = bigBmp;
                            AlbumArtEntranceStoryboard.Begin();
                            MenuCoverImage.ImageSource = bigBmp;

                            var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            miniBmp.DecodePixelWidth = 100;
                            miniBmp.UriSource = new Uri(thumb, UriKind.Absolute);
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
                        UpdateNowPlayingGradient(title, artist);
                    }
                });
            }
        }

        // ── Genre-Based Gradient for Now Playing ──
        private void UpdateNowPlayingGradient(string title, string artist)
        {
            // Detect genre from title/artist keywords → Spotify-like gradient colors
            string combined = (title + " " + artist).ToLowerInvariant();
            Windows.UI.Color topColor;

            if (combined.Contains("kpop") || combined.Contains("k-pop") || combined.Contains("bts") || combined.Contains("blackpink") || combined.Contains("twice"))
                topColor = Windows.UI.Color.FromArgb(255, 180, 80, 200);   // Purple-pink
            else if (combined.Contains("jpop") || combined.Contains("j-pop") || combined.Contains("anime"))
                topColor = Windows.UI.Color.FromArgb(255, 225, 17, 140);   // Hot pink
            else if (combined.Contains("lofi") || combined.Contains("chill") || combined.Contains("jazz"))
                topColor = Windows.UI.Color.FromArgb(255, 40, 100, 120);   // Teal
            else if (combined.Contains("edm") || combined.Contains("electronic") || combined.Contains("house"))
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

            try
            {
                NowPlayingGradientTop.Color = topColor;
            }
            catch { }
        }

    }
}
