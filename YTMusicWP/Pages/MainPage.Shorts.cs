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

        // Genre keywords for matching history tracks
        private static readonly string[][] _shortsCategoryKeywords = {
            new[] { "edm", "dance", "house", "electronic", "dj", "remix", "trance", "dubstep" },
            new[] { "kpop", "k-pop", "korean", "bts", "blackpink", "twice", "stray kids", "aespa", "ive", "newjeans" },
            new[] { "pop", "hit", "billboard", "top 40", "chart", "taylor swift", "ed sheeran", "ariana", "bruno mars" },
            new[] { "lofi", "lo-fi", "chill", "relax", "study", "beats", "ambient" },
            new[] { "hip hop", "hip-hop", "rap", "trap", "drake", "kendrick", "eminem", "kanye", "21 savage" },
            new[] { "rock", "metal", "punk", "alternative", "indie rock", "guitar", "linkin park" },
            new[] { "anime", "ost", "opening", "ending", "japanese", "naruto", "one piece", "jujutsu" },
            new[] { "vpop", "v-pop", "vietnam", "nhạc", "sơn tùng", "jack", "bích phương", "đen vâu", "hoàng thùy linh" }
        };

        // Category display order (reordered by history analysis)
        private int[] _shortsCategoryOrder;

        private int _shortsCategoryIndex = 0;
        private int _shortsSongIndex = 0;
        private List<YouTubeTrack> _shortsSongs = new List<YouTubeTrack>();
        private Dictionary<int, List<YouTubeTrack>> _shortsCategoryCache = new Dictionary<int, List<YouTubeTrack>>();
        private DateTime _shortsCacheTime = DateTime.MinValue;
        private bool _shortsIsOpen = false;

        /// <summary>
        /// Analyze historyTracks to score each category and reorder
        /// </summary>
        private void BuildSmartCategoryOrder()
        {
            var scores = new int[_shortsCategories.Length];

            if (historyTracks != null && historyTracks.Count > 0)
            {
                foreach (var track in historyTracks)
                {
                    string title = (track.Title ?? "").ToLowerInvariant();
                    string channel = (track.ChannelName ?? "").ToLowerInvariant();
                    string combined = title + " " + channel;

                    for (int i = 0; i < _shortsCategoryKeywords.Length; i++)
                    {
                        foreach (var keyword in _shortsCategoryKeywords[i])
                        {
                            if (combined.Contains(keyword))
                            {
                                scores[i]++;
                                break; // One match per track per category
                            }
                        }
                    }
                }
            }

            // Build ordered index array
            _shortsCategoryOrder = new int[_shortsCategories.Length];
            for (int i = 0; i < _shortsCategories.Length; i++) _shortsCategoryOrder[i] = i;

            // Sort by score descending (categories with most matches first)
            // If no history (all scores 0), use default trending order: Pop, K-Pop, EDM, Lofi, Hip-Hop, V-Pop, Rock, Anime
            bool hasHistory = scores.Any(s => s > 0);
            if (hasHistory)
            {
                Array.Sort(_shortsCategoryOrder, (a, b) => scores[b].CompareTo(scores[a]));
            }
            else
            {
                // Default trending order
                _shortsCategoryOrder = new[] { 2, 1, 0, 3, 4, 7, 5, 6 }; // Pop, K-Pop, EDM, Lofi, Hip-Hop, V-Pop, Rock, Anime
            }
        }

        // ==========================================
        // OPEN / CLOSE
        // ==========================================
        private async void OpenShortsView(int categoryIndex = 0)
        {
            _shortsCategoryIndex = categoryIndex;
            _shortsIsOpen = true;

            // Smart category ordering based on listening history
            BuildSmartCategoryOrder();

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

            await LoadShortsCategoryAsync(GetRealCategoryIndex(_shortsCategoryIndex));
        }

        private void CloseShortsView()
        {
            _shortsIsOpen = false;

            // Stop waveform + audio + loop timer
            if (_waveformStoryboard != null) { _waveformStoryboard.Stop(); _waveformStoryboard = null; }
            StopShortsLoop();
            try { ShortsMediaPlayer.Stop(); ShortsMediaPlayer.Source = null; } catch { }

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
        private int GetRealCategoryIndex(int displayIndex)
        {
            if (_shortsCategoryOrder != null && displayIndex < _shortsCategoryOrder.Length)
                return _shortsCategoryOrder[displayIndex];
            return displayIndex;
        }

        private void BuildCategoryDots()
        {
            ShortsCategoryDots.Children.Clear();
            for (int i = 0; i < _shortsCategories.Length; i++)
            {
                int idx = i;
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

            ShortsCategoryTitle.Text = "#" + _shortsCategories[GetRealCategoryIndex(_shortsCategoryIndex)].ToLower();
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
            ShortsCategoryTitle.Text = "#" + _shortsCategories[GetRealCategoryIndex(_shortsCategoryIndex)].ToLower();
        }

        private async void SwitchShortsCategory(int index)
        {
            if (index < 0 || index >= _shortsCategories.Length) return;
            _shortsCategoryIndex = index;
            _shortsSongIndex = 0;
            UpdateCategoryDots();
            await LoadShortsCategoryAsync(GetRealCategoryIndex(index));
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
                    // Blurred background: 30px decode → pixelated/blurred when stretched
                    ShortsBlurBg.ImageSource = new BitmapImage(new Uri(thumbUrl, UriKind.Absolute)) { DecodePixelWidth = 30 };

                    // Cover art in center (crisp)
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

            // Auto-play via independent MediaElement (not BackgroundMediaPlayer)
            PlayShortsAudio(track.VideoId);

            // Update heart state
            bool isFav = favoriteTracks.Any(t => t.VideoId == track.VideoId);
            ShortsHeartBtn.Text = isFav ? "♥" : "♡";
            ShortsHeartBtn.Foreground = isFav ? _greenBrush : _whiteBrush;
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

                    // Smooth animation: longer duration + easing
                    double minScale = 0.2 + rand.NextDouble() * 0.3;
                    double maxScale = 0.9 + rand.NextDouble() * 0.6;
                    double durationMs = 600 + rand.Next(600); // 600-1200ms (slower = smoother)
                    double beginMs = rand.Next(500); // More staggered

                    var anim = new DoubleAnimation
                    {
                        From = minScale,
                        To = maxScale,
                        Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(beginMs),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
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
            string[] tags = _shortsCategoryHashtags[GetRealCategoryIndex(_shortsCategoryIndex)].Split('|');
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
        // SWIPE NAVIGATION (Vertical = next/prev song, Horizontal = next/prev category)
        // ==========================================
        private double _shortsSwipeDeltaY = 0;
        private double _shortsSwipeDeltaX = 0;

        private void Shorts_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            _shortsSwipeDeltaY += e.Delta.Translation.Y;
            _shortsSwipeDeltaX += e.Delta.Translation.X;
        }

        private void Shorts_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            double totalY = _shortsSwipeDeltaY;
            double totalX = _shortsSwipeDeltaX;
            _shortsSwipeDeltaY = 0;
            _shortsSwipeDeltaX = 0;

            // Determine dominant axis
            bool isHorizontal = Math.Abs(totalX) > Math.Abs(totalY);

            if (isHorizontal && Math.Abs(totalX) >= 50)
            {
                // Horizontal swipe → switch category
                if (totalX < -50)
                {
                    // Swipe left → next category
                    int nextCat = (_shortsCategoryIndex + 1) % _shortsCategories.Length;
                    SwitchShortsCategory(nextCat);
                }
                else if (totalX > 50)
                {
                    // Swipe right → prev category
                    int prevCat = (_shortsCategoryIndex - 1 + _shortsCategories.Length) % _shortsCategories.Length;
                    SwitchShortsCategory(prevCat);
                }
            }
            else if (!isHorizontal && Math.Abs(totalY) >= 50)
            {
                // Vertical swipe → switch song
                if (totalY < -50)
                {
                    // Swipe up → next song
                    if (_shortsSongIndex < _shortsSongs.Count - 1)
                    {
                        _shortsSongIndex++;
                        AnimateShortTransition(true);
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
        // INDEPENDENT AUDIO PLAYER — 15s LOOP
        // ==========================================
        private DispatcherTimer _shortsLoopTimer;
        private static readonly double ShortsClipSeconds = 15.0;

        private async void PlayShortsAudio(string videoId)
        {
            try
            {
                // Stop current + timer
                StopShortsLoop();
                ShortsMediaPlayer.Stop();

                string streamUrl = await InnerTubeClient.ResolveStreamUrlAsync(videoId);
                if (!string.IsNullOrEmpty(streamUrl) && _shortsIsOpen)
                {
                    ShortsMediaPlayer.Source = new Uri(streamUrl, UriKind.Absolute);
                    ShortsMediaPlayer.Play();
                    // Start loop timer (plays 15s from start, then loops)
                    StartShortsLoop();
                }
            }
            catch { }
        }

        private void StartShortsLoop()
        {
            StopShortsLoop();
            _shortsLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
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
                if (ShortsMediaPlayer.CurrentState != Windows.UI.Xaml.Media.MediaElementState.Playing) return;

                // Loop back to start after 15 seconds
                if (ShortsMediaPlayer.Position.TotalSeconds >= ShortsClipSeconds)
                {
                    ShortsMediaPlayer.Position = TimeSpan.Zero;
                }
            }
            catch { }
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
                if (ShortsMediaPlayer.CurrentState == Windows.UI.Xaml.Media.MediaElementState.Playing)
                {
                    ShortsMediaPlayer.Pause();
                    UpdateShortsPauseIcon(false);
                }
                else
                {
                    ShortsMediaPlayer.Play();
                    UpdateShortsPauseIcon(true);
                }
            }
            catch { }
        }

        private void UpdateShortsPauseIcon(bool isPlaying)
        {
            ShortsPauseBtn.Text = isPlaying ? "❚❚" : "▶";
        }

        private void ShortsHeart_Click(object sender, RoutedEventArgs e)
        {
            if (_shortsSongs == null || _shortsSongs.Count == 0 || _shortsSongIndex >= _shortsSongs.Count) return;
            var track = _shortsSongs[_shortsSongIndex];

            var existing = favoriteTracks.FirstOrDefault(t => t.VideoId == track.VideoId);
            if (existing != null)
            {
                favoriteTracks.Remove(existing);
                ShortsHeartBtn.Text = "♡";
                ShortsHeartBtn.Foreground = _whiteBrush;
                ShowToast("Removed from Favorites");
            }
            else
            {
                favoriteTracks.Insert(0, track);
                ShortsHeartBtn.Text = "♥";
                ShortsHeartBtn.Foreground = _greenBrush;
                ShowToast("Added to Favorites");
            }
            SaveFavoritesAsync();
        }

        private void ShortsSongBar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_shortsSongs == null || _shortsSongs.Count == 0 || _shortsSongIndex >= _shortsSongs.Count) return;
            var track = _shortsSongs[_shortsSongIndex];

            // Close Shorts first (stops shorts audio)
            CloseShortsView();

            // Play in main player
            PlayTrack(track);
        }
    }
}
