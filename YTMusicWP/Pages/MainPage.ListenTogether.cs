using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Playback;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using YTMusicWP.Services.ListenTogether;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private bool _isApplyingRemoteAction;
        private string _lastRemoteTrackId;
        private string _shareRoomCode;

        private void InitializeListenTogether()
        {
            var mgr = ListenTogetherManager.Instance;
            mgr.StateChanged += UpdateListenTogetherUI;
            mgr.RemotePlaybackActionReceived += OnRemotePlaybackActionReceived;
            mgr.RemoteTrackChanged += OnRemoteTrackChanged;

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

                var queue = currentQueueTracks.Select(t => new TrackInfo
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
            var mgr = ListenTogetherManager.Instance;
            if (!mgr.InRoom || mgr.IsHost || act == null) return;

            _isApplyingRemoteAction = true;
            try
            {
                if (act.Action == PlaybackActions.Play)
                {
                    if (_appMediaPlayer.CurrentState != MediaPlayerState.Playing)
                    {
                        _appMediaPlayer.Play();
                    }
                }
                else if (act.Action == PlaybackActions.Pause)
                {
                    if (_appMediaPlayer.CurrentState == MediaPlayerState.Playing)
                    {
                        _appMediaPlayer.Pause();
                    }
                }

                if (act.Action == PlaybackActions.Seek || act.Action == PlaybackActions.Play)
                {
                    long corrected = mgr.PositionAt(act.Position, true);
                    double currentMs = _appMediaPlayer.Position.TotalMilliseconds;
                    if (Math.Abs(currentMs - corrected) > 750)
                    {
                        _appMediaPlayer.Position = TimeSpan.FromMilliseconds(corrected);
                    }
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
            var mgr = ListenTogetherManager.Instance;
            if (!mgr.InRoom || mgr.IsHost || remoteTrack == null) return;

            if (currentTrack != null && currentTrack.VideoId == remoteTrack.Id)
            {
                return; // Already playing this track
            }

            _isApplyingRemoteAction = true;
            try
            {
                _lastRemoteTrackId = remoteTrack.Id;
                var ytTrack = new YouTubeTrack
                {
                    VideoId = remoteTrack.Id,
                    Title = remoteTrack.Title,
                    ChannelName = remoteTrack.Artist,
                    ThumbnailUrl = remoteTrack.Thumbnail,
                    Duration = (remoteTrack.Duration > 0)
                        ? string.Format("{0}:{1:D2}", (int)(remoteTrack.Duration / 60000), (int)((remoteTrack.Duration % 60000) / 1000))
                        : ""
                };

                PlayTrack(ytTrack);

                // Report buffer ready to barrier once stream starts
                var ignored = Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    await mgr.ReportBufferReadyAsync(remoteTrack.Id);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ListenTogether] OnRemoteTrackChanged error: " + ex.Message);
            }
            finally
            {
                _isApplyingRemoteAction = false;
            }
        }

        #endregion
    }
}
