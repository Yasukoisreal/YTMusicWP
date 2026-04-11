using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Playback;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Security.Cryptography.Certificates;

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
        private int _playbackId = 0;
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
                StartPlayback();
            }
            else if (e.Data.ContainsKey("NextTrackMessage")) MoveNext();
            else if (e.Data.ContainsKey("PrevTrackMessage")) MovePrevious();
        }

        private async void StartPlayback()
        {
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _trackList.Count) return;

            string vidId = _videoIdList[_currentTrackIndex];

            if (vidId == _currentLoadedVidId)
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
            _playbackId++;
            int currentId = _playbackId;

            try
            {
                if (vidId.StartsWith("LOCAL:"))
                {
                    string localUrl = "ms-appdata:///local/" + vidId.Substring(6);
                    _mediaPlayer.SetUriSource(new Uri(localUrl));
                    _currentLoadedVidId = vidId;
                }
                else
                {
                    // HIỆN NGAY TÊN BÀI HÁT VÀ ẢNH BÌA LÊN UI TRONG LÚC CHỜ TẢI ĐỆM
                    UpdateSystemMediaControls();

                    StorageFolder folder = ApplicationData.Current.LocalFolder;

                    try
                    {
                        var files = await folder.GetFilesAsync();
                        foreach (var f in files)
                        {
                            if (f.Name.StartsWith("temp_play_") && f.Name != "temp_play_" + vidId + ".m4a")
                            {
                                await f.DeleteAsync(StorageDeleteOption.PermanentDelete);
                            }
                        }
                    }
                    catch { }

                    string fileName = "temp_play_" + vidId + ".m4a";
                    StorageFile file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                    string downloadUrl = "https://summer-fire-6e3f.adianhseng.workers.dev/api/play?v=" + vidId + "&key=LumiaWP81-An&nocache=" + Guid.NewGuid().ToString();

                    var filter = new HttpBaseProtocolFilter();
                    filter.AllowAutoRedirect = true;
                    filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                    filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                    filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);

                    using (var client = new HttpClient(filter))
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(downloadUrl));
                        var response = await client.SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead);

                        if (response.IsSuccessStatusCode)
                        {
                            using (var stream = await response.Content.ReadAsInputStreamAsync())
                            {
                                using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                                {
                                    await RandomAccessStream.CopyAndCloseAsync(stream, fileStream.GetOutputStreamAt(0));
                                }
                            }
                        }
                        else
                        {
                            if (currentId == _playbackId) ReportErrorToUI("Server Error: " + (int)response.StatusCode);
                            return;
                        }
                    }

                    if (currentId != _playbackId) return;

                    _mediaPlayer.SetUriSource(new Uri("ms-appdata:///local/" + fileName));
                    _currentLoadedVidId = vidId;
                }

                _mediaPlayer.Play();
                _systemControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                UpdateSystemMediaControls();
            }
            catch (Exception ex)
            {
                if (currentId == _playbackId) ReportErrorToUI("Net Error: " + ex.Message.Split('\n')[0]);
            }
        }

        private void ReportErrorToUI(string errorDetail)
        {
            // Sửa lỗi UI: Lấy tên bài hát thay vì hiện "Youtify Status" khi có lỗi
            string title = "Youtify Status";
            if (_currentTrackIndex >= 0 && _currentTrackIndex < _titleList.Count)
            {
                title = _titleList[_currentTrackIndex];
            }

            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["CurrentTitle"] = title;
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["CurrentArtist"] = errorDetail;

                var msg = new ValueSet();
                msg.Add("TrackChanged", "");
                msg.Add("NewTitle", title);
                msg.Add("NewArtist", errorDetail);
                if (_currentTrackIndex >= 0 && _currentTrackIndex < _thumbnailList.Count)
                {
                    msg.Add("NewThumbnail", _thumbnailList[_currentTrackIndex]);
                }
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

            try
            {
                _systemControls.DisplayUpdater.Type = MediaPlaybackType.Music;
                _systemControls.DisplayUpdater.MusicProperties.Title = title;
                _systemControls.DisplayUpdater.MusicProperties.Artist = artist;
                _systemControls.DisplayUpdater.Update();
            }
            catch { }

            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["CurrentTitle"] = title;
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["CurrentArtist"] = artist;
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["CurrentVideoId"] = _videoIdList[_currentTrackIndex];
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["CurrentThumbnail"] = thumb;
            }
            catch { }

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

            try
            {
                var msg = new ValueSet();
                msg.Add("TrackChanged", "");
                msg.Add("NewTitle", title);
                msg.Add("NewArtist", artist);
                msg.Add("NewVideoId", _videoIdList[_currentTrackIndex]);
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
            if (_retryCount < 3)
            {
                _retryCount++;
                await Task.Delay(1500);
                StartPlayback();
                return;
            }

            _retryCount = 0;
            string errorDetail = "Stream Expired";
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