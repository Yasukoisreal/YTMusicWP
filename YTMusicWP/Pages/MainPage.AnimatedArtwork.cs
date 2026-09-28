using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Playback;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;
using YTMusicWP.Services;

namespace YTMusicWP
{
    // ==========================================
    // ANIMATED ARTWORK (Apple Music motion artwork, see AnimatedArtworkService)
    // A muted, looping MediaElement laid over the cover of the player page: over BigCoverRectangle in the default
    // style, over the full-bleed AppleMusicArtwork in the Apple Music style. It stays transparent until the video
    // has really started, so the still cover underneath doubles as its poster. It exists while Now Playing is open
    // (~15-20 MB) and only plays while the player page is on screen and the song is playing.
    // Cost control: decoding starts after the slide-in animation, the rendition matches the cover's pixel size, and
    // flipping to the lyrics / queue page pauses the video instead of tearing it down (which re-downloaded it).
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
                // Same song still loaded: the host may have changed (style switch), or the pivot page
                HostArtworkVideo();
                ApplyArtworkPlayState();
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
                url = await AnimatedArtworkService.GetVideoUrlAsync(track, ArtworkTargetPixels(), cts.Token);
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

        // Song whose animated artwork was dropped because the OS reported high memory use: not restarted for it
        private string _artworkDroppedForMemoryId;

        /// <summary>The OS reports high memory use: the artwork video (~15-20 MB) is the largest thing the page can drop.</summary>
        private void MemoryHelper_HighMemoryPressure(object sender, EventArgs e)
        {
            if (_artworkVideo == null && _artworkCts == null) return;
            _artworkDroppedForMemoryId = currentTrack != null ? currentTrack.VideoId : null;
            StopAnimatedArtwork();
        }

        private bool IsAnimatedArtworkWanted(YouTubeTrack track)
        {
            if (track == null || !AnimatedArtworkService.IsEnabled) return false;
            if (track.VideoId == _artworkDroppedForMemoryId) return false; // the next song may try again
            if (NowPlayingView == null || NowPlayingView.Visibility != Visibility.Visible || _isClosingNowPlaying) return false;
            // Default style: only over a square cover (a music video's 16:9 thumbnail is not an album cover)
            if (!_isAppleMusicStyle && BigCoverRectangle != null && Math.Abs(BigCoverRectangle.Width - BigCoverRectangle.Height) > 4) return false;
            return true;
        }

        /// <summary>Pixel width of the cover on this screen, clamped to 360..486 (the rendition ladder's cheap end).</summary>
        private int ArtworkTargetPixels()
        {
            double scale = 1;
            try { scale = Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel; } catch { }
            double width = Window.Current.Bounds.Width * (_isAppleMusicStyle ? 1.0 : 0.75) * scale;
            return (int)Math.Max(360, Math.Min(486, width));
        }

        private bool IsArtworkOnScreen()
        {
            return NowPlayingPivot == null || NowPlayingPivot.SelectedIndex == 0;
        }

        private bool IsSongPlaying()
        {
            try { return _appMediaPlayer != null && _appMediaPlayer.CurrentState == MediaPlayerState.Playing; }
            catch { return false; }
        }

        /// <summary>Plays the video only while its page is on screen and the song plays; the reveal timer runs only then.</summary>
        private void ApplyArtworkPlayState()
        {
            var video = _artworkVideo;
            if (video == null || video.Source == null) return;
            bool play = IsSongPlaying() && IsArtworkOnScreen();
            try
            {
                if (play) video.Play(); else video.Pause();
            }
            catch { }
            if (_artworkRevealTimer != null)
            {
                if (play && video.Opacity < 1) _artworkRevealTimer.Start();
                else _artworkRevealTimer.Stop();
            }
        }

        private DateTime _nowPlayingShownAt = DateTime.MinValue;
        private const int ArtworkStartDelayMs = 450; // Now Playing slides in over 300 ms

        private async void ShowArtworkVideo(string videoId, string url)
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

