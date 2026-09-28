using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Media.Playback;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        // ==========================================
        // SAMPLES TAB (YouTube Music "Samples" / Shorts)
        // Full-screen music video clips played with a foreground MediaElement (itag 18 360p MP4).
        // Feed, all dynamic and localized through the InnerTube context (hl/gl) like the Home tab:
        //   1. music videos on the user's Home (FEmusic_home; only signed-in homes contain videos),
        //   2. the region's "Video charts" playlists (FEmusic_charts + formData gl), paged on demand.
        // FEmusic_immersive, the real Samples browse, only answers signed-in Android clients, so it is not used.
        // ==========================================

        private const int SamplesTab = 3;
        private const double SampleClipSeconds = 30.0;   // length of the looping clip

        private List<YouTubeTrack> _samples = new List<YouTubeTrack>();
        private int _sampleIndex = 0;
        // Chart playlists that still have more pages: playlist id -> continuation token
        private readonly List<KeyValuePair<string, string>> _samplesMoreSources = new List<KeyValuePair<string, string>>();
        private string _samplesLocale;                   // hl/gl the feed was built for
        private DateTime _samplesLoadedAt = DateTime.MinValue;
        private readonly HashSet<string> _samplesSeen = new HashSet<string>();
        private readonly Random _samplesRandom = new Random();
        private YouTubeTrack _pendingSampleTrack;        // opened from elsewhere (e.g. Home charts)

        private bool _shortsIsOpen = false;
        private int _samplesGeneration = 0;              // cancels stale resolves when swiping fast
        private bool _samplesLoadingMore = false;
        private bool _samplesWasMainPlaying = false;
        private YouTubeTrack _samplesSavedTrack;
        private TimeSpan _samplesSavedPosition;
        private bool _samplesUserPaused = false;
        private TimeSpan _sampleClipStart = TimeSpan.Zero;
        private DispatcherTimer _shortsLoopTimer;
        private bool _samplesEventsHooked = false;

        // ==========================================
        // OPEN / CLOSE (driven by SwitchTab)
        // ==========================================
        private void NavSamples_Click(object sender, RoutedEventArgs e)
        {
            SwitchTab(SamplesTab);
        }

        /// <summary>Opens the Samples tab starting with a specific track (e.g. a video from the Home charts).</summary>
        private void OpenShortsWithTrack(YouTubeTrack track)
        {
            _pendingSampleTrack = track;
            if (_currentTab == SamplesTab && _shortsIsOpen)
            {
                InsertPendingSample();
                ShowSample(_sampleIndex);
            }
            else
            {
                SwitchTab(SamplesTab);
            }
        }

        private void OpenShortsView(int unused = 0)
        {
            SwitchTab(SamplesTab);
        }

        /// <summary>Called by SwitchTab when the Samples tab becomes active.</summary>
        private async void OpenSamplesTab()
        {
            HookSampleVideoEvents();
            _shortsIsOpen = true;
            _samplesUserPaused = false;

            // The main (background) player and the foreground MediaElement must not play at the same time
            try
            {
                _samplesWasMainPlaying = !_playerDisconnected && _appMediaPlayer != null && _appMediaPlayer.CurrentState == MediaPlayerState.Playing;
                if (_samplesWasMainPlaying)
                {
                    _samplesSavedTrack = currentTrack;
                    _samplesSavedPosition = _appMediaPlayer.Position;
                    _appMediaPlayer.Pause();
                }
            }
            catch { _samplesWasMainPlaying = false; }

            Services.MotionHelper.FadeIn(ShortsView);
            ShortsView.Opacity = 1;
            ShortsView.SampleStageTransform.Y = 0;

            string locale = InnerTubeClient.CurrentLanguage + "-" + InnerTubeClient.CurrentRegion;
            bool stale = _samples.Count == 0 || _samplesLocale != locale || (DateTime.Now - _samplesLoadedAt).TotalMinutes > 30;
            if (stale)
            {
                ShowSampleMessage(null);
                ShortsView.SampleLoading.IsActive = true;
                ShortsView.SampleTitle.Text = "";
                ShortsView.SampleArtist.Text = "";
                await LoadSamplesFeedAsync(locale);
                if (!_shortsIsOpen) return;
            }

            InsertPendingSample();

            if (_samples.Count == 0)
            {
                ShortsView.SampleLoading.IsActive = false;
                ShowSampleMessage(SamplesVi ? "Chưa có đoạn nhạc nào.\nHãy kiểm tra kết nối rồi thử lại." : "No samples available right now.\nCheck your connection and try again.");
                return;
            }
            ShowSample(Math.Min(_sampleIndex, _samples.Count - 1));
        }

        /// <summary>App suspending: drop the video (decoder + network buffers) but keep the tab and feed as they are.</summary>
        private bool _samplesPausedBeforeSuspend;

        private void ReleaseSampleVideoForSuspend()
        {
            _samplesPausedBeforeSuspend = _samplesUserPaused;
            _samplesGeneration++;
            StopShortsLoop();
            StopSampleVideo();
            ShortsView.SampleLoading.IsActive = false;
        }

        private void ResumeSampleVideoAfterSuspend()
        {
            if (_samples.Count == 0 || _currentTab != SamplesTab) return;
            ShowSample(Math.Min(_sampleIndex, _samples.Count - 1));
            if (_samplesPausedBeforeSuspend)
            {
                // ShowSample reset it; BeginSamplePlayback then only seeks and leaves the clip paused
                _samplesUserPaused = true;
                ShortsView.SamplePausedIcon.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Called by SwitchTab when leaving the Samples tab (and by the back key).</summary>
        private void CloseShortsView(bool keepMainPaused = false)
        {
            if (!_shortsIsOpen) return;
            _shortsIsOpen = false;
            _samplesGeneration++;
            StopShortsLoop();
            StopSampleVideo();

            ShortsView.Visibility = Visibility.Collapsed;
            ShortsView.SamplePoster.ImageSource = null;
            ShortsView.SampleCover.ImageSource = null;

            if (_samplesWasMainPlaying && !keepMainPaused)
            {
                if (_playerDisconnected)
                {
                    // The OS closed the paused audio task while the video played: start the song again where it was
                    if (_samplesSavedTrack != null) PlayTrack(_samplesSavedTrack, null, _samplesSavedPosition.TotalSeconds);
                }
                else
                {
                    try { _appMediaPlayer.Play(); } catch { MarkPlayerDisconnected(); }
                }
            }
            _samplesWasMainPlaying = false;
            _samplesSavedTrack = null;
        }

        private void InsertPendingSample()
        {
            var track = _pendingSampleTrack;
            _pendingSampleTrack = null;
            if (track == null || string.IsNullOrEmpty(track.VideoId)) return;

            int existing = _samples.FindIndex(t => t.VideoId == track.VideoId);
            if (existing >= 0) _samples.RemoveAt(existing);
            int at = Math.Min(_sampleIndex, _samples.Count);
            _samples.Insert(at, track);
            _sampleIndex = at;
        }

        // ==========================================
        // FEED (Home videos + region video charts, localized by hl/gl)
        // ==========================================
        private async Task LoadSamplesFeedAsync(string locale)
        {
            var ids = new HashSet<string>();
            var fromHome = new List<YouTubeTrack>();
            var fromCharts = new List<YouTubeTrack>();
            _samplesMoreSources.Clear();

            // 1. Music videos on the user's Home (already loaded for the same language/region/account)
            if (_homeDynamicSections != null)
                AddSampleVideos(_homeDynamicSections.SelectMany(s => s.Tracks ?? Enumerable.Empty<YouTubeTrack>()), fromHome, ids, true);

            // 2. The region's video chart playlists
            try
            {
                var playlistIds = await InnerTubeClient.GetVideoChartPlaylistIdsAsync(2);
                foreach (var playlistId in playlistIds)
                {
                    var page = await InnerTubeClient.BrowsePlaylistAsync(playlistId);
                    if (page == null) continue;
                    AddSampleVideos(page.Tracks, fromCharts, ids, false);
                    if (!string.IsNullOrEmpty(page.ContinuationToken))
                        _samplesMoreSources.Add(new KeyValuePair<string, string>(playlistId, page.ContinuationToken));
                }
            }
            catch { }

            // Personal videos first, then charts; unseen before already-seen, each group shuffled
            _samples = OrderForSamples(fromHome).Concat(OrderForSamples(fromCharts)).ToList();
            _sampleIndex = 0;
            _samplesLocale = locale;
            _samplesLoadedAt = DateTime.Now;
        }

        private List<YouTubeTrack> OrderForSamples(List<YouTubeTrack> tracks)
        {
            var unseen = tracks.Where(t => !_samplesSeen.Contains(t.VideoId)).ToList();
            var seen = tracks.Where(t => _samplesSeen.Contains(t.VideoId)).ToList();
            Shuffle(unseen);
            Shuffle(seen);
            return unseen.Concat(seen).ToList();
        }

        /// <summary>Appends the next page of a chart playlist when the user nears the end of the feed.</summary>
        private async void LoadMoreSamplesAsync()
        {
            if (_samplesLoadingMore || _samplesMoreSources.Count == 0) return;
            _samplesLoadingMore = true;
            var source = default(KeyValuePair<string, string>);
            try
            {
                source = _samplesMoreSources[0];
                _samplesMoreSources.RemoveAt(0);
                var page = await InnerTubeClient.BrowsePlaylistAsync(source.Key, source.Value);
                if (page != null)
                {
                    var more = new List<YouTubeTrack>();
                    AddSampleVideos(page.Tracks, more, new HashSet<string>(_samples.Select(t => t.VideoId)), false);
                    Shuffle(more);
                    _samples.AddRange(more);
                    if (!string.IsNullOrEmpty(page.ContinuationToken))
                        _samplesMoreSources.Add(new KeyValuePair<string, string>(source.Key, page.ContinuationToken));
                }
            }
            catch
            {
                // Network error: keep the page for the next attempt (the next swipe near the end)
                if (source.Key != null) _samplesMoreSources.Add(source);
            }
            finally { _samplesLoadingMore = false; }
        }

        private static void AddSampleVideos(IEnumerable<YouTubeTrack> tracks, List<YouTubeTrack> feed, HashSet<string> ids, bool requireVideoThumbnail)
        {
            if (tracks == null) return;
            foreach (var t in tracks)
            {
                if (IsSampleCandidate(t, requireVideoThumbnail) && ids.Add(t.VideoId)) feed.Add(t);
            }
        }

        /// <summary>
        /// A playable video id (not a playlist/channel/offline file). Home mixes audio-only "songs" (square
        /// googleusercontent artwork) with music videos, so for Home a YouTube video thumbnail (i.ytimg.com)
        /// is required; chart playlists contain only videos.
        /// </summary>
        private static bool IsSampleCandidate(YouTubeTrack t, bool requireVideoThumbnail)
        {
            if (t == null || string.IsNullOrEmpty(t.VideoId) || t.VideoId.Contains(":")) return false;
            if (!requireVideoThumbnail) return true;
            string thumb = t.ThumbnailUrl ?? "";
            return thumb.IndexOf("ytimg.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _samplesRandom.Next(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        // ==========================================
        // SHOW / PLAY ONE SAMPLE
        // ==========================================
        private YouTubeTrack CurrentSample
        {
            get { return (_sampleIndex >= 0 && _sampleIndex < _samples.Count) ? _samples[_sampleIndex] : null; }
        }

        private async void ShowSample(int index)
        {
            if (index < 0 || index >= _samples.Count) return;
            _sampleIndex = index;
            int gen = ++_samplesGeneration;
            var track = _samples[index];
            _samplesSeen.Add(track.VideoId);
            _samplesUserPaused = false;

            StopShortsLoop();
            StopSampleVideo();
            ShowSampleMessage(null);
            ShortsView.SamplePausedIcon.Visibility = Visibility.Collapsed;
            ShortsView.SampleProgress.Value = 0;
            ShortsView.SampleLoading.IsActive = true;

            ShortsView.SampleTitle.Text = track.Title ?? "";
            ShortsView.SampleArtist.Text = track.ChannelName ?? "";
            UpdateSampleLikeState(track);

            // Poster = HQ video frame, cover = small thumbnail; decoded small to keep 512MB devices happy
            ShortsView.SamplePoster.ImageSource = CreateSampleBitmap("https://i.ytimg.com/vi/" + track.VideoId + "/hqdefault.jpg", 480);
            ShortsView.SampleCover.ImageSource = CreateSampleBitmap(track.ThumbnailUrl, 104);

            if (index >= _samples.Count - 3) LoadMoreSamplesAsync();

            string url;
            if (_sampleUrlCache.TryGetValue(track.VideoId, out url) && !IsStreamUrlFresh(url))
            {
                // Prefetched long ago (the tab kept in the background): googlevideo refuses it after "expire"
                _sampleUrlCache.Remove(track.VideoId);
                _clipStartTasks.Remove(track.VideoId);
                url = null;
            }
            if (url == null)
            {
                try { url = await InnerTubeClient.ResolveStreamUrlAsync(track.VideoId, false, true); }
                catch { url = null; }
            }
            if (gen != _samplesGeneration || !_shortsIsOpen) return;

            if (string.IsNullOrEmpty(url))
            {
                ShortsView.SampleLoading.IsActive = false;
                ShowSampleMessage(SampleUnplayableText);
                return;
            }

            _currentClipStartTask = GetClipStartTask(track.VideoId, url);
            if (IsUndecodableClip(_currentClipStartTask))
            {
                // Already known from the prefetch: do not even open it
                SkipUndecodableSample(track);
                return;
            }
            try
            {
                ShortsView.SampleVideo.Source = new Uri(url);
            }
            catch
            {
                ShortsView.SampleLoading.IsActive = false;
                ShowSampleMessage(SampleUnplayableText);
            }
        }

        /// <summary>googlevideo URLs carry expire=&lt;unix seconds&gt;; past it (minus a margin) MediaElement fails to open them.</summary>
        private static bool IsStreamUrlFresh(string url)
        {
            var m = System.Text.RegularExpressions.Regex.Match(url ?? "", @"[?&]expire=(\d+)");
            long expire;
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out expire)) return true;
            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return expire > now + 120;
        }

        private string _sampleRetriedVideoId; // clip already given its one fresh-URL retry

        private static BitmapImage CreateSampleBitmap(string url, int decodeWidth)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                var bmp = new BitmapImage { DecodePixelWidth = decodeWidth };
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                return bmp;
            }
            catch { return null; }
        }

        private void StopSampleVideo()
        {
            _sampleWaitingSeek = false;
            _sampleRevealPending = false;
            if (_sampleSeekFallback != null) _sampleSeekFallback.Stop();
            try
            {
                ShortsView.SampleVideo.Stop();
                ShortsView.SampleVideo.Source = null;
                ShowSamplePoster();
            }
            catch { }
        }

        private void ShowSampleMessage(string message)
        {
            ShortsView.SampleMessage.Text = message ?? "";
            ShortsView.SampleMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ==========================================
        // MEDIAELEMENT EVENTS
        // ==========================================
        private void HookSampleVideoEvents()
        {
            if (_samplesEventsHooked) return;
            _samplesEventsHooked = true;
            ShortsView.SampleVideo.MediaOpened += SampleVideo_MediaOpened;
            ShortsView.SampleVideo.MediaFailed += SampleVideo_MediaFailed;
            ShortsView.SampleVideo.MediaEnded += SampleVideo_MediaEnded;
            ShortsView.SampleVideo.SeekCompleted += SampleVideo_SeekCompleted;
        }

        private async void SampleVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (!_shortsIsOpen) { StopSampleVideo(); return; }
            int gen = _samplesGeneration;
            try
            {
                // A full song started from "Play" may be running in the main player: never play both
                _sampleRetriedVideoId = null; // opened fine: a later failure may retry again
                try
                {
                    if (_appMediaPlayer != null && _appMediaPlayer.CurrentState == MediaPlayerState.Playing)
                    {
                        // e.g. the song started with "Play" and the user swiped on: pause it, and give it back on exit
                        if (!_samplesWasMainPlaying)
                        {
                            _samplesWasMainPlaying = true;
                            _samplesSavedTrack = currentTrack;
                            _samplesSavedPosition = _appMediaPlayer.Position;
                        }
                        _appMediaPlayer.Pause();
                    }
                }
                catch { }

                // Start about a third into the video (past the intro) like YouTube Music Samples, but exactly on a
                // keyframe read from the MP4 index; without one (moov at the end, network error) start at 0.
                double start = 0;
                var startTask = _currentClipStartTask;
                if (startTask != null)
                {
                    var done = await Task.WhenAny(startTask, Task.Delay(2500));
                    if (gen != _samplesGeneration || !_shortsIsOpen) return;
                    if (done == startTask && IsUndecodableClip(startTask))
                    {
                        SkipUndecodableSample(CurrentSample);
                        return;
                    }
                    if (done == startTask && startTask.Result.HasValue) start = startTask.Result.Value;
                }
                // Seek slightly BEFORE the keyframe: if the player's timeline differs from the MP4 index by a few
                // frames we then land in the previous GOP's tail (hidden by the poster) instead of just past the
                // keyframe (broken until the next one). The poster is removed once playback is past the keyframe.
                _sampleClipStart = TimeSpan.FromSeconds(Math.Max(0, start - SampleSeekLeadSeconds));
                _sampleRevealAtSeconds = start + SampleRevealLagSeconds;

                _sampleRevealPending = true;
                SeekSampleThenPlay(_sampleClipStart);
                StartShortsLoop();
                PrefetchSampleUrl(_sampleIndex + 1);
            }
            catch { }
        }

        // ==========================================
        // SEEK THEN PLAY
        // Playing right after setting Position makes the decoder start mid-GOP (grey macroblocks until the next
        // keyframe). So: pause, seek, wait for SeekCompleted (1.5s fallback), then play; on first play the video
        // is revealed a little later so the poster covers the first frames.
        // ==========================================
        private const double SampleSeekLeadSeconds = 0.3;  // seek this much before the keyframe
        private const double SampleRevealLagSeconds = 0.2;  // remove the poster this much after it
        private bool _sampleWaitingSeek = false;
        private bool _sampleRevealPending = false;
        private double _sampleRevealAtSeconds = 0;
        private DateTime _sampleRevealDeadline = DateTime.MaxValue;
        private DispatcherTimer _sampleSeekFallback;

        private void SeekSampleThenPlay(TimeSpan position)
        {
            var video = ShortsView.SampleVideo;
            try
            {
                if (position <= TimeSpan.FromMilliseconds(200) && video.Position <= TimeSpan.FromMilliseconds(200))
                {
                    BeginSamplePlayback();
                    return;
                }
                video.Pause();
                _sampleWaitingSeek = true;
                video.Position = position;

                if (_sampleSeekFallback == null)
                {
                    _sampleSeekFallback = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                    _sampleSeekFallback.Tick += (s, e) => { _sampleSeekFallback.Stop(); if (_sampleWaitingSeek) BeginSamplePlayback(); };
                }
                _sampleSeekFallback.Stop();
                _sampleSeekFallback.Start();
            }
            catch { BeginSamplePlayback(); }
        }

        private void SampleVideo_SeekCompleted(object sender, RoutedEventArgs e)
        {
            if (_sampleWaitingSeek) BeginSamplePlayback();
        }

        private void BeginSamplePlayback()
        {
            _sampleWaitingSeek = false;
            if (_sampleSeekFallback != null) _sampleSeekFallback.Stop();
            if (!_shortsIsOpen || ShortsView.SampleVideo.Source == null) return;
            try
            {
                if (!_samplesUserPaused) ShortsView.SampleVideo.Play();
                ShortsView.SampleLoading.IsActive = false;
                if (_sampleRevealPending) _sampleRevealDeadline = DateTime.Now.AddSeconds(3); // reveal even if position stalls
            }
            catch { }
        }

        // ==========================================
        // KEYFRAME-ALIGNED CLIP START
        // Reads the MP4 'moov' index with HTTP range requests (itag 18 files keep it at the front) and picks the
        // keyframe nearest before 1/3 of the video. Cached per video id; null = unknown, start at 0.
        // ==========================================
        private const int SampleMp4HeadBytes = 256 * 1024;
        private const int SampleMaxMoovBytes = 4 * 1024 * 1024;
        private readonly Dictionary<string, Task<double?>> _clipStartTasks = new Dictionary<string, Task<double?>>();
        private Task<double?> _currentClipStartTask;

        private Task<double?> GetClipStartTask(string videoId, string url)
        {
            Task<double?> task;
            if (_clipStartTasks.TryGetValue(videoId, out task)) return task;
            if (_clipStartTasks.Count >= 12) _clipStartTasks.Clear();
            task = FindClipStartAsync(url);
            _clipStartTasks[videoId] = task;
            return task;
        }

        private static async Task<double?> FindClipStartAsync(string url)
        {
            try
            {
                byte[] head = await DownloadRangeAsync(url, 0, SampleMp4HeadBytes - 1).ConfigureAwait(false);
                long moovOffset, moovSize;
                if (head == null || !Services.Mp4KeyframeParser.TryFindMoov(head, head.Length, out moovOffset, out moovSize))
                    return null;

                byte[] moov;
                if (moovOffset + moovSize <= head.Length)
                {
                    moov = new byte[moovSize];
                    Array.Copy(head, (int)moovOffset, moov, 0, (int)moovSize);
                }
                else
                {
                    if (moovSize > SampleMaxMoovBytes) return null;
                    moov = await DownloadRangeAsync(url, moovOffset, moovOffset + moovSize - 1).ConfigureAwait(false);
                    if (moov == null || moov.Length < moovSize) return null;
                }

                // A video track this phone cannot decode plays as sound over a black picture: flag it (NaN)
                if (Services.Mp4KeyframeParser.ReadAvcLevel(moov) > Services.Mp4KeyframeParser.MaxDecodableAvcLevel)
                    return double.NaN;

                var info = Services.Mp4KeyframeParser.ParseMoov(moov);
                if (info == null || info.DurationSeconds <= SampleClipSeconds * 2) return 0;
                double target = Math.Min(info.DurationSeconds * 0.33, info.DurationSeconds - SampleClipSeconds - 1);
                return Services.Mp4KeyframeParser.ChooseStart(info.KeyframeSeconds, target);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Samples] keyframe index failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>The clip's MP4 index says its video track exceeds what WP8.1 decodes (FindClipStartAsync returned NaN).</summary>
        private static bool IsUndecodableClip(Task<double?> clipTask)
        {
            return clipTask != null && clipTask.Status == TaskStatus.RanToCompletion
                   && clipTask.Result.HasValue && double.IsNaN(clipTask.Result.Value);
        }

        /// <summary>
        /// Drops a clip the phone cannot decode (black picture with sound, see Mp4KeyframeParser.MaxDecodableAvcLevel)
        /// from the feed and shows the next one in its place.
        /// </summary>
        private void SkipUndecodableSample(YouTubeTrack track)
        {
            System.Diagnostics.Debug.WriteLine("[Samples] skipping " + (track != null ? track.VideoId : "?") + ": H.264 level above what WP8.1 decodes");
            StopSampleVideo();
            int i = track != null ? _samples.IndexOf(track) : -1;
            if (i >= 0) _samples.RemoveAt(i);
            if (_samples.Count == 0)
            {
                ShortsView.SampleLoading.IsActive = false;
                ShowSampleMessage(SampleUnplayableText);
                return;
            }
            ShowSample(Math.Min(_sampleIndex, _samples.Count - 1));
        }

        private static async Task<byte[]> DownloadRangeAsync(string url, long from, long to)
        {
            using (var req = new Windows.Web.Http.HttpRequestMessage(Windows.Web.Http.HttpMethod.Get, new Uri(url)))
            {
                req.Headers.TryAppendWithoutValidation("Range", "bytes=" + from + "-" + to);
                using (var resp = await InnerTubeClient.GetWinrtClient().SendRequestAsync(req).AsTask().ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var buffer = await resp.Content.ReadAsBufferAsync().AsTask().ConfigureAwait(false);
                    return System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
                }
            }
        }

        // ==========================================
        // URL PREFETCH (resolving a stream takes 1-2s; do it for the next clip while this one plays)
        // ==========================================
        private readonly Dictionary<string, string> _sampleUrlCache = new Dictionary<string, string>();
        private readonly HashSet<string> _sampleUrlPending = new HashSet<string>();

        private async void PrefetchSampleUrl(int index)
        {
            if (index < 0 || index >= _samples.Count) return;
            string videoId = _samples[index].VideoId;
            if (_sampleUrlCache.ContainsKey(videoId) || !_sampleUrlPending.Add(videoId)) return;
            try
            {
                string url = await InnerTubeClient.ResolveStreamUrlAsync(videoId, false, true);
                if (!string.IsNullOrEmpty(url))
                {
                    if (_sampleUrlCache.Count >= 8) _sampleUrlCache.Clear();
                    _sampleUrlCache[videoId] = url;
                    var ignored = GetClipStartTask(videoId, url); // warm the keyframe index too
                }
            }
            catch { }
            finally { _sampleUrlPending.Remove(videoId); }
        }

        private void SampleVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (!_shortsIsOpen) return;
            System.Diagnostics.Debug.WriteLine("[Samples] MediaFailed: " + e.ErrorMessage);

            // Usually a stale or IP-bound stream URL: resolve a fresh one once before giving up on the clip
            var track = CurrentSample;
            if (track != null && _sampleRetriedVideoId != track.VideoId)
            {
                _sampleRetriedVideoId = track.VideoId;
                _sampleUrlCache.Remove(track.VideoId);
                _clipStartTasks.Remove(track.VideoId);
                ShowSample(_sampleIndex);
                return;
            }

            ShortsView.SampleLoading.IsActive = false;
            ShortsView.SamplePosterLayer.Opacity = 1;
            ShowSampleMessage(SampleUnplayableText);
        }

        private void SampleVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            RestartSampleClip();
        }

        private Storyboard _samplePosterFade;

        private void ShowSamplePoster()
        {
            if (_samplePosterFade != null) { _samplePosterFade.Stop(); _samplePosterFade = null; }
            ShortsView.SamplePosterLayer.Opacity = 1;
        }

        private void FadeInSampleVideo()
        {
            var anim = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, ShortsView.SamplePosterLayer);
            Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            // Keep the final value as a local value (not held by the storyboard) so ShowSamplePoster can set it again
            sb.Completed += (s, e) => { sb.Stop(); ShortsView.SamplePosterLayer.Opacity = 0; if (_samplePosterFade == sb) _samplePosterFade = null; };
            _samplePosterFade = sb;
            sb.Begin();
        }

        private void RestartSampleClip()
        {
            if (!_shortsIsOpen || _sampleWaitingSeek) return;
            // Looping jumps back before the keyframe too: cover it again until playback is past it
            ShowSamplePoster();
            _sampleRevealPending = true;
            SeekSampleThenPlay(_sampleClipStart);
        }

        // ==========================================
        // CLIP LOOP + PROGRESS
        // ==========================================
        private void StartShortsLoop()
        {
            StopShortsLoop();
            _shortsLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _shortsLoopTimer.Tick += ShortsLoop_Tick;
            _shortsLoopTimer.Start();
        }

        private void StopShortsLoop()
        {
            if (_shortsLoopTimer != null)
            {
                _shortsLoopTimer.Stop();
                _shortsLoopTimer.Tick -= ShortsLoop_Tick;
                _shortsLoopTimer = null;
            }
        }

        private void ShortsLoop_Tick(object sender, object e)
        {
            try
            {
                if (_sampleRevealPending && !_sampleWaitingSeek &&
                    (ShortsView.SampleVideo.Position.TotalSeconds >= _sampleRevealAtSeconds || DateTime.Now >= _sampleRevealDeadline))
                {
                    _sampleRevealPending = false;
                    _sampleRevealDeadline = DateTime.MaxValue;
                    FadeInSampleVideo();
                }

                double elapsed = (ShortsView.SampleVideo.Position - _sampleClipStart).TotalSeconds;
                ShortsView.SampleProgress.Value = Math.Max(0, Math.Min(1, elapsed / SampleClipSeconds));
                if (elapsed >= SampleClipSeconds && !_sampleWaitingSeek) RestartSampleClip();
            }
            catch { }
        }

        // ==========================================
        // INPUT
        // ==========================================
        /// <summary>
        /// The main player started playing while a clip plays (lock screen / headset / Now Playing): the song the user
        /// asked for wins, the clip pauses, and leaving Samples must not touch the song any more.
        /// </summary>
        private void PauseSamplesForMainPlayer()
        {
            if (!_shortsIsOpen || ShortsView.SampleVideo.Source == null) return;
            if (ShortsView.SampleVideo.CurrentState != Windows.UI.Xaml.Media.MediaElementState.Playing && !_sampleWaitingSeek) return;
            try { ShortsView.SampleVideo.Pause(); } catch { }
            _samplesUserPaused = true;
            ShortsView.SamplePausedIcon.Visibility = Visibility.Visible;
            _samplesWasMainPlaying = false;
            _samplesSavedTrack = null;
        }

        private void SamplesStage_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (CurrentSample == null) return;
            if (ShortsView.SampleVideo.Source == null)
            {
                // Stopped by "Play" (or failed): tap reloads the clip
                ShowSample(_sampleIndex);
                return;
            }
            try
            {
                if (ShortsView.SampleVideo.CurrentState == Windows.UI.Xaml.Media.MediaElementState.Playing)
                {
                    ShortsView.SampleVideo.Pause();
                    _samplesUserPaused = true;
                    ShortsView.SamplePausedIcon.Visibility = Visibility.Visible;
                }
                else
                {
                    ShortsView.SampleVideo.Play();
                    _samplesUserPaused = false;
                    ShortsView.SamplePausedIcon.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void SamplesSwipe_Delta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            ShortsView.SampleStageTransform.Y += e.Delta.Translation.Y;
        }

        private bool _samplesChanging; // a clip change animation is running

        private void SamplesSwipe_Completed(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (_samplesChanging) return;
            double dy = e.Cumulative.Translation.Y;
            double vy = e.Velocities.Linear.Y;
            bool next = dy < -90 || vy < -0.8;
            bool prev = dy > 90 || vy > 0.8;

            if (next && _sampleIndex < _samples.Count - 1) AnimateSampleChange(_sampleIndex + 1, -1);
            else if (prev && _sampleIndex > 0) AnimateSampleChange(_sampleIndex - 1, 1);
            else AnimateStageTo(0, null);
        }

        /// <summary>Slides the current clip off screen in <paramref name="direction"/> (-1 up, 1 down), then shows the new one.</summary>
        private void AnimateSampleChange(int newIndex, int direction)
        {
            double height = ShortsView.ActualHeight > 0 ? ShortsView.ActualHeight : 800;
            _samplesChanging = true;
            AnimateStageTo(direction * height, () =>
            {
                ShortsView.SampleStageTransform.Y = -direction * height * 0.25;
                ShowSample(newIndex);
                AnimateStageTo(0, () => _samplesChanging = false);
            });
        }

        private void AnimateStageTo(double y, Action completed)
        {
            var anim = new DoubleAnimation
            {
                To = y,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(anim, ShortsView.SampleStageTransform);
            Storyboard.SetTargetProperty(anim, "Y");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Completed += (s, a) =>
            {
                sb.Stop();
                ShortsView.SampleStageTransform.Y = y;
                if (completed != null) completed();
            };
            sb.Begin();
        }

        // ==========================================
        // ACTIONS
        // ==========================================
        private static bool SamplesVi
        {
            get { return InnerTubeClient.CurrentLanguage == "vi"; }
        }

        private static string SampleUnplayableText
        {
            get { return SamplesVi ? "Không phát được video này.\nVuốt lên để xem đoạn tiếp theo." : "This video can't be played.\nSwipe up for the next one."; }
        }

        /// <summary>Action labels follow the app language (called from UpdateLocalizedUI).</summary>
        private void UpdateSamplesLocalizedText()
        {
            ShortsView.SampleSaveText.Text = SamplesVi ? "Lưu" : "Save";
            ShortsView.SampleShareText.Text = SamplesVi ? "Chia sẻ" : "Share";
            ShortsView.SamplePlayText.Text = SamplesVi ? "Phát" : "Play";
            UpdateSampleLikeState(CurrentSample);
        }

        private static readonly SolidColorBrush _samplesLikedBrush = new SolidColorBrush(Color.FromArgb(255, 29, 185, 84));

        private void UpdateSampleLikeState(YouTubeTrack track)
        {
            bool liked = track != null && favoriteTracks.Any(t => t.VideoId == track.VideoId);
            ShortsView.SampleLikeIcon.Fill = liked ? _samplesLikedBrush : _whiteBrush;
            ShortsView.SampleLikeText.Text = liked ? (SamplesVi ? "Đã thích" : "Liked") : (SamplesVi ? "Thích" : "Like");
        }

        private async void SamplesLike_Click(object sender, RoutedEventArgs e)
        {
            var track = CurrentSample;
            if (track == null) return;

            string token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token) && !InnerTubeClient.HasCookieAuth)
            {
                ShowToast("Sign in to like songs");
                return;
            }

            var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
            bool isAdding = existing == null;
            if (existing != null)
            {
                favoriteTracks.Remove(existing);
                ShowToast("Removed from Favorites");
                var _ = YTMusicWP.Services.DatabaseHelper.RemoveFavoriteAsync(track.VideoId);
            }
            else
            {
                favoriteTracks.Insert(0, track);
                ShowToast("Added to Favorites");
                var _ = YTMusicWP.Services.DatabaseHelper.AddFavoriteAsync(track);
            }
            UpdateSampleLikeState(track);
            if (isAdding) Services.MotionHelper.Pop(ShortsView.SampleLikeIcon);

            await RateVideoAsync(track.VideoId, isAdding ? "like" : "none");
        }

        private void SamplesSave_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentSample == null) return;
            _bottomSheetTrack = CurrentSample;
            BottomSheetAddToPlaylist_Click(null, null);
        }

        private void SamplesShare_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentSample == null) return;
            _bottomSheetTrack = CurrentSample;
            BottomSheetShare_Click(null, null);
        }

        private void SamplesMore_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentSample == null) return;
            _bottomSheetTrack = CurrentSample;
            CustomBottomSheet.Show(CurrentSample);
        }

        /// <summary>Plays the full song in the main player (the previous main track is not resumed).</summary>
        private void SamplesPlay_Click(object sender, RoutedEventArgs e)
        {
            var track = CurrentSample;
            if (track == null) return;
            StopShortsLoop();
            StopSampleVideo();
            _samplesUserPaused = true;
            ShortsView.SamplePausedIcon.Visibility = Visibility.Visible;
            _samplesWasMainPlaying = false;
            PlayTrack(track);
        }

        private void SamplesSongBar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SamplesPlay_Click(sender, null);
        }
    }
}
