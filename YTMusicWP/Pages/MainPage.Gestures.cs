using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using YTMusicWP.Services;

namespace YTMusicWP
{
    /// <summary>
    /// Swipes on the player, as in Apple Music and Spotify:
    /// <list type="bullet">
    /// <item>Mini player: sideways skips; upwards pulls Now Playing up under the finger.</item>
    /// <item>Now Playing (either artwork, header, grabber): downwards pulls it down to close.</item>
    /// <item>Apple Music artwork: follows the finger sideways; past the threshold it flies off, the song changes and
    /// the new cover slides in from the other side. The Spotify-style cover only pulls down: its sideways swipe
    /// belongs to the Pivot (Lyrics, Queue).</item>
    /// <item>Fullscreen lyrics: pulling the title down closes them.</item>
    /// </list>
    /// A drag locks to the axis it starts along, so a sideways skip never moves the page and the other way round.
    /// </summary>
    public sealed partial class MainPage
    {
        private enum SwipeAxis { None, Horizontal, Vertical }

        /// <summary>Movement before a drag commits to an axis.</summary>
        private const double AxisLockDistance = 10;
        /// <summary>A release faster than this (px/ms) counts as a flick, whatever the distance.</summary>
        private const double FlickVelocity = 0.5;

        private readonly MotionGroup _nowPlayingDrag = new MotionGroup();
        private readonly MotionGroup _artworkSwipe = new MotionGroup();
        private DispatcherTimer _coverWait;

        private SwipeAxis _miniAxis;
        private bool _miniPullingUp;
        private SwipeAxis _artworkAxis;
        private bool _nowPlayingPulling;
        private bool _artworkSwipeBusy;

        /// <summary>
        /// Measures every player drag against the page itself. The default container is the element's parent, and
        /// Now Playing moves under the finger while it is dragged: its own coordinates would feed the drag back.
        /// </summary>
        private void PlayerDrag_ManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
        {
            e.Container = this.Content as UIElement;
        }

        private static SwipeAxis LockAxis(SwipeAxis axis, Point moved)
        {
            if (axis != SwipeAxis.None) return axis;
            if (Math.Max(Math.Abs(moved.X), Math.Abs(moved.Y)) < AxisLockDistance) return SwipeAxis.None;
            return Math.Abs(moved.X) >= Math.Abs(moved.Y) ? SwipeAxis.Horizontal : SwipeAxis.Vertical;
        }

        /// <summary>How far Now Playing travels between hidden (below the screen) and shown.</summary>
        private double NowPlayingTravel
        {
            get
            {
                double h = NowPlayingView.ActualHeight;
                if (h <= 0)
                {
                    try { h = Window.Current.Bounds.Height; } catch { h = 800; }
                }
                return h;
            }
        }

        // ── Mini player ────────────────────────────────────────────────────────────────────────────────────

        private void MiniPlayer_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _miniAxis = SwipeAxis.None;
            _miniPullingUp = false;
        }

        private void MiniPlayer_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            var moved = e.Cumulative.Translation;
            _miniAxis = LockAxis(_miniAxis, moved);

