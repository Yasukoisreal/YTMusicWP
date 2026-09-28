using System;
using System.Threading;
using Windows.Media.Playback;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using YTMusicWP.Services;

namespace YTMusicWP
{
    // ==========================================
    // ANIMATED ARTWORK (Apple Music motion artwork, see AnimatedArtworkService)
    // A muted, looping MediaElement laid over the cover of the player page: over BigCoverRectangle in the default
    // style, over the full-bleed AppleMusicArtwork in the Apple Music style. It stays transparent until the video
    // has really started, so the still cover underneath doubles as its poster. It only exists while the player
    // page is on screen and is released otherwise (~15-20 MB while playing).
    // ==========================================
    public sealed partial class MainPage
    {
        private MediaElement _artworkVideo;
        private string _artworkVideoId;
        private CancellationTokenSource _artworkCts;
        private DispatcherTimer _artworkRevealTimer;

        /// <summary>
        /// Brings the animated artwork in line with the current state: call on track change, when Now Playing opens,
        /// when the pivot changes page, after resume and when the setting changes. Stops it when it is not wanted.
        /// </summary>
        private async void UpdateAnimatedArtwork()
        {
            var track = currentTrack;
            if (!IsAnimatedArtworkWanted(track))
            {
                StopAnimatedArtwork();
                return;
            }
            if (_artworkVideo != null && _artworkVideoId == track.VideoId)
            {
                // Same song still loaded: only the host may have changed (style switch)
                HostArtworkVideo();
                return;
            }
            // A track change is reported more than once (PlayTrack, then the background player): keep the running lookup
            if (_artworkCts != null && _artworkPendingId == track.VideoId) return;

            StopAnimatedArtwork();
            var cts = new CancellationTokenSource();
            _artworkCts = cts;
            _artworkPendingId = track.VideoId;
            string url;
            try
            {
                url = await AnimatedArtworkService.GetVideoUrlAsync(track, cts.Token);
            }
            catch
            {
                url = null;
            }
            if (_artworkCts == cts)
            {
                _artworkCts = null;
                _artworkPendingId = null;
                _artworkLookedUpId = track.VideoId;
            }
            if (cts.IsCancellationRequested || url == null || currentTrack != track || !IsAnimatedArtworkWanted(track)) return;
            ShowArtworkVideo(track.VideoId, url);
        }

        private string _artworkPendingId;   // lookup in flight for this song
        private string _artworkLookedUpId;  // last song whose lookup finished (with or without artwork)

        private bool IsAnimatedArtworkWanted(YouTubeTrack track)
        {
            if (track == null || !AnimatedArtworkService.IsEnabled) return false;
            if (NowPlayingView == null || NowPlayingView.Visibility != Visibility.Visible || _isClosingNowPlaying) return false;
            if (NowPlayingPivot != null && NowPlayingPivot.SelectedIndex != 0) return false;
            // Default style: only over a square cover (a music video's 16:9 thumbnail is not an album cover)
            if (!_isAppleMusicStyle && BigCoverRectangle != null && Math.Abs(BigCoverRectangle.Width - BigCoverRectangle.Height) > 4) return false;
            return true;
        }

        private void ShowArtworkVideo(string videoId, string url)
        {
            var video = new MediaElement
            {
                IsMuted = true,
                IsLooping = true,
                AutoPlay = true,
                AudioCategory = AudioCategory.Other,
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false,
                Opacity = 0
            };
            video.MediaOpened += ArtworkVideo_MediaOpened;
            video.MediaEnded += ArtworkVideo_MediaEnded;
            video.MediaFailed += ArtworkVideo_MediaFailed;
            _artworkVideo = video;
            _artworkVideoId = videoId;
            HostArtworkVideo();
            video.Source = new Uri(url);
        }

        /// <summary>Puts the video over the cover of the current style, at the cover's size.</summary>
        private void HostArtworkVideo()
        {
            var video = _artworkVideo;
            if (video == null) return;
            Panel host = _isAppleMusicStyle ? (Panel)AppleMusicArtworkGrid : DefaultNowPlayingArtwork;
            if (host == null) return;

            var current = video.Parent as Panel;
            if (current != host)
            {
                if (current != null) current.Children.Remove(video);
                // Directly above the cover image, below everything drawn over it (fades, scrims)
                UIElement cover = _isAppleMusicStyle ? (UIElement)AppleMusicArtwork : BigCoverRectangle;
                int index = host.Children.IndexOf(cover);
                host.Children.Insert(index >= 0 ? index + 1 : host.Children.Count, video);
            }
            SizeArtworkVideo();
        }

