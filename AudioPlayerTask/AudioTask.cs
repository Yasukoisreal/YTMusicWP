using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Playback;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using System.Threading.Tasks;

namespace AudioPlayerTask
{
    public sealed class AudioTask : IBackgroundTask
    {
        private BackgroundTaskDeferral _deferral;
        private SystemMediaTransportControls _systemControls;
        private MediaPlayer _mediaPlayer;

        private List<string> _trackList = new List<string>();
        private List<string> _titleList = new List<string>();
        private List<string> _artistList = new List<string>();
        private List<string> _videoIdList = new List<string>();
        private List<string> _thumbnailList = new List<string>();

        private int _currentTrackIndex = -1;
        private Random _rand = new Random();
        private int _retryCount = 0;
        private string _currentLoadedVidId = "";

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();

            _systemControls = SystemMediaTransportControls.GetForCurrentView();
            _systemControls.IsEnabled = true;
            _systemControls.ButtonPressed += SystemControls_ButtonPressed;
            _systemControls.IsPlayEnabled = true;
            _systemControls.IsPauseEnabled = true;
            _systemControls.IsNextEnabled = true;
            _systemControls.IsPreviousEnabled = true;

            _mediaPlayer = BackgroundMediaPlayer.Current;
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
            _mediaPlayer.CurrentStateChanged += MediaPlayer_CurrentStateChanged;

            BackgroundMediaPlayer.MessageReceivedFromForeground += BackgroundMediaPlayer_MessageReceivedFromForeground;
            taskInstance.Canceled += TaskInstance_Canceled;
        }

        private void TaskInstance_Canceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            try
            {
                _systemControls.ButtonPressed -= SystemControls_ButtonPressed;
                _systemControls.IsEnabled = false;

                _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
                _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
                _mediaPlayer.CurrentStateChanged -= MediaPlayer_CurrentStateChanged;

                BackgroundMediaPlayer.MessageReceivedFromForeground -= BackgroundMediaPlayer_MessageReceivedFromForeground;
                BackgroundMediaPlayer.Shutdown();
            }
            catch { }