            // Opening a video (network + decoder start) during the slide-in animation makes it stutter
            int wait = ArtworkStartDelayMs - (int)(DateTime.UtcNow - _nowPlayingShownAt).TotalMilliseconds;
            if (wait > 0)
            {
                await Task.Delay(wait);
                if (_artworkVideo != video) return; // stopped or replaced meanwhile
            }
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

            // Bottom fade into the backdrop (Apple Music style only), directly above the video
            var fade = EnsureArtworkFade();
            var fadeParent = fade.Parent as Panel;
            if (!_isAppleMusicStyle)
            {
                if (fadeParent != null) fadeParent.Children.Remove(fade);
            }
            else if (fadeParent != host)
            {
                if (fadeParent != null) fadeParent.Children.Remove(fade);
                host.Children.Insert(host.Children.IndexOf(video) + 1, fade);
            }
            fade.Opacity = video.Opacity;

            // Rounded corners (default style only; the Apple Music artwork is full-bleed)
            var corners = EnsureArtworkCorners();
            var cornersParent = corners.Parent as Panel;
            if (_isAppleMusicStyle)
            {
                if (cornersParent != null) cornersParent.Children.Remove(corners);
            }
            else if (cornersParent != host)
            {
                if (cornersParent != null) cornersParent.Children.Remove(corners);
                host.Children.Insert(host.Children.IndexOf(video) + 1, corners);
            }
            SizeArtworkVideo();
        }

        private void SizeArtworkVideo()
        {
            var video = _artworkVideo;
            if (video == null) return;
            if (_isAppleMusicStyle)
            {
                // Square and top-aligned like the still artwork (a slot shorter than the width crops its bottom, which is
                // what the fade rows assume); the grid's clip cuts the overflow
                double width = AppleMusicArtwork.Width;
                if (double.IsNaN(width) || width <= 0) width = AppleMusicArtworkGrid.ActualWidth;
                video.Width = double.NaN;
                video.Height = width > 0 ? width : AppleMusicArtwork.Height;
                video.HorizontalAlignment = HorizontalAlignment.Stretch;
                video.VerticalAlignment = VerticalAlignment.Top;
                // Fade rows depend on the grid's final size: paint after this layout pass
                var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, UpdateArtworkFade);
            }
            else
            {
                video.Width = BigCoverRectangle.Width;
                video.Height = BigCoverRectangle.Height;
                video.HorizontalAlignment = BigCoverRectangle.HorizontalAlignment;
                video.VerticalAlignment = BigCoverRectangle.VerticalAlignment;
                video.Margin = BigCoverRectangle.Margin;
                if (_artworkCorners != null)
                {
                    _artworkCorners.Width = video.Width;
                    _artworkCorners.Height = video.Height;
                    _artworkCorners.HorizontalAlignment = video.HorizontalAlignment;
                    _artworkCorners.VerticalAlignment = video.VerticalAlignment;
                    _artworkCorners.Margin = video.Margin;
                }
                // Colors depend on where the cover ends up: read them after this layout pass
                var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, UpdateArtworkCornerColors);
            }
        }

        // ── Rounded corners ──
        // WP8.1 can only clip to a plain rectangle (RectangleGeometry has no radius, Border.CornerRadius does not clip
        // children), so the video's corners are covered instead: four "square minus quarter circle" paths painted with
        // the Now Playing background color at the cover's top and bottom edges. The gradient barely changes across a
        // 16 px corner, and the cover's shadow is rounded the same way, so the corner areas hold background only.
        private Grid _artworkCorners;
        private readonly SolidColorBrush _artworkCornerTopBrush = new SolidColorBrush(Colors.Transparent);
        private readonly SolidColorBrush _artworkCornerBottomBrush = new SolidColorBrush(Colors.Transparent);

        private Grid EnsureArtworkCorners()
        {
            if (_artworkCorners != null) return _artworkCorners;
            double r = BigCoverRectangle.RadiusX > 0 ? BigCoverRectangle.RadiusX : 16;
            var grid = new Grid { IsHitTestVisible = false };
            grid.Children.Add(CornerPath(r, HorizontalAlignment.Left, VerticalAlignment.Top, 1, 1, _artworkCornerTopBrush));
            grid.Children.Add(CornerPath(r, HorizontalAlignment.Right, VerticalAlignment.Top, -1, 1, _artworkCornerTopBrush));
            grid.Children.Add(CornerPath(r, HorizontalAlignment.Left, VerticalAlignment.Bottom, 1, -1, _artworkCornerBottomBrush));
            grid.Children.Add(CornerPath(r, HorizontalAlignment.Right, VerticalAlignment.Bottom, -1, -1, _artworkCornerBottomBrush));
            return _artworkCorners = grid;
        }

        /// <summary>
        /// Top-left corner mask (mirrored by <paramref name="sx"/>/<paramref name="sy"/> for the others): the area of an
        /// (r+1)-square outside the cover's arc, overhanging the video edge by 1 px so no video pixel peeks through.
        /// </summary>
        private static Path CornerPath(double r, HorizontalAlignment h, VerticalAlignment v, double sx, double sy, Brush fill)
        {
            double s = r + 1;
            var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment { Point = new Point(s, 0) });
            figure.Segments.Add(new LineSegment { Point = new Point(s, 1) });
            figure.Segments.Add(new ArcSegment { Point = new Point(1, s), Size = new Size(r, r), SweepDirection = SweepDirection.Counterclockwise });
            figure.Segments.Add(new LineSegment { Point = new Point(0, s) });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new Path
            {
                Data = geometry,
                Fill = fill,
                Width = s,
                Height = s,
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Margin = new Thickness(h == HorizontalAlignment.Left ? -1 : 0, v == VerticalAlignment.Top ? -1 : 0,
                                       h == HorizontalAlignment.Right ? -1 : 0, v == VerticalAlignment.Bottom ? -1 : 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform { ScaleX = sx, ScaleY = sy }
            };
        }

        /// <summary>Paints the corner masks with the background gradient's color at the cover's top and bottom edge.</summary>
        private void UpdateArtworkCornerColors()
        {
            var video = _artworkVideo;
            var background = DefaultNowPlayingBackground;
            if (_artworkCorners == null || video == null || background == null || _isAppleMusicStyle) return;
            double height = background.ActualHeight;
            if (height <= 0 || video.ActualHeight <= 0) return;
            try
            {
                var toBackground = video.TransformToVisual(background);
                double top = toBackground.TransformPoint(new Point(0, 0)).Y;
                double bottom = toBackground.TransformPoint(new Point(0, video.ActualHeight)).Y;
                _artworkCornerTopBrush.Color = GradientColorAt(NowPlayingGradient, top / height);
                _artworkCornerBottomBrush.Color = GradientColorAt(NowPlayingGradient, bottom / height);
            }
            catch { }
        }

        /// <summary>Color of a vertical LinearGradientBrush at <paramref name="t"/> (0 = top, 1 = bottom), sRGB interpolation like XAML.</summary>
        private static Color GradientColorAt(LinearGradientBrush brush, double t)
        {
            var stops = brush.GradientStops.OrderBy(s => s.Offset).ToList();
            if (stops.Count == 0) return Colors.Black;
            if (t <= stops[0].Offset) return stops[0].Color;
            for (int i = 1; i < stops.Count; i++)
            {
                if (t <= stops[i].Offset)
                {
                    var a = stops[i - 1];
                    var b = stops[i];
                    double f = b.Offset > a.Offset ? (t - a.Offset) / (b.Offset - a.Offset) : 0;
                    return Color.FromArgb(
                        (byte)Math.Round(a.Color.A + (b.Color.A - a.Color.A) * f),
                        (byte)Math.Round(a.Color.R + (b.Color.R - a.Color.R) * f),
                        (byte)Math.Round(a.Color.G + (b.Color.G - a.Color.G) * f),
                        (byte)Math.Round(a.Color.B + (b.Color.B - a.Color.B) * f));
                }
            }
            return stops[stops.Count - 1].Color;
        }

        // ── Apple Music style bottom fade ──
        // The still artwork dissolves into the backdrop through its own alpha (LumiaBlurHelper.RenderFadedArtworkAsync).
        // A video has no alpha and WP8.1 has no OpacityMask, so the fade is painted over the video instead: a small bitmap
        // of what lies behind the artwork (blurred backdrop under the tinted wash) whose opacity rises along the same
        // cosine curve over the same rows, and is solid below them. Video under it composes exactly like the still image.
        private Image _artworkFade;
        private const int ArtworkFadeColumns = 48; // the backdrop is a ~32 px wide blur, stretched back smoothly

        private Image EnsureArtworkFade()
        {
            if (_artworkFade == null)
            {
                _artworkFade = new Image
                {
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    Opacity = 0
                };
            }
            return _artworkFade;
        }

        private void UpdateArtworkFade()
        {
            var fade = _artworkFade;
            var grid = AppleMusicArtworkGrid;
            if (fade == null || fade.Parent != grid || grid == null || !_isAppleMusicStyle) return;
            double w = grid.ActualWidth;
            double h = !double.IsNaN(grid.Height) ? grid.Height : grid.ActualHeight;
            if (w <= 0 || h <= 0) return;
            try
            {
                // Rows of the still artwork's fade, in grid coordinates (square bitmap scaled to the width, top-aligned)
                int size = AppleMusicFadeSize;
                double scale = w / size;
                int fadeEndY = GetArtworkFadeEndY(size);
                double top = Math.Max(0, fadeEndY - AppleMusicFadeHeight) * scale;
                double end = fadeEndY * scale;
                if (top >= h - 1) { fade.Source = null; return; }
                int rows = (int)Math.Ceiling(h - top);
                int cols = ArtworkFadeColumns;

                // Blurred backdrop: UniformToFill, centered, in its own box
                var backdrop = AppleMusicBackdrop;
                var blur = backdrop != null && backdrop.Visibility == Visibility.Visible ? backdrop.Source as WriteableBitmap : null;
                byte[] px = null;
                int pw = 0, ph = 0;
                double bScale = 1, bx0 = 0, by0 = 0;
                Point origin = new Point(), unit = new Point(1, 1);
                if (blur != null && blur.PixelWidth > 0 && blur.PixelHeight > 0 && backdrop.ActualWidth > 0 && backdrop.ActualHeight > 0)
                {
                    px = blur.PixelBuffer.ToArray();
                    pw = blur.PixelWidth;
                    ph = blur.PixelHeight;
                    bScale = Math.Max(backdrop.ActualWidth / pw, backdrop.ActualHeight / ph);
                    bx0 = (backdrop.ActualWidth - pw * bScale) / 2;
                    by0 = (backdrop.ActualHeight - ph * bScale) / 2;
                    var toBackdrop = grid.TransformToVisual(backdrop);
                    origin = toBackdrop.TransformPoint(new Point(0, 0));
                    var one = toBackdrop.TransformPoint(new Point(1, 1));
                    unit = new Point(one.X - origin.X, one.Y - origin.Y);
                }

                // Tinted wash over it
                var wash = AppleMusicWash;
                bool hasWash = wash != null && wash.Visibility == Visibility.Visible && wash.ActualHeight > 0;
                double washTop = 0;
                if (hasWash) washTop = grid.TransformToVisual(wash).TransformPoint(new Point(0, 0)).Y;

                var pixels = new byte[cols * rows * 4];
                double rowHeight = (h - top) / rows;
                double colWidth = w / cols;
                double span = Math.Max(1, end - top);
                for (int j = 0; j < rows; j++)
                {
                    double y = top + (j + 0.5) * rowHeight;
                    // 1 - the still artwork's alpha 0.5 * (1 + cos(pi * progress))
                    double alpha = y >= end ? 1 : 0.5 * (1 - Math.Cos(Math.PI * (y - top) / span));
                    Color washColor = Colors.Black;
                    double washAmount = 0;
                    if (hasWash)
                    {
                        washColor = GradientColorAt(AppleMusicWashGradient, (washTop + y) / wash.ActualHeight);
                        washAmount = wash.Opacity * washColor.A / 255.0;
                    }
                    for (int i = 0; i < cols; i++)
                    {
                        double r = 0, g = 0, b = 0;
                        if (px != null)
                        {
                            double bx = origin.X + unit.X * (i + 0.5) * colWidth;
                            double by = origin.Y + unit.Y * y;
                            SampleBilinear(px, pw, ph, (bx - bx0) / bScale - 0.5, (by - by0) / bScale - 0.5, out r, out g, out b);
                        }
                        r = r * (1 - washAmount) + washColor.R * washAmount;
                        g = g * (1 - washAmount) + washColor.G * washAmount;
                        b = b * (1 - washAmount) + washColor.B * washAmount;
                        int k = (j * cols + i) * 4; // BGRA, premultiplied
                        pixels[k] = (byte)Math.Round(b * alpha);
                        pixels[k + 1] = (byte)Math.Round(g * alpha);
                        pixels[k + 2] = (byte)Math.Round(r * alpha);
                        pixels[k + 3] = (byte)Math.Round(255 * alpha);
                    }
                }

                var bitmap = new WriteableBitmap(cols, rows);
                using (var stream = bitmap.PixelBuffer.AsStream())
                {
                    stream.Write(pixels, 0, pixels.Length);
                }
                bitmap.Invalidate();
                fade.Source = bitmap;
                fade.Margin = new Thickness(0, top, 0, 0);
                fade.Height = h - top;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AnimatedArtwork] fade: " + ex.Message);
            }
        }

        /// <summary>Bilinear sample of an opaque BGRA pixel buffer at (x, y) in pixel-center coordinates, edges clamped.</summary>
        private static void SampleBilinear(byte[] px, int width, int height, double x, double y, out double r, out double g, out double b)
        {
            x = Math.Max(0, Math.Min(width - 1, x));
            y = Math.Max(0, Math.Min(height - 1, y));
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(width - 1, x0 + 1), y1 = Math.Min(height - 1, y0 + 1);
            double fx = x - x0, fy = y - y0;
            int a = (y0 * width + x0) * 4, bb = (y0 * width + x1) * 4, c = (y1 * width + x0) * 4, d = (y1 * width + x1) * 4;
            double w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            b = px[a] * w00 + px[bb] * w10 + px[c] * w01 + px[d] * w11;
            g = px[a + 1] * w00 + px[bb + 1] * w10 + px[c + 1] * w01 + px[d + 1] * w11;
            r = px[a + 2] * w00 + px[bb + 2] * w10 + px[c + 2] * w01 + px[d + 2] * w11;
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
            var cornersParent = _artworkCorners != null ? _artworkCorners.Parent as Panel : null;
            if (cornersParent != null) cornersParent.Children.Remove(_artworkCorners);
            if (_artworkFade != null)
            {
                var fadeParent = _artworkFade.Parent as Panel;
                if (fadeParent != null) fadeParent.Children.Remove(_artworkFade);
                _artworkFade.Opacity = 0;
                _artworkFade.Source = null;
            }
        }

        /// <summary>The song was paused or resumed: the artwork follows it.</summary>
        private void SyncAnimatedArtworkPlayback(bool isPlaying)
        {
            ApplyArtworkPlayState();
        }

        private void ArtworkVideo_MediaOpened(object sender, RoutedEventArgs e)
        {
            var video = sender as MediaElement;
            if (video == null || video != _artworkVideo) return;
            // Reveal once frames are really flowing (the first decoded frames can be grey), the cover is the poster until then
            if (_artworkRevealTimer == null)
            {
                _artworkRevealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _artworkRevealTimer.Tick += ArtworkRevealTimer_Tick;
            }
            ApplyArtworkPlayState();
        }

        private void ArtworkRevealTimer_Tick(object sender, object e)
        {
            var video = _artworkVideo;
            if (video == null) { _artworkRevealTimer.Stop(); return; }
            if (video.Position.TotalSeconds >= 0.3)
            {
                UpdateArtworkCornerColors();
                if (_artworkFade != null && _artworkFade.Parent != null)
                {
                    UpdateArtworkFade();
                    _artworkFade.Opacity = 1;
                }
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