        private void SizeArtworkVideo()
        {
            var video = _artworkVideo;
            if (video == null) return;
            if (_isAppleMusicStyle)
            {
                video.Width = double.NaN;
                video.Height = AppleMusicArtwork.Height;
                video.HorizontalAlignment = HorizontalAlignment.Stretch;
                video.VerticalAlignment = VerticalAlignment.Top;
            }
            else
            {
                video.Width = BigCoverRectangle.Width;
                video.Height = BigCoverRectangle.Height;
                video.HorizontalAlignment = BigCoverRectangle.HorizontalAlignment;
                video.VerticalAlignment = BigCoverRectangle.VerticalAlignment;
                video.Margin = BigCoverRectangle.Margin;
            }
        }

        /// <summary>The cover was resized (new artwork loaded, layout pass): keep the video on it, or drop it if the cover is no longer square.</summary>
        private void SyncArtworkVideoToCover()
        {
            var track = currentTrack;
            if (_artworkVideo == null)
            {
                // The new cover may only now have become square (the previous one was a 16:9 video thumbnail)
                if (track != null && _artworkCts == null && track.VideoId != _artworkLookedUpId && IsAnimatedArtworkWanted(track))
                    UpdateAnimatedArtwork();
                return;
            }
            if (!IsAnimatedArtworkWanted(track)) StopAnimatedArtwork();
            else SizeArtworkVideo();
        }

        private void StopAnimatedArtwork()
        {
            if (_artworkCts != null)
            {
                _artworkCts.Cancel();
                _artworkCts = null;
                _artworkPendingId = null;
            }
            if (_artworkRevealTimer != null) _artworkRevealTimer.Stop();
            var video = _artworkVideo;
            _artworkVideo = null;
            _artworkVideoId = null;
            if (video == null) return;
            video.MediaOpened -= ArtworkVideo_MediaOpened;
            video.MediaEnded -= ArtworkVideo_MediaEnded;
            video.MediaFailed -= ArtworkVideo_MediaFailed;
            try { video.Stop(); } catch { }
            video.Source = null;
            var parent = video.Parent as Panel;
            if (parent != null) parent.Children.Remove(video);
        }

        /// <summary>The song was paused or resumed: the artwork follows it.</summary>
        private void SyncAnimatedArtworkPlayback(bool isPlaying)
        {
            var video = _artworkVideo;
            if (video == null || video.Source == null) return;
            try
            {
                if (isPlaying) video.Play(); else video.Pause();
            }
            catch { }
        }

        private void ArtworkVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            var video = sender as MediaElement;
            if (video == null || video != _artworkVideo) return;
            bool songPlaying = false;
            try { songPlaying = _appMediaPlayer != null && _appMediaPlayer.CurrentState == MediaPlayerState.Playing; } catch { }
            if (!songPlaying) video.Pause();

            // Reveal once frames are really flowing (the first decoded frames can be grey), the cover is the poster until then
            if (_artworkRevealTimer == null)
            {
                _artworkRevealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _artworkRevealTimer.Tick += ArtworkRevealTimer_Tick;
            }
            _artworkRevealTimer.Start();
        }

        private void ArtworkRevealTimer_Tick(object sender, object e)
        {
            var video = _artworkVideo;
            if (video == null) { _artworkRevealTimer.Stop(); return; }
            if (video.Position.TotalSeconds >= 0.3)
            {
                video.Opacity = 1;
                _artworkRevealTimer.Stop();
            }
        }

        private void ArtworkVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            // The fragmented MP4 reports no duration, so IsLooping may not wrap on its own
            var video = sender as MediaElement;
            if (video == null || video != _artworkVideo) return;
            video.Position = TimeSpan.Zero;
            video.Play();
        }

        private async void ArtworkVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            var video = sender as MediaElement;
            if (video == null || video != _artworkVideo) return;
            string videoId = _artworkVideoId;
            System.Diagnostics.Debug.WriteLine("[AnimatedArtwork] playback failed: " + e.ErrorMessage);
            StopAnimatedArtwork();
            await AnimatedArtworkService.ForgetAsync(videoId);
        }
    }
}
