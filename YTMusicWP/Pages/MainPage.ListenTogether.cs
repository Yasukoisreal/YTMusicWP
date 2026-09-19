using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Playback;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using YTMusicWP.Services.ListenTogether;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private bool _isApplyingRemoteAction;
        private bool _isBufferingRemoteTrack;
        private bool _pendingRemotePlay;
        private long _pendingRemotePosition;
        private string _lastRemoteTrackId;
        private string _shareRoomCode;

        private void InitializeListenTogether()
        {
            var mgr = ListenTogetherManager.Instance;
            mgr.StateChanged += UpdateListenTogetherUI;
            mgr.RemotePlaybackActionReceived += OnRemotePlaybackActionReceived;
            mgr.RemoteTrackChanged += OnRemoteTrackChanged;

            LoadListenTogetherSettings();
            UpdateListenTogetherUI();
        }

        #region Navigation & Visibility

        public void OpenListenTogetherView()
        {
            if (ListenTogetherView == null) return;

            // Pre-fill user name if not set
            if (string.IsNullOrWhiteSpace(LtDisplayNameBox.Text))
            {
                LtDisplayNameBox.Text = string.IsNullOrWhiteSpace(HomeAvatarLetter?.Text) ? "Lumia User" : ("Lumia " + HomeAvatarLetter.Text);
            }

            ListenTogetherView.Visibility = Visibility.Visible;
            if (ListenTogetherSlideInStoryboard != null)
            {
                ListenTogetherSlideInStoryboard.Begin();
            }

            UpdateListenTogetherUI();
        }

        private void CloseListenTogetherView_Click(object sender, RoutedEventArgs e)
        {
            if (LtSettingsPanel != null) LtSettingsPanel.Visibility = Visibility.Collapsed;
            if (ListenTogetherView != null)
            {
                ListenTogetherView.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenListenTogether_Click(object sender, RoutedEventArgs e)
        {
            OpenListenTogetherView();
        }

        #endregion

        #region UI State Updates

        private void UpdateListenTogetherUI()
        {
            var mgr = ListenTogetherManager.Instance;

            // 1. Top bar indicator dot
            if (ListenTogetherActiveDot != null)
            {
                ListenTogetherActiveDot.Visibility = mgr.InRoom ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ListenTogetherView == null || ListenTogetherView.Visibility != Visibility.Visible) return;

            // 2. Server connection status
            if (LtConnectionDot != null)
            {
                Color dotColor;
                string statusText;
                switch (mgr.Connection)
                {
                    case ConnectionState.Connected:
                        dotColor = Color.FromArgb(255, 29, 185, 84); // Green
                        statusText = "Connected";
                        break;
                    case ConnectionState.Connecting:
                        dotColor = Color.FromArgb(255, 255, 170, 0); // Amber
                        statusText = "Connecting…";
                        break;
                    case ConnectionState.Failed:
                        dotColor = Color.FromArgb(255, 255, 59, 48); // Red
                        statusText = mgr.ErrorMessage ?? "Connection failed";
                        break;
                    default:
                        dotColor = Color.FromArgb(255, 128, 128, 128); // Grey
                        statusText = "Not connected";
                        break;
                }
                LtConnectionDot.Fill = new SolidColorBrush(dotColor);
                if (LtConnectionStatusText != null) LtConnectionStatusText.Text = statusText;
                if (LtConnectBtn != null)
                {
                    LtConnectBtn.Content = (mgr.Connection == ConnectionState.Connected) ? "Disconnect" : "Connect";
                }
            }

            // 3. Lobby vs Room Panel
            if (mgr.InRoom)
            {
                if (LtLobbyPanel != null) LtLobbyPanel.Visibility = Visibility.Collapsed;
                if (LtRoomPanel != null) LtRoomPanel.Visibility = Visibility.Visible;

                // Room Code Poster
                if (LtRoomCodePoster != null)
                {
                    string code = mgr.RoomCode ?? "";
                    if (code.Length == 8)
                    {
                        LtRoomCodePoster.Text = code.Substring(0, 4) + " " + code.Substring(4, 4);
                    }
                    else
                    {
                        LtRoomCodePoster.Text = code;
                    }
                }

                // Host subtitle
                if (LtRoomRoleText != null)
                {
                    if (mgr.IsHost)
                    {
                        LtRoomRoleText.Text = "You are the host · " + mgr.Members.Count + " listening";
                    }
                    else
                    {
                        var host = mgr.Members.FirstOrDefault(m => m.IsHost);
                        LtRoomRoleText.Text = (host != null ? host.Username : "Host") + " is controlling playback";
                    }
                }

                // Buffer barrier banner
                if (LtBufferBanner != null)
                {
                    if (mgr.WaitingFor != null && mgr.WaitingFor.Count > 0)
                    {
                        var names = mgr.Members.Where(m => mgr.WaitingFor.Contains(m.UserId)).Select(m => m.Username).ToList();
                        string nameList = names.Count > 0 ? string.Join(", ", names) : "everyone";
                        LtBufferBannerText.Text = "Waiting for " + nameList + "…";
                        LtBufferBanner.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        LtBufferBanner.Visibility = Visibility.Collapsed;
                    }
                }

                // Join Requests Section (Host only)
                if (LtJoinRequestsSection != null)
                {
                    LtJoinRequestsSection.Visibility = (mgr.IsHost && mgr.JoinRequests.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
                    if (LtJoinRequestsList != null) LtJoinRequestsList.ItemsSource = mgr.JoinRequests;
                }

                // Members list
                if (LtMembersList != null)
                {
                    LtMembersList.ItemsSource = null;
                    LtMembersList.ItemsSource = mgr.Members;
                }
            }
            else
            {
                if (LtLobbyPanel != null) LtLobbyPanel.Visibility = Visibility.Visible;
                if (LtRoomPanel != null) LtRoomPanel.Visibility = Visibility.Collapsed;

                // Waiting for approval state
                if (LtWaitingApprovalCard != null)
                {
                    if (!string.IsNullOrEmpty(mgr.PendingJoinCode))
                    {
                        LtWaitingApprovalCard.Visibility = Visibility.Visible;
                        LtWaitingCodeText.Text = mgr.PendingJoinCode;
                    }
                    else
                    {
                        LtWaitingApprovalCard.Visibility = Visibility.Collapsed;
                    }
                }

                // Error message
                if (LtErrorBanner != null)
                {
                    if (!string.IsNullOrEmpty(mgr.ErrorMessage))
                    {
                        LtErrorBanner.Visibility = Visibility.Visible;
                        LtErrorMessageText.Text = mgr.ErrorMessage;
                    }
                    else
                    {
                        LtErrorBanner.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        #endregion

        #region Lobby Button Handlers

        private async void LtConnect_Click(object sender, RoutedEventArgs e)
        {
            var mgr = ListenTogetherManager.Instance;
            if (mgr.Connection == ConnectionState.Connected)
            {
                mgr.Disconnect();
            }
            else
            {
                await mgr.ConnectAsync();
            }
        }

        private async void LtCreateRoom_Click(object sender, RoutedEventArgs e)
        {
            string name = LtDisplayNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ShowToast("Please enter a display name");
                return;
            }

            var mgr = ListenTogetherManager.Instance;
            if (mgr.Connection != ConnectionState.Connected)
            {
                await mgr.ConnectAsync();
            }
            await mgr.CreateRoomAsync(name);

            // If a track is already playing locally, publish it as initial state
            if (currentTrack != null)
            {
                OnTrackStartedAsHost(currentTrack);
            }
        }

        private async void LtJoinRoom_Click(object sender, RoutedEventArgs e)
        {
            string name = LtDisplayNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ShowToast("Please enter a display name");
                return;
            }

            string code = GetEnteredRoomCode().Trim().ToUpper();
            if (code.Length != 8)
            {
                ShowToast("Room code must be 8 characters");
                return;
            }

            var mgr = ListenTogetherManager.Instance;
            if (mgr.Connection != ConnectionState.Connected)
            {
                await mgr.ConnectAsync();
            }
            await mgr.JoinRoomAsync(code, name);
        }

        private void LtCancelJoin_Click(object sender, RoutedEventArgs e)
        {
            ListenTogetherManager.Instance.CancelJoin();
        }

        private async void LtLeaveRoom_Click(object sender, RoutedEventArgs e)
        {
            await ListenTogetherManager.Instance.LeaveRoomAsync();
        }

        private void LtDismissError_Click(object sender, RoutedEventArgs e)
        {
            ListenTogetherManager.Instance.ClearError();
        }

        private void LtCopyCode_Click(object sender, RoutedEventArgs e)
        {
            string code = ListenTogetherManager.Instance.RoomCode;
            if (!string.IsNullOrEmpty(code))
            {
                _shareRoomCode = code;
                DataTransferManager.ShowShareUI();
                ShowToast("Share or copy room code: " + code);
            }
        }

        private void LtShareCode_Click(object sender, RoutedEventArgs e)
        {
            string code = ListenTogetherManager.Instance.RoomCode;
            if (!string.IsNullOrEmpty(code))
            {
                _shareRoomCode = code;
                DataTransferManager.ShowShareUI();
            }
        }

        private async void LtApproveJoin_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var req = btn?.DataContext as PendingJoinRequest;
            if (req != null)
            {
                await ListenTogetherManager.Instance.ApproveJoinAsync(req.UserId);
            }
        }

        private async void LtRejectJoin_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var req = btn?.DataContext as PendingJoinRequest;
            if (req != null)
            {
                await ListenTogetherManager.Instance.RejectJoinAsync(req.UserId);
            }
        }

        private async void LtKickMember_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var mem = btn?.DataContext as RoomMember;
            if (mem != null && !mem.IsHost)
            {
                await ListenTogetherManager.Instance.KickUserAsync(mem.UserId, "Removed by host");
            }
        }

        private async void LtTransferHost_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var mem = btn?.DataContext as RoomMember;
            if (mem != null && !mem.IsHost)
            {
                await ListenTogetherManager.Instance.TransferHostAsync(mem.UserId);
            }
        }

        #endregion

        #region OTP Code Input Handling

        private string GetEnteredRoomCode()
        {
            return (LtHiddenCodeBox != null) ? LtHiddenCodeBox.Text : "";
        }

        private void LtHiddenCodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = LtHiddenCodeBox.Text.ToUpper();
            if (text.Length > 8)
            {
                text = text.Substring(0, 8);
                LtHiddenCodeBox.Text = text;
            }

            // Update 8 separate boxes
            TextBlock[] boxes = new[] { LtDigit0, LtDigit1, LtDigit2, LtDigit3, LtDigit4, LtDigit5, LtDigit6, LtDigit7 };
            Border[] borders = new[] { LtBorder0, LtBorder1, LtBorder2, LtBorder3, LtBorder4, LtBorder5, LtBorder6, LtBorder7 };

            for (int i = 0; i < 8; i++)
            {
                if (boxes[i] != null)
                {
                    boxes[i].Text = (i < text.Length) ? text[i].ToString() : "";
                }
                if (borders[i] != null)
                {
                    bool isCurrentCaret = (i == text.Length);
                    borders[i].BorderBrush = isCurrentCaret ? new SolidColorBrush(Color.FromArgb(255, 29, 185, 84)) : new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
                }
            }
        }

        private void LtCodeBoxes_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (LtHiddenCodeBox != null)
            {
                LtHiddenCodeBox.Focus(FocusState.Programmatic);
            }
        }

        #endregion

        #region Playback Bridge: Host Publishing & Guest Reception

        /// <summary>
        /// Called when the local player starts playing a track as Host.
        /// </summary>
        public async void OnTrackStartedAsHost(YouTubeTrack track)
        {
            var mgr = ListenTogetherManager.Instance;
            if (!mgr.InRoom || !mgr.IsHost || _isApplyingRemoteAction || track == null) return;

            try
            {
                long durMs = 0;
                if (!string.IsNullOrEmpty(track.Duration))
                {
                    var parts = track.Duration.Split(':');
                    if (parts.Length == 2)
                    {
                        int m, s;
                        if (int.TryParse(parts[0], out m) && int.TryParse(parts[1], out s))
                            durMs = (long)((m * 60 + s) * 1000);
                    }
                    else if (parts.Length == 3)
                    {
                        int h, m, s;
                        if (int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m) && int.TryParse(parts[2], out s))
                            durMs = (long)(((h * 60 + m) * 60 + s) * 1000);
                    }
                }
                if (durMs <= 0)
                {
                    try { durMs = (long)_appMediaPlayer.NaturalDuration.TotalMilliseconds; } catch { }
                }

                var trackInfo = new TrackInfo
                {
                    Id = track.VideoId,
                    Title = track.Title ?? "",
                    Artist = track.ChannelName ?? "",
                    Album = track.AlbumName ?? "",
                    Duration = durMs,
                    Thumbnail = track.ThumbnailUrl ?? ""
                };

                var queue = currentQueueTracks.Take(50).Select(t => new TrackInfo
                {
                    Id = t.VideoId,
                    Title = t.Title ?? "",
                    Artist = t.ChannelName ?? "",
                    Thumbnail = t.ThumbnailUrl ?? ""
                }).ToList();

                await mgr.SendPlaybackActionAsync(PlaybackActions.ChangeTrack, track.VideoId, 0, trackInfo, queue, "Queue");
                await mgr.SendPlaybackActionAsync(PlaybackActions.Play, "", (long)_appMediaPlayer.Position.TotalMilliseconds, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogether] OnTrackStartedAsHost error: " + ex.Message);
            }
        }

        /// <summary>
        /// Update host track duration once NaturalDuration is loaded by MediaPlayer,
        /// ensuring metroserver and guests (like MetroList) know the true duration and do not clamp playback/seek to 180s.
        /// </summary>
        public async void UpdateHostTrackDuration(long realDurationMs)
        {
            var mgr = ListenTogetherManager.Instance;
            if (!mgr.InRoom || !mgr.IsHost || mgr.CurrentTrack == null) return;
            if (realDurationMs <= 0) return;
            // Only update if duration was unknown (<= 0) and we are within the first 4 seconds of playback
            if (mgr.CurrentTrack.Duration > 0) return;

            long currentPos = 0;
            try { if (_appMediaPlayer != null) currentPos = (long)_appMediaPlayer.Position.TotalMilliseconds; } catch { }
            if (currentPos > 4000) return;

            Debug.WriteLine(string.Format("[ListenTogether] Updating host track duration from {0}ms to {1}ms", mgr.CurrentTrack.Duration, realDurationMs));
            mgr.CurrentTrack.Duration = realDurationMs;

            try
            {
                var queue = currentQueueTracks.Take(50).Select(t => new TrackInfo
                {
                    Id = t.VideoId,
                    Title = t.Title ?? "",
                    Artist = t.ChannelName ?? "",
                    Thumbnail = t.ThumbnailUrl ?? ""
                }).ToList();

                await mgr.SendPlaybackActionAsync(PlaybackActions.ChangeTrack, mgr.CurrentTrack.Id, currentPos, mgr.CurrentTrack, queue, "Queue");
                if (_appMediaPlayer != null && _appMediaPlayer.CurrentState == MediaPlayerState.Playing)
                {
                    await mgr.SendPlaybackActionAsync(PlaybackActions.Play, mgr.CurrentTrack.Id, currentPos, null);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogether] UpdateHostTrackDuration error: " + ex.Message);
            }
        }

        /// <summary>
        /// Called when Play/Pause is toggled as Host.
        /// </summary>
        public async void OnPlayPauseChangedAsHost(bool isPlaying)
        {
            var mgr = ListenTogetherManager.Instance;
            if (!mgr.InRoom || !mgr.IsHost || _isApplyingRemoteAction) return;

            try
            {
                string action = isPlaying ? PlaybackActions.Play : PlaybackActions.Pause;
                long pos = (long)_appMediaPlayer.Position.TotalMilliseconds;
                await mgr.SendPlaybackActionAsync(action, "", pos, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogether] OnPlayPauseChangedAsHost error: " + ex.Message);
            }
        }

        /// <summary>
        /// Called when Seek occurs as Host.
        /// </summary>
        public async void OnSeekOccurredAsHost(long positionMs)
        {
            var mgr = ListenTogetherManager.Instance;
            if (!mgr.InRoom || !mgr.IsHost || _isApplyingRemoteAction) return;

            try
            {
                await mgr.SendPlaybackActionAsync(PlaybackActions.Seek, "", positionMs, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogether] OnSeekOccurredAsHost error: " + ex.Message);
            }
        }

        private void OnRemotePlaybackActionReceived(PlaybackActionPayload act)
        {
            if (!Dispatcher.HasThreadAccess)
            {
                var ign = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => OnRemotePlaybackActionReceived(act));
                return;
            }

            var mgr = ListenTogetherManager.Instance;
            if (!mgr.InRoom || mgr.IsHost || act == null) return;

            double localPosMs = 0;
            double naturalDurMs = 0;
            try
            {
                if (_appMediaPlayer != null)
                {
                    localPosMs = _appMediaPlayer.Position.TotalMilliseconds;
                    naturalDurMs = _appMediaPlayer.NaturalDuration.TotalMilliseconds;
                }
            }
            catch { }

            Debug.WriteLine(string.Format("[ListenTogether RECV] Action={0}, Pos={1}ms, LocalPos={2:F0}ms, NatDur={3:F0}ms, RoomDur={4}ms",
                act.Action, act.Position, localPosMs, naturalDurMs, mgr.CurrentTrack != null ? mgr.CurrentTrack.Duration : 0));

            // MetroList / MetroServer 3-minute clamp detection:
            // When MetroList host doesn't have track duration, it falls back to 180,000ms (3 minutes).
            // MetroServer then clamps any heartbeat or sync position >= 180,000ms down to 180,000ms.
            // If our actual local media is longer than 3 minutes and local playback is already at or beyond 3:00,
            // ignore incoming Play heartbeat actions clamped at 180,000ms to avoid repeatedly yanking playback back to 3:01.
            bool isClampedHeartbeat = string.Equals(act.Action, PlaybackActions.Play, StringComparison.OrdinalIgnoreCase)
                && (mgr.CurrentTrack == null || mgr.CurrentTrack.Duration <= 180000)
                && act.Position >= 179000 && act.Position <= 181000
                && naturalDurMs > 185000 && localPosMs >= 179000;

            if (isClampedHeartbeat)
            {
                Debug.WriteLine("[ListenTogether] Ignored 180s clamped heartbeat to maintain smooth playback past 3:00");
                _pendingRemotePlay = true;
                if (_appMediaPlayer != null && _appMediaPlayer.CurrentState != MediaPlayerState.Playing && !_isBufferingRemoteTrack)
                {
                    _appMediaPlayer.Play();
                }
                SetPlayPauseIcon(true);
                return;
            }

            _isApplyingRemoteAction = true;
            try
            {
                if (act.Action == PlaybackActions.Play)
                {
                    _pendingRemotePlay = true;
                    _pendingRemotePosition = act.Position;
                    if (!_isBufferingRemoteTrack && _appMediaPlayer != null && _appMediaPlayer.CurrentState != MediaPlayerState.Closed)
                    {
                        if (_appMediaPlayer.CurrentState != MediaPlayerState.Playing)
                        {
                            _appMediaPlayer.Play();
                        }
                        long corrected = mgr.PositionAt(act.Position, true);
                        double currentMs = _appMediaPlayer.Position.TotalMilliseconds;
                        if (Math.Abs(currentMs - corrected) > 750)
                        {
                            _appMediaPlayer.Position = TimeSpan.FromMilliseconds(corrected);
                        }
                        SetPlayPauseIcon(true);
                    }
                }
                else if (act.Action == PlaybackActions.Pause)
                {
                    _pendingRemotePlay = false;
                    _pendingRemotePosition = act.Position;
                    if (!_isBufferingRemoteTrack && _appMediaPlayer != null)
                    {
                        if (_appMediaPlayer.CurrentState == MediaPlayerState.Playing)
                        {
                            _appMediaPlayer.Pause();
                        }
                        if (act.Position > 0)
                        {
                            bool isClampedPause = (mgr.CurrentTrack == null || mgr.CurrentTrack.Duration <= 180000)
                                && act.Position >= 179000 && act.Position <= 181000
                                && naturalDurMs > 185000 && localPosMs >= 179000;

                            if (!isClampedPause)
                            {
                                long corrected = mgr.PositionAt(act.Position, false);
                                double currentMs = _appMediaPlayer.Position.TotalMilliseconds;
                                if (Math.Abs(currentMs - corrected) > 750)
                                {
                                    _appMediaPlayer.Position = TimeSpan.FromMilliseconds(corrected);
                                }
                            }
                        }
                        SetPlayPauseIcon(false);
                    }
                }
                else if (act.Action == PlaybackActions.Seek)
                {
                    _pendingRemotePosition = act.Position;
                    if (!_isBufferingRemoteTrack && _appMediaPlayer != null && _appMediaPlayer.CurrentState != MediaPlayerState.Closed)
                    {
                        long corrected = mgr.PositionAt(act.Position, mgr.IsPlaying);
                        Debug.WriteLine(string.Format("[ListenTogether] Applying SEEK to {0}ms (raw pos={1}ms)", corrected, act.Position));
                        _appMediaPlayer.Position = TimeSpan.FromMilliseconds(corrected);
                    }
                }
                else if (act.Action == PlaybackActions.SyncQueue)
                {
                    if (act.Queue != null && act.Queue.Count > 0)
                    {
                        QueueListView.ItemsSource = null;
                        currentQueueTracks.Clear();
                        if (currentTrack != null)
                        {
                            currentQueueTracks.Add(currentTrack);
                        }
                        foreach (var q in act.Queue.Take(50))
                        {
                            if (q != null && !string.IsNullOrEmpty(q.Id) && (currentTrack == null || q.Id != currentTrack.VideoId))
                            {
                                currentQueueTracks.Add(TrackInfoToYouTubeTrack(q));
                            }
                        }
                        QueueListView.ItemsSource = currentQueueTracks;
                        UpdateQueueActiveState();
                    }
                }

                if (mgr.SyncVolume && act.Volume > 0 && act.Volume <= 1.0f)
                {
                    try
                    {
                        if (_appMediaPlayer != null)
                        {
                            _appMediaPlayer.Volume = act.Volume;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogether] Apply remote action error: " + ex.Message);
            }
            finally
            {
                _isApplyingRemoteAction = false;
            }
        }

        private void OnRemoteTrackChanged(TrackInfo remoteTrack)
        {
            if (!Dispatcher.HasThreadAccess)
            {
                var ign = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => OnRemoteTrackChanged(remoteTrack));
                return;
            }

            var mgr = ListenTogetherManager.Instance;
            if (!mgr.InRoom || mgr.IsHost || remoteTrack == null) return;

            if (currentTrack != null && currentTrack.VideoId == remoteTrack.Id)
            {
                return; // Already playing/buffering this track
            }

            _lastRemoteTrackId = remoteTrack.Id;
            PlayRemoteTrack(remoteTrack);
        }

        private async void PlayRemoteTrack(TrackInfo remoteTrack)
        {
            if (remoteTrack == null || string.IsNullOrEmpty(remoteTrack.Id)) return;

            var mgr = ListenTogetherManager.Instance;
            _isBufferingRemoteTrack = true;
            _pendingRemotePlay = mgr.IsPlaying;
            _pendingRemotePosition = mgr.Position;
            _isApplyingRemoteAction = true;

            try
            {
                var ytTrack = TrackInfoToYouTubeTrack(remoteTrack);
                currentTrack = ytTrack;

                // 1. Canonical queue: current track is ALWAYS index 0, followed by upcoming tracks from host
                QueueListView.ItemsSource = null;
                currentQueueTracks.Clear();
                currentQueueTracks.Add(ytTrack);
                if (mgr.Queue != null && mgr.Queue.Count > 0)
                {
                    foreach (var q in mgr.Queue.Take(50))
                    {
                        if (q != null && !string.IsNullOrEmpty(q.Id) && q.Id != ytTrack.VideoId)
                        {
                            currentQueueTracks.Add(TrackInfoToYouTubeTrack(q));
                        }
                    }
                }
                QueueListView.ItemsSource = currentQueueTracks;

                // 2. Update UI metadata
                MiniTitle.Text = ytTrack.Title;
                BigTitle.Text = ytTrack.Title;
                MiniArtist.Text = ytTrack.ChannelName;
                BigArtist.Text = ytTrack.ChannelName;
                MenuTitle.Text = ytTrack.Title;
                MenuArtist.Text = ytTrack.ChannelName;

                if (NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible)
                {
                    var ignored2 = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => StartTitleMarquee());
                }

                SetPlayPauseIcon(_pendingRemotePlay);

                if (!string.IsNullOrEmpty(ytTrack.ThumbnailUrl))
                {
                    try
                    {
                        var bigBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetNowPlayingThumbnail(ytTrack.ThumbnailUrl), UriKind.Absolute));
                        bigBmp.DecodePixelWidth = Services.MemoryHelper.IsLowMemoryDevice ? 320 : 480;
                        BigCoverImage.ImageSource = bigBmp;
                        if (AlbumArtEntranceStoryboard != null) AlbumArtEntranceStoryboard.Begin();
                        MenuCoverImage.ImageSource = bigBmp;

                        var miniBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetSquareThumbnail(ytTrack.ThumbnailUrl), UriKind.Absolute));
                        miniBmp.DecodePixelWidth = 100;
                        MiniCoverImage.ImageSource = miniBmp;
                    }
                    catch { }
                }

                bool isFav = favoriteTracks.Any(t => t.VideoId == ytTrack.VideoId);
                BigHeartBtn.Content = isFav ? "♥" : "♡";
                BigHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;

                var ignoredLyrics = UpdateLyricsAsync(ytTrack.Title, ytTrack.ChannelName);
                UpdateNowPlayingGradient(ytTrack.Title, ytTrack.ChannelName, ytTrack.ThumbnailUrl);
                if (_isAppleMusicStyle)
                {
                    UpdateAppleMusicCompactHeaders();
                }

                if (MusicSlider != null) MusicSlider.Value = 0;
                if (AppleMusicSlider != null) AppleMusicSlider.Value = 0;

                // 3. Resolve stream URL
                string resolvedUrl = "";
                try
                {
                    resolvedUrl = await InnerTubeClient.ResolveStreamUrlAsync(ytTrack.VideoId) ?? "";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[ListenTogether] ResolveStreamUrlAsync error: " + ex.Message);
                }

                // 4. Prepare IPC slice for BackgroundTask (capped to 100 items around current)
                var activeList = currentQueueTracks;
                int trackIdx = -1;
                for (int i = 0; i < activeList.Count; i++)
                {
                    if (activeList[i].VideoId == ytTrack.VideoId) { trackIdx = i; break; }
                }
                if (trackIdx < 0) trackIdx = 0;

                int maxItems = 100;
                int half = maxItems / 2;
                int sliceStart = Math.Max(0, trackIdx - half);
                int sliceEnd = Math.Min(activeList.Count - 1, trackIdx + half);
                int count = sliceEnd - sliceStart + 1;
                int relativeStartIndex = trackIdx - sliceStart;
                if (relativeStartIndex < 0) relativeStartIndex = 0;

                string[] urls = new string[count];
                string[] titles = new string[count];
                string[] artists = new string[count];
                string[] videoIds = new string[count];
                string[] thumbnails = new string[count];

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

                bool shouldStartPaused = !_pendingRemotePlay && !mgr.IsPlaying;
                var message = new Windows.Foundation.Collections.ValueSet {
                    { "UpdatePlaylist", "" },
                    { "Urls", urls },
                    { "Titles", titles },
                    { "Artists", artists },
                    { "VideoIds", videoIds },
                    { "Thumbnails", thumbnails },
                    { "StartIndex", relativeStartIndex },
                    { "FastUrl", urls[relativeStartIndex] },
                    { "StartPaused", shouldStartPaused }
                };

                try
                {
                    BackgroundMediaPlayer.SendMessageToBackground(message);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[ListenTogether] SendMessageToBackground error: " + ex.Message);
                }

                UpdateQueueActiveState();

                // 5. Notify server that buffer is ready
                await mgr.ReportBufferReadyAsync(remoteTrack.Id);
                _isBufferingRemoteTrack = false;

                // 6. If playback should now be running
                if (_pendingRemotePlay || mgr.IsPlaying)
                {
                    await Task.Delay(250);
                    try
                    {
                        if (_pendingRemotePosition > 0)
                        {
                            long targetPos = mgr.PositionAt(_pendingRemotePosition, true);
                            _appMediaPlayer.Position = TimeSpan.FromMilliseconds(targetPos);
                        }
                        if (_appMediaPlayer.CurrentState != MediaPlayerState.Playing)
                        {
                            _appMediaPlayer.Play();
                        }
                        SetPlayPauseIcon(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[ListenTogether] Post-buffer play error: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogether] PlayRemoteTrack error: " + ex.Message);
                _isBufferingRemoteTrack = false;
            }
            finally
            {
                _isApplyingRemoteAction = false;
            }
        }

        private YouTubeTrack TrackInfoToYouTubeTrack(TrackInfo info)
        {
            if (info == null) return null;
            return new YouTubeTrack
            {
                VideoId = info.Id,
                Title = info.Title ?? "",
                ChannelName = info.Artist ?? "",
                AlbumName = info.Album ?? "",
                ThumbnailUrl = info.Thumbnail ?? "",
                Duration = (info.Duration > 0)
                    ? string.Format("{0}:{1:D2}", (int)(info.Duration / 60000), (int)((info.Duration % 60000) / 1000))
                    : ""
            };
        }

        #endregion

        #region Listen Together Settings

        private void LoadListenTogetherSettings()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                var mgr = ListenTogetherManager.Instance;

                if (settings.ContainsKey("LtDisplayName"))
                {
                    string name = settings["LtDisplayName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        mgr.SelfUsername = name;
                        if (LtDisplayNameBox != null) LtDisplayNameBox.Text = name;
                    }
                }

                if (settings.ContainsKey("LtAutoApprove"))
                {
                    bool autoApprove = (bool)settings["LtAutoApprove"];
                    mgr.AutoApproveJoins = autoApprove;
                    if (LtAutoApproveToggle != null) LtAutoApproveToggle.IsOn = autoApprove;
                }

                if (settings.ContainsKey("LtAutoApproveSuggestions"))
                {
                    bool autoSug = (bool)settings["LtAutoApproveSuggestions"];
                    mgr.AutoApproveSuggestions = autoSug;
                    if (LtAutoApproveSuggestionsToggle != null) LtAutoApproveSuggestionsToggle.IsOn = autoSug;
                }

                if (settings.ContainsKey("LtSyncVolume"))
                {
                    bool syncVol = (bool)settings["LtSyncVolume"];
                    mgr.SyncVolume = syncVol;
                    if (LtSyncVolumeToggle != null) LtSyncVolumeToggle.IsOn = syncVol;
                }

                if (settings.ContainsKey("LtServerUrl"))
                {
                    string sUrl = settings["LtServerUrl"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(sUrl))
                    {
                        mgr.Client.ServerUrl = sUrl.Trim();
                    }
                }
            }
            catch { }
        }

        private void LtOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (LtSettingsPanel == null) return;
            var mgr = ListenTogetherManager.Instance;

            if (LtSettingsNicknameBox != null)
            {
                LtSettingsNicknameBox.Text = string.IsNullOrWhiteSpace(LtDisplayNameBox?.Text) ? mgr.SelfUsername : LtDisplayNameBox.Text;
            }

            if (LtSettingsServerUrlBox != null)
            {
                LtSettingsServerUrlBox.Text = mgr.Client.ServerUrl;
            }

            if (LtAutoApproveToggle != null)
            {
                LtAutoApproveToggle.IsOn = mgr.AutoApproveJoins;
            }

            if (LtAutoApproveSuggestionsToggle != null)
            {
                LtAutoApproveSuggestionsToggle.IsOn = mgr.AutoApproveSuggestions;
            }

            if (LtSyncVolumeToggle != null)
            {
                LtSyncVolumeToggle.IsOn = mgr.SyncVolume;
            }

            if (LtSettingsActiveServerText != null)
            {
                string sUrl = mgr.Client.ServerUrl;
                if (string.Equals(sUrl, ListenTogetherClient.DefaultServerUrl, StringComparison.OrdinalIgnoreCase))
                {
                    LtSettingsActiveServerText.Text = "The Meowery · Poland (Default)";
                    LtSettingsActiveServerText.Foreground = new SolidColorBrush(Color.FromArgb(255, 29, 185, 84));
                }
                else
                {
                    LtSettingsActiveServerText.Text = sUrl;
                    LtSettingsActiveServerText.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 229, 255));
                }
            }

            LtSettingsPanel.Visibility = Visibility.Visible;
        }

        private void LtCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            if (LtSettingsPanel != null)
            {
                LtSettingsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void LtSettingsCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            LtOpenSettings_Click(sender, null);
        }

        private void LtAutoApproveToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (LtAutoApproveToggle == null) return;
            var mgr = ListenTogetherManager.Instance;
            mgr.AutoApproveJoins = LtAutoApproveToggle.IsOn;
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["LtAutoApprove"] = LtAutoApproveToggle.IsOn;
            }
            catch { }
        }

        private void LtAutoApproveSuggestionsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (LtAutoApproveSuggestionsToggle == null) return;
            var mgr = ListenTogetherManager.Instance;
            mgr.AutoApproveSuggestions = LtAutoApproveSuggestionsToggle.IsOn;
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["LtAutoApproveSuggestions"] = LtAutoApproveSuggestionsToggle.IsOn;
            }
            catch { }
        }

        private void LtSyncVolumeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (LtSyncVolumeToggle == null) return;
            var mgr = ListenTogetherManager.Instance;
            mgr.SyncVolume = LtSyncVolumeToggle.IsOn;
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["LtSyncVolume"] = LtSyncVolumeToggle.IsOn;
            }
            catch { }
        }

        private void LtSaveNickname_Click(object sender, RoutedEventArgs e)
        {
            if (LtSettingsNicknameBox == null) return;
            string name = LtSettingsNicknameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowToast("Name cannot be empty");
                return;
            }

            var mgr = ListenTogetherManager.Instance;
            mgr.SelfUsername = name;
            if (LtDisplayNameBox != null) LtDisplayNameBox.Text = name;

            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["LtDisplayName"] = name;
            }
            catch { }

            ShowToast("Display name saved: " + name);
        }

        private async void LtSettingsApplyServer_Click(object sender, RoutedEventArgs e)
        {
            if (LtSettingsServerUrlBox == null) return;
            string newUrl = LtSettingsServerUrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newUrl) || (!newUrl.StartsWith("ws://") && !newUrl.StartsWith("wss://")))
            {
                ShowToast("Enter a valid ws:// or wss:// URL");
                return;
            }

            var mgr = ListenTogetherManager.Instance;
            mgr.Client.ServerUrl = newUrl;
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["LtServerUrl"] = newUrl;
            }
            catch { }

            if (LtSettingsActiveServerText != null)
            {
                LtSettingsActiveServerText.Text = newUrl;
                LtSettingsActiveServerText.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 229, 255));
            }

            if (mgr.Connection == ConnectionState.Connected || mgr.Connection == ConnectionState.Connecting)
            {
                mgr.Disconnect();
                await mgr.ConnectAsync();
            }

            ShowToast("Server updated!");
        }

        private async void LtSettingsResetServer_Click(object sender, RoutedEventArgs e)
        {
            var mgr = ListenTogetherManager.Instance;
            string defaultUrl = ListenTogetherClient.DefaultServerUrl;
            mgr.Client.ServerUrl = defaultUrl;
            if (LtSettingsServerUrlBox != null) LtSettingsServerUrlBox.Text = defaultUrl;

            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["LtServerUrl"] = defaultUrl;
            }
            catch { }

            if (LtSettingsActiveServerText != null)
            {
                LtSettingsActiveServerText.Text = "The Meowery · Poland (Default)";
                LtSettingsActiveServerText.Foreground = new SolidColorBrush(Color.FromArgb(255, 29, 185, 84));
            }

            if (mgr.Connection == ConnectionState.Connected || mgr.Connection == ConnectionState.Connecting)
            {
                mgr.Disconnect();
                await mgr.ConnectAsync();
            }

            ShowToast("Reset to default server!");
        }

        #endregion
    }
}