            if (_deferral != null) _deferral.Complete();
        }

        private void BackgroundMediaPlayer_MessageReceivedFromForeground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            if (e.Data.ContainsKey("UpdatePlaylist"))
            {
                _trackList = new List<string>((string[])e.Data["Urls"]);
                _titleList = new List<string>((string[])e.Data["Titles"]);
                _artistList = new List<string>((string[])e.Data["Artists"]);
                _videoIdList = new List<string>((string[])e.Data["VideoIds"]);
                _thumbnailList = new List<string>((string[])e.Data["Thumbnails"]);

                _currentTrackIndex = (int)e.Data["StartIndex"];

                // OPTIMIZATION: Nếu Foreground đã resolve URL nhanh (FastUrl), override URL tại startIndex
                // tránh phải resolve lại khi Audio Task có thể dùng URL cũ.
                if (e.Data.ContainsKey("FastUrl"))
                {
                    string fastUrl = e.Data["FastUrl"].ToString();
                    if (!string.IsNullOrEmpty(fastUrl) && _currentTrackIndex < _trackList.Count)
                        _trackList[_currentTrackIndex] = fastUrl;
                }

                StartPlayback();
            }
            else if (e.Data.ContainsKey("NextTrackMessage")) MoveNext();
            else if (e.Data.ContainsKey("PrevTrackMessage")) MovePrevious();
        }

        private void StartPlayback()
        {
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _trackList.Count) return;

            string vidId = _videoIdList[_currentTrackIndex];
            string trackUrl = _trackList[_currentTrackIndex];

            // Nếu đang phát lại bài hát cũ
            if (vidId == _currentLoadedVidId && _mediaPlayer.CurrentState != MediaPlayerState.Closed)
            {
                try
                {
                    _mediaPlayer.Position = TimeSpan.Zero;
                    _mediaPlayer.Play();
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    UpdateSystemMediaControls();
                }
                catch { }
                return;
            }

            _mediaPlayer.AutoPlay = false;

            try
            {
                // Cập nhật UI ngay lập tức
                UpdateSystemMediaControls();

                // Truyền trực tiếp Link Streaming (Cloudflare) hoặc Link Offline vào MediaPlayer
                _mediaPlayer.SetUriSource(new Uri(trackUrl));
                _currentLoadedVidId = vidId;

                _mediaPlayer.Play();
                _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
            }
            catch (Exception ex)
            {
                ReportErrorToUI("Stream Error: " + ex.Message.Split('\n')[0]);
            }
        }

        private void ReportErrorToUI(string errorDetail)
        {
            string title = "Youtify Status";
            if (_currentTrackIndex >= 0 && _currentTrackIndex < _titleList.Count)
                title = _titleList[_currentTrackIndex];

            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["CurrentTitle"] = title;
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["CurrentArtist"] = errorDetail;

                var msg = new ValueSet();
                msg.Add("TrackChanged", "");
                msg.Add("NewTitle", title);
                msg.Add("NewArtist", errorDetail);
                if (_currentTrackIndex >= 0 && _currentTrackIndex < _thumbnailList.Count)
                    msg.Add("NewThumbnail", _thumbnailList[_currentTrackIndex]);
                BackgroundMediaPlayer.SendMessageToForeground(msg);
            }
            catch { }

            try
            {
                _systemControls.DisplayUpdater.MusicProperties.Title = title;
                _systemControls.DisplayUpdater.MusicProperties.Artist = errorDetail;
                _systemControls.DisplayUpdater.Update();
            }
            catch { }
        }

        private void UpdateSystemMediaControls()
        {
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _titleList.Count) return;

            string title = _titleList[_currentTrackIndex];
            string artist = _artistList[_currentTrackIndex];
            string thumb = _thumbnailList[_currentTrackIndex];
            string vidId = _videoIdList[_currentTrackIndex];

            // SMTC Update
            try
            {
                _systemControls.DisplayUpdater.Type = MediaPlaybackType.Music;
                _systemControls.DisplayUpdater.MusicProperties.Title = title;
                _systemControls.DisplayUpdater.MusicProperties.Artist = artist;
                _systemControls.DisplayUpdater.Update();
            }
            catch { }

            // LocalSettings: chỉ ghi nếu giá trị thay đổi, tránh write thao tác dư thừa
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if (localSettings["CurrentTitle"]?.ToString() != title) localSettings["CurrentTitle"] = title;
                if (localSettings["CurrentArtist"]?.ToString() != artist) localSettings["CurrentArtist"] = artist;
                if (localSettings["CurrentVideoId"]?.ToString() != vidId) localSettings["CurrentVideoId"] = vidId;
                if (localSettings["CurrentThumbnail"]?.ToString() != thumb) localSettings["CurrentThumbnail"] = thumb;
            }
            catch { }

            // Live Tile
            try
            {
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(true);
                updater.Clear();

                string safeTitle = System.Net.WebUtility.HtmlEncode(title);
                string safeThumb = System.Net.WebUtility.HtmlEncode(thumb);

                string xml = string.Format(
                    "<tile><visual version=\"2\">" +
                    "<binding template=\"TileSquare150x150Image\"><image id=\"1\" src=\"{0}\" placement=\"background\"/></binding>" +
                    "<binding template=\"TileWide310x150ImageAndText01\"><image id=\"1\" src=\"{0}\" placement=\"background\"/><text id=\"1\">{1}</text></binding>" +
                    "</visual></tile>", safeThumb, safeTitle);

                var doc = new XmlDocument();
                doc.LoadXml(xml);
                updater.Update(new TileNotification(doc));
            }
            catch { }

            // Notify Foreground
            try
            {
                var msg = new ValueSet();
                msg.Add("TrackChanged", "");
                msg.Add("NewTitle", title);
                msg.Add("NewArtist", artist);
                msg.Add("NewVideoId", vidId);
                msg.Add("NewThumbnail", thumb);
                BackgroundMediaPlayer.SendMessageToForeground(msg);
            }
            catch { }
        }

        private void MoveNext()
        {
            if (_trackList.Count == 0) return;
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            bool shuffle = localSettings.ContainsKey("ShuffleMode") ? (bool)localSettings["ShuffleMode"] : false;
            int repeat = localSettings.ContainsKey("RepeatMode") ? (int)localSettings["RepeatMode"] : 0;

            if (repeat == 2) { StartPlayback(); return; }

            if (shuffle) _currentTrackIndex = _rand.Next(0, _trackList.Count);
            else
            {
                _currentTrackIndex++;
                if (_currentTrackIndex >= _trackList.Count)
                {
                    if (repeat == 1) _currentTrackIndex = 0;
                    else { _currentTrackIndex = _trackList.Count - 1; return; }
                }
            }
            StartPlayback();
        }

        private void MovePrevious()
        {
            if (_trackList.Count == 0) return;

            if (_mediaPlayer.Position.TotalSeconds > 3)
            {
                _mediaPlayer.Position = TimeSpan.Zero;
                _mediaPlayer.Play();
                return;
            }

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            bool shuffle = localSettings.ContainsKey("ShuffleMode") ? (bool)localSettings["ShuffleMode"] : false;
            int repeat = localSettings.ContainsKey("RepeatMode") ? (int)localSettings["RepeatMode"] : 0;

            if (repeat == 2) { StartPlayback(); return; }

            if (shuffle) _currentTrackIndex = _rand.Next(0, _trackList.Count);
            else
            {
                _currentTrackIndex--;
                if (_currentTrackIndex < 0)
                {
                    if (repeat == 1) _currentTrackIndex = _trackList.Count - 1;
                    else { _currentTrackIndex = 0; return; }
                }
            }
            StartPlayback();
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args) => MoveNext();

        private void MediaPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            try
            {
                if (sender.CurrentState == MediaPlayerState.Playing)
                {
                    _retryCount = 0;
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                }
                else if (sender.CurrentState == MediaPlayerState.Paused)
                {
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                }
                else if (sender.CurrentState == MediaPlayerState.Closed)
                {
                    _systemControls.PlaybackStatus = MediaPlaybackStatus.Closed;
                }
            }
            catch { }
        }

        private async void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            // Reset cached vidId để force reload source URI khi retry
            _currentLoadedVidId = "";

            if (_retryCount < 3)
            {
                // [OPT-C4] Exponential backoff: 1s → 2s → 4s thay vì flat 1.5s x3
                // Giảm stress lên network và server trên thiết bị 512MB RAM
                int delayMs = 1000 * (1 << _retryCount); // 1000, 2000, 4000
                _retryCount++;
                await Task.Delay(delayMs);
                StartPlayback();
                return;
            }

            _retryCount = 0;
            string errorDetail = "Stream Error";
            if (args.ExtendedErrorCode != null)
                errorDetail = "Code: " + args.ExtendedErrorCode.HResult.ToString();

            ReportErrorToUI(errorDetail);
        }

        private void SystemControls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    try
                    {
                        if (_mediaPlayer.CurrentState == MediaPlayerState.Closed) StartPlayback();
                        else _mediaPlayer.Play();
                    }
                    catch { StartPlayback(); }
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    try { _mediaPlayer.Pause(); } catch { }
                    break;
                case SystemMediaTransportControlsButton.Next: MoveNext(); break;
                case SystemMediaTransportControlsButton.Previous: MovePrevious(); break;
            }
        }
    }
}