            if (_miniAxis == SwipeAxis.Horizontal)
            {
                if (MiniPlayerTranslate != null) MiniPlayerTranslate.X = Math.Max(-80, Math.Min(80, moved.X));
            }
            else if (_miniAxis == SwipeAxis.Vertical)
            {
                if (!_miniPullingUp)
                {
                    // Only upwards, only with a song, and not while Now Playing is already up
                    bool shown = NowPlayingView.Visibility == Visibility.Visible && !_isClosingNowPlaying;
                    if (moved.Y > -AxisLockDistance || currentTrack == null || shown) return;
                    _miniPullingUp = true;
                    OpenNowPlaying(false); // below the screen, ready to follow the finger
                }
                if (!_isClosingNowPlaying) NowPlayingTransform.Y = Math.Max(0, NowPlayingTravel + moved.Y);
            }
        }

        private void MiniPlayer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (_miniAxis == SwipeAxis.Horizontal && MiniPlayerTranslate != null)
            {
                double totalX = e.Cumulative.Translation.X;
                double velX = e.Velocities.Linear.X;
                MiniPlayerTranslate.X = 0;
                if (totalX < -40 || velX < -0.15) NextButton_Click(null, null);
                else if (totalX > 40 || velX > 0.15) PrevButton_Click(null, null);
            }
            else if (_miniPullingUp)
            {
                FinishNowPlayingDrag(e.Velocities.Linear.Y, true);
            }
            _miniAxis = SwipeAxis.None;
            _miniPullingUp = false;
        }

        // ── Now Playing: pull down to close ─────────────────────────────────────────────────────────────────

        private void NowPlayingPull_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _nowPlayingPulling = false;
        }

        private void NowPlayingPull_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            PullNowPlayingDown(e.Cumulative.Translation.Y);
        }

        private void NowPlayingPull_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (_nowPlayingPulling) FinishNowPlayingDrag(e.Velocities.Linear.Y, false);
            _nowPlayingPulling = false;
        }

        private void PullNowPlayingDown(double dy)
        {
            if (!_nowPlayingPulling)
            {
                if (dy < AxisLockDistance || _isClosingNowPlaying || _nowPlayingDrag.IsRunning) return;
                // The slide-in storyboard holds Y at 0 and would ignore the finger: hand the value back to code
                double y = NowPlayingTransform.Y;
                SlideUpStoryboard.Stop();
                NowPlayingTransform.Y = y;
                _nowPlayingPulling = true;
            }
            if (_isClosingNowPlaying) return; // closed meanwhile (Back): the slide-down owns Y
            NowPlayingTransform.Y = Math.Max(0, dy);
        }

        /// <summary>
        /// Settles Now Playing after a drag: open or closed by how far it is up, unless the release was a flick,
        /// which wins either way.
        /// </summary>
        private void FinishNowPlayingDrag(double velocityY, bool pullingUp)
        {
            // Closed during the drag (Back): settling it open would fight the slide-down
            if (_isClosingNowPlaying || NowPlayingView.Visibility != Visibility.Visible) return;
            double travel = NowPlayingTravel;
            double shown = 1 - NowPlayingTransform.Y / travel;
            bool open;
            if (velocityY < -FlickVelocity) open = true;
            else if (velocityY > FlickVelocity) open = false;
            else open = pullingUp ? shown > 0.3 : shown > 0.75;

            if (open)
            {
                int ms = (int)Math.Max(120, Math.Min(350, (1 - shown) * 400));
                _nowPlayingDrag.Animate(ms, EasingMode.EaseOut, null,
                    MotionHelper.To(NowPlayingTransform, "Y", 0),
                    MotionHelper.To(NowPlayingView, "Opacity", 1));
            }
            else
            {
                CloseNowPlaying_Click(null, null); // slides on down from where the finger left it
            }
        }

        // ── Apple Music artwork: sideways changes the song, downwards closes ────────────────────────────────
        // (The Spotify-style cover only closes: it sits in the Pivot, whose sideways swipe leads to Lyrics and Queue.)

        private void Cover_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _artworkAxis = SwipeAxis.None;
            _nowPlayingPulling = false;
            // A cover still flying or settling: this touch waits for the next one
            if (_artworkSwipeBusy || _artworkSwipe.IsRunning) e.Complete();
        }

        private void Cover_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            var moved = e.Cumulative.Translation;
            _artworkAxis = LockAxis(_artworkAxis, moved);
            if (_artworkAxis == SwipeAxis.Horizontal)
            {
                AppleMusicArtworkTranslate.X = moved.X;
                AppleMusicArtworkGrid.Opacity = 1 - Math.Min(1, Math.Abs(moved.X) / ArtworkWidth) * 0.35;
            }
            else if (_artworkAxis == SwipeAxis.Vertical)
            {
                PullNowPlayingDown(moved.Y);
            }
        }

        private void Cover_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (_artworkAxis == SwipeAxis.Horizontal)
                FinishCoverSwipe(e.Cumulative.Translation.X, e.Velocities.Linear.X);
            else if (_nowPlayingPulling)
                FinishNowPlayingDrag(e.Velocities.Linear.Y, false);
            _artworkAxis = SwipeAxis.None;
            _nowPlayingPulling = false;
        }

        private double ArtworkWidth
        {
            get
            {
                double w = AppleMusicArtworkGrid.ActualWidth;
                return w > 0 ? w : 400;
            }
        }

        private void FinishCoverSwipe(double dx, double velocityX)
        {
            double w = ArtworkWidth;
            bool next = dx < -w * 0.25 || (velocityX < -0.4 && dx < -24);
            bool prev = !next && (dx > w * 0.25 || (velocityX > 0.4 && dx > 24));
            if (!next && !prev)
            {
                // Not far enough: back into place
                _artworkSwipe.Animate(260, EasingMode.EaseOut, null,
                    MotionHelper.To(AppleMusicArtworkTranslate, "X", 0),
                    MotionHelper.To(AppleMusicArtworkGrid, "Opacity", 1));
                return;
            }

            _artworkSwipeBusy = true;
            // Previous within the first 3 s goes to the previous song; later it restarts this one and the cover stays
            bool sameSong = prev && CurrentPositionSeconds() > 3;
            object coverBefore = AppleMusicArtwork.Source;
            if (next) NextButton_Click(null, null);
            else PrevButton_Click(null, null);

            double dir = next ? -1 : 1;
            _artworkSwipe.Animate(180, EasingMode.EaseIn, () =>
            {
                // Waits just off the other side for the new cover, then slides in
                AppleMusicArtworkTranslate.X = -dir * w * 0.3;
                AppleMusicArtworkGrid.Opacity = 0;
                WaitForNewCover(coverBefore, sameSong ? 0 : 2500, () =>
                    _artworkSwipe.Animate(320, EasingMode.EaseOut, () => _artworkSwipeBusy = false,
                        MotionHelper.To(AppleMusicArtworkTranslate, "X", 0),
                        MotionHelper.To(AppleMusicArtworkGrid, "Opacity", 1)));
            },
            MotionHelper.To(AppleMusicArtworkTranslate, "X", dir * w),
            MotionHelper.To(AppleMusicArtworkGrid, "Opacity", 0));
        }

        // ── Fullscreen lyrics: pull the title down to close ─────────────────────────────────────────────────

        private bool _lyricsPulling;

        private void FullscreenLyricsPull_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _lyricsPulling = false;
            if (_fullscreenLyricsMotion.IsRunning) e.Complete(); // still opening or closing
        }

        private void FullscreenLyricsPull_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            double dy = e.Cumulative.Translation.Y;
            if (_fullscreenLyricsMotion.IsRunning) return; // closing (Back or a tap) owns the view now
            if (!_lyricsPulling && dy < AxisLockDistance) return;
            _lyricsPulling = true;
            var t = MotionHelper.EnsureTransform(FullscreenLyricsView);
            t.TranslateY = Math.Max(0, dy);
            FullscreenLyricsView.Opacity = 1 - Math.Min(1, Math.Max(0, dy) / NowPlayingTravel) * 0.5;
        }

        private void FullscreenLyricsPull_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (!_lyricsPulling) return;
            _lyricsPulling = false;
            // Closed during the drag: springing back would replace the close (MotionGroup) and leave it open
            if (_fullscreenLyricsMotion.IsRunning || FullscreenLyricsView.Visibility != Visibility.Visible) return;
            var t = MotionHelper.EnsureTransform(FullscreenLyricsView);
            double v = e.Velocities.Linear.Y;
            if (v > FlickVelocity || (v > -FlickVelocity && t.TranslateY > NowPlayingTravel * 0.2))
            {
                CloseFullscreenLyrics_Tapped(null, null); // fades out from where it was pulled to
                return;
            }
            _fullscreenLyricsMotion.Animate(250, EasingMode.EaseOut, null,
                MotionHelper.To(t, "TranslateY", 0),
                MotionHelper.To(FullscreenLyricsView, "Opacity", 1));
        }

        private double CurrentPositionSeconds()
        {
            try { return _appMediaPlayer.Position.TotalSeconds; } catch { return 0; }
        }

        /// <summary>
        /// Calls <paramref name="done"/> once the Apple Music artwork shows a different image (the new song's cover
        /// arrives after the audio task has switched songs and the image has loaded), or after
        /// <paramref name="maxMs"/> so a slow or failed load never leaves the cover missing.
        /// </summary>
        private void WaitForNewCover(object coverBefore, int maxMs, Action done)
        {
            if (_coverWait != null) _coverWait.Stop();
            if (maxMs <= 0 || AppleMusicArtwork.Source != coverBefore) { done(); return; }

            var started = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (s, e) =>
            {
                if (AppleMusicArtwork.Source == coverBefore && (DateTime.UtcNow - started).TotalMilliseconds < maxMs) return;
                timer.Stop();
                if (_coverWait == timer) _coverWait = null;
                done();
            };
            _coverWait = timer;
            timer.Start();
        }
    }
}
