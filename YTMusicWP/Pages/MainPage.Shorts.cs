using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Playback;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        // ==========================================
        // MUSIC SHORTS — Spotify Clips-style Discovery
        // ==========================================

        private static readonly string[] _shortsCategories = {
            "EDM", "K-Pop", "Pop", "Lofi", "Hip-Hop", "Rock", "Anime", "V-Pop"
        };

        private static readonly string[] _shortsCategoryQueries = {
            "EDM Dance House Hits",
            "K-Pop Trending Hits",
            "Pop Music Hits",
            "Lofi Chill Beats",
            "Hip-Hop Rap Hits",
            "Rock Music Hits",
            "Anime OST Hits",
            "V-Pop Vietnamese Hits"
        };

        private static readonly string[] _shortsCategoryHashtags = {
            "#edm|#dance|#house",
            "#kpop|#korean|#idol",
            "#pop|#hits|#trending",
            "#lofi|#chill|#relax",
            "#hiphop|#rap|#trap",
            "#rock|#alternative|#indie",
            "#anime|#ost|#japan",
            "#vpop|#vietnam|#nhạc việt"
        };

        private int _shortsCategoryIndex = 0;
        private int _shortsSongIndex = 0;
        private List<YouTubeTrack> _shortsSongs = new List<YouTubeTrack>();
        private Dictionary<int, List<YouTubeTrack>> _shortsCategoryCache = new Dictionary<int, List<YouTubeTrack>>();
        private DateTime _shortsCacheTime = DateTime.MinValue;
        private bool _shortsIsOpen = false;

        // ==========================================
        // OPEN / CLOSE
        // ==========================================
        private async void OpenShortsView(int categoryIndex = 0)
        {
            _shortsCategoryIndex = categoryIndex;
            _shortsIsOpen = true;

            // Build category dots
            BuildCategoryDots();

            ShortsView.Visibility = Visibility.Visible;

            // Slide-in animation
            var sb = new Storyboard();
            var anim = new DoubleAnimation
            {
                From = 1000,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(anim, ShortsTransform);
            Storyboard.SetTargetProperty(anim, "Y");
            sb.Children.Add(anim);
            sb.Begin();

            // Check cache validity (24h)
            if ((DateTime.Now - _shortsCacheTime).TotalHours >= 24)
            {
                _shortsCategoryCache.Clear();
                _shortsCacheTime = DateTime.Now;
            }

            await LoadShortsCategoryAsync(_shortsCategoryIndex);
        }

        private void CloseShortsView()
        {
            _shortsIsOpen = false;

            // Stop waveform
            if (_waveformStoryboard != null) { _waveformStoryboard.Stop(); _waveformStoryboard = null; }

            var sb = new Storyboard();
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1000,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(anim, ShortsTransform);
            Storyboard.SetTargetProperty(anim, "Y");
            sb.Children.Add(anim);
            sb.Completed += (s, e) =>
            {
                ShortsView.Visibility = Visibility.Collapsed;
            };
            sb.Begin();
        }

        // ==========================================
        // CATEGORY DOTS
        // ==========================================
        private void BuildCategoryDots()
        {
            ShortsCategoryDots.Children.Clear();
            for (int i = 0; i < _shortsCategories.Length; i++)
            {
                int idx = i; // closure capture
                var dot = new Border
                {
                    Width = i == _shortsCategoryIndex ? 20 : 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(
                        i == _shortsCategoryIndex ? Colors.White : Color.FromArgb(120, 255, 255, 255)),
                    Margin = new Thickness(3, 0, 3, 0)
                };
                dot.Tapped += (s, e) => SwitchShortsCategory(idx);
                ShortsCategoryDots.Children.Add(dot);
            }

            ShortsCategoryTitle.Text = "#" + _shortsCategories[_shortsCategoryIndex].ToLower();
        }

        private void UpdateCategoryDots()
        {
            for (int i = 0; i < ShortsCategoryDots.Children.Count; i++)
            {
                var dot = ShortsCategoryDots.Children[i] as Border;
                if (dot == null) continue;
                dot.Width = i == _shortsCategoryIndex ? 20 : 8;
                dot.Background = new SolidColorBrush(
                    i == _shortsCategoryIndex ? Colors.White : Color.FromArgb(120, 255, 255, 255));
            }
            ShortsCategoryTitle.Text = "#" + _shortsCategories[_shortsCategoryIndex].ToLower();
        }

        private async void SwitchShortsCategory(int index)
        {
            if (index < 0 || index >= _shortsCategories.Length) return;
            _shortsCategoryIndex = index;
            _shortsSongIndex = 0;
            UpdateCategoryDots();
            await LoadShortsCategoryAsync(index);
        }

        // ==========================================
        // LOAD SONGS FOR CATEGORY
        // ==========================================
        private async Task LoadShortsCategoryAsync(int categoryIndex)
        {
            // Check cache
            if (_shortsCategoryCache.ContainsKey(categoryIndex) && _shortsCategoryCache[categoryIndex].Count > 0)
            {
                _shortsSongs = _shortsCategoryCache[categoryIndex];
                _shortsSongIndex = 0;
                DisplayCurrentShort();
                return;
            }

            // Show loading
            ShortsSongTitle.Text = "Loading...";
            ShortsSongArtist.Text = "";

            try
            {
                var result = await InnerTubeClient.SearchWithContinuationAsync(
                    _shortsCategoryQueries[categoryIndex], 10);

                if (result != null && result.Tracks != null && result.Tracks.Count > 0)
                {
                    // Filter out playlists/channels — only songs/videos
                    _shortsSongs = result.Tracks
                        .Where(t => !t.VideoId.StartsWith("PLAYLIST:") && !t.VideoId.StartsWith("CHANNEL:"))
                        .Take(10)
                        .ToList();

                    if (_shortsSongs.Count > 0)
                    {
                        _shortsCategoryCache[categoryIndex] = _shortsSongs;
                        _shortsSongIndex = 0;
                        DisplayCurrentShort();
                        return;
                    }
                }
            }
            catch { }

            ShortsSongTitle.Text = "No results";
            ShortsSongArtist.Text = "Try another category";
        }

        // ==========================================
        // DISPLAY CURRENT SHORT
        // ==========================================
        private Storyboard _waveformStoryboard;

        private void DisplayCurrentShort()
        {
            if (_shortsSongs == null || _shortsSongs.Count == 0 || _shortsSongIndex >= _shortsSongs.Count) return;

            var track = _shortsSongs[_shortsSongIndex];

            // Song info
            ShortsSongTitle.Text = track.Title;
            ShortsSongArtist.Text = track.ChannelName;
            ShortsArtistName.Text = track.ChannelName;
            ShortsArtistSub.Text = "";

            // Thumbnail
            string thumbUrl = GetHighResThumbnail(track.ThumbnailUrl);
            if (!string.IsNullOrEmpty(thumbUrl))
            {
                try
                {
                    // Background: dimmed ambient thumbnail
                    ShortsBackgroundImage.Source = new BitmapImage(new Uri(thumbUrl, UriKind.Absolute)) { DecodePixelWidth = 400 };
                    ShortsBackgroundImage.Opacity = 0.3;

                    // Cover art in center
                    ShortsCoverArt.ImageSource = new BitmapImage(new Uri(thumbUrl, UriKind.Absolute)) { DecodePixelWidth = 240 };
                    ShortsCoverArtPanel.Visibility = Visibility.Visible;

                    // Mini cover
                    ShortsMiniCover.ImageSource = new BitmapImage(new Uri(track.ThumbnailUrl, UriKind.Absolute)) { DecodePixelWidth = 50 };

                    // Artist avatar
                    ShortsArtistAvatarBrush.ImageSource = new BitmapImage(new Uri(track.ThumbnailUrl, UriKind.Absolute)) { DecodePixelWidth = 40 };
                }
                catch { }
            }

            // Start waveform animation
            StartWaveformAnimation();

            // Update hashtags
            UpdateShortsHashtags();

            // Auto-play this track
            PlayTrack(track);
            UpdateShortsPauseIcon(true);
        }

        private void StartWaveformAnimation()
        {
            // Stop previous
            if (_waveformStoryboard != null)
            {
                _waveformStoryboard.Stop();
                _waveformStoryboard = null;
            }

            _waveformStoryboard = new Storyboard();
            _waveformStoryboard.RepeatBehavior = RepeatBehavior.Forever;

            var rand = new Random();

            // Animate all waveform bars (children of the two StackPanels in ShortsCoverArtPanel)
            foreach (var child in ShortsCoverArtPanel.Children)
            {
                var panel = child as StackPanel;
                if (panel == null) continue;

                foreach (var bar in panel.Children)
                {
                    var border = bar as Border;
                    if (border == null) continue;

                    // Ensure each bar has a ScaleTransform
                    if (border.RenderTransform == null || !(border.RenderTransform is ScaleTransform))
                    {
                        border.RenderTransform = new ScaleTransform { ScaleY = 1, CenterY = 0.5 };
                        border.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                    }

                    var scaleTransform = border.RenderTransform as ScaleTransform;

                    // Random animation per bar
                    double minScale = 0.3 + rand.NextDouble() * 0.3;
                    double maxScale = 0.8 + rand.NextDouble() * 0.7;
                    double durationMs = 300 + rand.Next(400);
                    double beginMs = rand.Next(200);

                    var anim = new DoubleAnimation
                    {
                        From = minScale,
                        To = maxScale,
                        Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(beginMs)
                    };
                    Storyboard.SetTarget(anim, scaleTransform);
                    Storyboard.SetTargetProperty(anim, "ScaleY");
                    _waveformStoryboard.Children.Add(anim);
                }
            }

            if (_waveformStoryboard.Children.Count > 0)
                _waveformStoryboard.Begin();
        }

        private void UpdateShortsHashtags()
        {
            ShortsHashtags.Children.Clear();
            string[] tags = _shortsCategoryHashtags[_shortsCategoryIndex].Split('|');
            var semiBoldFont = new FontFamily("/Assets/Fonts/Montserrat-SemiBold.ttf#Montserrat");
            foreach (var tag in tags)
            {
                var tb = new TextBlock
                {
                    Text = tag,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    Margin = new Thickness(0, 0, 15, 0),
                    FontFamily = semiBoldFont
                };
                ShortsHashtags.Children.Add(tb);
            }
        }

        // ==========================================
        // SWIPE NAVIGATION (Vertical = next/prev song, detect horizontal for category)
        // ==========================================
        private double _shortsSwipeDeltaY = 0;

        private void Shorts_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            _shortsSwipeDeltaY += e.Delta.Translation.Y;
        }

        private void Shorts_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            double totalY = _shortsSwipeDeltaY;
            _shortsSwipeDeltaY = 0;

            if (Math.Abs(totalY) < 50) return; // Threshold

            if (totalY < -50)
            {
                // Swipe up → next song
                if (_shortsSongIndex < _shortsSongs.Count - 1)
                {
                    _shortsSongIndex++;
                    AnimateShortTransition(true);
                }
                else
                {
                    // Wrap to next category
                    int nextCat = (_shortsCategoryIndex + 1) % _shortsCategories.Length;
                    SwitchShortsCategory(nextCat);
                }
            }
            else if (totalY > 50)
            {
                // Swipe down → prev song
                if (_shortsSongIndex > 0)
                {
                    _shortsSongIndex--;
                    AnimateShortTransition(false);
                }
                else
                {
                    // Wrap to prev category
                    int prevCat = (_shortsCategoryIndex - 1 + _shortsCategories.Length) % _shortsCategories.Length;
                    SwitchShortsCategory(prevCat);
                }
            }
        }

        private void AnimateShortTransition(bool slideUp)
        {
            // Quick fade transition
            var sb = new Storyboard();
            var fadeOut = new DoubleAnimation
            {
                From = 1, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(100))
            };
            Storyboard.SetTarget(fadeOut, ShortsView);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            sb.Children.Add(fadeOut);
            sb.Completed += (s, e) =>
            {
                DisplayCurrentShort();
                var sb2 = new Storyboard();
                var fadeIn = new DoubleAnimation
                {
                    From = 0, To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(150))
                };
                Storyboard.SetTarget(fadeIn, ShortsView);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                sb2.Children.Add(fadeIn);
                sb2.Begin();
            };
            sb.Begin();
        }

        // ==========================================
        // EVENT HANDLERS
        // ==========================================
        private void ShortsBack_Click(object sender, RoutedEventArgs e)
        {
            CloseShortsView();
        }

        private void ShortsPlayCurrent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_appMediaPlayer.CurrentState == MediaPlayerState.Playing)
                {
                    _appMediaPlayer.Pause();
                    UpdateShortsPauseIcon(false);
                }
                else
                {
                    _appMediaPlayer.Play();
                    UpdateShortsPauseIcon(true);
                }
            }
            catch { }
        }

        private void UpdateShortsPauseIcon(bool isPlaying)
        {
            ShortsPauseBtn.Text = isPlaying ? "❚❚" : "▶";
        }

        private void ShortsAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_shortsSongs == null || _shortsSongs.Count == 0 || _shortsSongIndex >= _shortsSongs.Count) return;
            var track = _shortsSongs[_shortsSongIndex];

            if (!currentQueueTracks.Any(t => t.VideoId == track.VideoId))
            {
                currentQueueTracks.Add(track);
                ShowToast("Added to queue");
            }
            else
            {
                ShowToast("Already in queue");
            }
        }
    }
}
