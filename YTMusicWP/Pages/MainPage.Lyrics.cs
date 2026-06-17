using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Playback;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private TimeSpan ParseLrcTime(string timeStr)
        {
            try
            {
                int colonIdx = timeStr.IndexOf(':');
                int dotIdx = timeStr.IndexOf('.');

                if (colonIdx > 0)
                {
                    int min = int.Parse(timeStr.Substring(0, colonIdx));
                    int sec = 0;
                    int ms = 0;

                    if (dotIdx > 0)
                    {
                        sec = int.Parse(timeStr.Substring(colonIdx + 1, dotIdx - colonIdx - 1));
                        string msStr = timeStr.Substring(dotIdx + 1).PadRight(3, '0');
                        if (msStr.Length > 3) msStr = msStr.Substring(0, 3);
                        ms = int.Parse(msStr);
                    }
                    else
                    {
                        sec = int.Parse(timeStr.Substring(colonIdx + 1));
                    }
                    return new TimeSpan(0, 0, min, sec, ms);
                }
            }
            catch { }
            return TimeSpan.Zero;
        }
        // ── Lyrics Cache (in-memory, keyed by cleaned title+artist) ──
        private static readonly Dictionary<string, string> _lyricsCache = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _plainLyricsCache = new Dictionary<string, string>();
        private const int MAX_LYRICS_CACHE = 20;

        private async Task UpdateLyricsAsync(string title, string artist)
        {
            var oldLyricsCts = _lyricsCts;
            _lyricsCts = new CancellationTokenSource();
            if (oldLyricsCts != null) { oldLyricsCts.Cancel(); oldLyricsCts.Dispose(); }
            var token = _lyricsCts.Token;

            currentLyrics.Clear();
            currentLyricIndex = -1;
            _cachedLyricsScrollViewer = null;
            _cachedFullscreenLyricsScrollViewer = null;

            LyricsFallbackScrollViewer.Visibility = Visibility.Collapsed;
            LyricsListView.Visibility = Visibility.Visible;
            LyricsLoadingBar.Visibility = Visibility.Visible;

            try
            {
                if (title.StartsWith("LOCAL:"))
                {
                    LyricsFallbackText.Text = "No lyrics for offline files.";
                    LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                    LyricsLoadingBar.Visibility = Visibility.Collapsed;
                    return;
                }

                string cleanTitle = title
                    .Replace(" (Official Video)", "").Replace(" (Lyric Video)", "")
                    .Replace(" (Official Audio)", "").Replace(" (Audio)", "")
                    .Replace(" (Official MV)", "").Replace(" (Official Music Video)", "")
                    .Replace(" (Visualizer)", "").Replace(" (Visualiser)", "")
                    .Replace(" [Official Video]", "").Replace(" [Official Audio]", "")
                    .Replace(" [NCS Release]", "").Replace(" [NCS]", "")
                    .Replace(" (NCS Release)", "").Replace(" (NCS)", "");
                // Strip (feat. ...) and [feat. ...]
                cleanTitle = Regex.Replace(cleanTitle, @"\s*[\(\[](feat\.?|ft\.?|featuring)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
                // Strip trailing (Remix), (Extended Mix), etc.
                cleanTitle = Regex.Replace(cleanTitle, @"\s*[\(\[](?:Remix|Extended Mix|Radio Edit|Sped Up|Slowed)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
                cleanTitle = cleanTitle.Trim();
                string cleanArtist = CleanChannelName(artist);

                // Extract artist from title if needed
                string[] typeLabels = { "Song", "Video", "Artist", "Playlist", "Album", "EP", "Single", "" };
                if (Array.IndexOf(typeLabels, cleanArtist) >= 0)
                {
                    if (title.Contains(" - "))
                    {
                        var parts = title.Split(new[] { " - " }, StringSplitOptions.None);
                        if (parts.Length >= 2)
                        {
                            cleanArtist = parts[0].Trim();
                            cleanTitle = parts[1].Replace(" (Official Video)", "").Replace(" (Lyric Video)", "")
                                .Replace(" (Official Audio)", "").Replace(" (Audio)", "")
                                .Replace(" (Official MV)", "").Trim();
                        }
                    }
                }
                else if (cleanTitle.Contains(" - "))
                {
                    var titleParts = cleanTitle.Split(new[] { " - " }, StringSplitOptions.None);
                    if (titleParts.Length >= 2 && titleParts[0].Trim().Equals(cleanArtist, StringComparison.OrdinalIgnoreCase))
                        cleanTitle = titleParts[1].Trim();
                }

                string cacheKey = (cleanTitle + "|" + cleanArtist).ToLowerInvariant();

                // ── Check Cache First ──
                if (_lyricsCache.ContainsKey(cacheKey))
                {
                    ParseAndDisplaySyncedLyrics(_lyricsCache[cacheKey]);
                    LyricsLoadingBar.Visibility = Visibility.Collapsed;
                    return;
                }
                if (_plainLyricsCache.ContainsKey(cacheKey))
                {
                    LyricsFallbackText.Text = _plainLyricsCache[cacheKey];
                    LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                    LyricsLoadingBar.Visibility = Visibility.Collapsed;
                    return;
                }

                token.ThrowIfCancellationRequested();

                string syncedLyrics = null;
                string plainLyrics = null;

                // Helper: pick best synced lyrics match, preferring duration match
                double trackDurationSec = 0;
                Func<JArray, double, string[]> pickBestMatch = (arr, dur) =>
                {
                    if (arr == null || arr.Count == 0) return new string[] { null, null };
                    
                    JToken bestItem = null;
                    double bestDiff = double.MaxValue;
                    
                    foreach (var item in arr)
                    {
                        string s = item["syncedLyrics"]?.ToString();
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        
                        double itemDuration = item["duration"]?.Value<double>() ?? 0;
                        
                        if (dur > 10 && itemDuration > 10)
                        {
                            double diff = Math.Abs(itemDuration - dur);
                            if (diff < bestDiff)
                            {
                                bestDiff = diff;
                                bestItem = item;
                            }
                        }
                        else if (bestItem == null)
                        {
                            bestItem = item;
                            bestDiff = 999;
                        }
                    }
                    
                    if (bestItem != null)
                        return new string[] { bestItem["syncedLyrics"]?.ToString(), bestItem["plainLyrics"]?.ToString() };
                    
                    return new string[] { null, arr[0]["plainLyrics"]?.ToString() };
                };

                // ── Fire ALL search requests IMMEDIATELY ──
                string url1 = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) + "&artist_name=" + Uri.EscapeDataString(cleanArtist);
                string url2 = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(cleanTitle + " " + cleanArtist);
                var searchTask1 = _apiClient.GetStringAsync(url1);
                var searchTask2 = _apiClient.GetStringAsync(url2);

                // ── Quick duration poll (max 500ms) — in parallel with searches ──
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try { trackDurationSec = _appMediaPlayer.NaturalDuration.TotalSeconds; } catch { }
                    if (trackDurationSec > 10) break;
                    await Task.Delay(100);
                    token.ThrowIfCancellationRequested();
                }

                // ── Fire /api/get with duration in parallel too ──
                Task<string> getTask = null;
                if (trackDurationSec > 10)
                {
                    string getUrl = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(cleanTitle)
                        + "&artist_name=" + Uri.EscapeDataString(cleanArtist)
                        + "&duration=" + ((int)Math.Round(trackDurationSec)).ToString();
                    getTask = _apiClient.GetStringAsync(getUrl);
                }

                // ── LAYER 0: /api/get with exact duration (best, fastest) ──
                if (getTask != null)
                {
                    try
                    {
                        var getResp = await getTask;
                        token.ThrowIfCancellationRequested();
                        var getJson = JObject.Parse(getResp);
                        syncedLyrics = getJson["syncedLyrics"]?.ToString();
                        plainLyrics = getJson["plainLyrics"]?.ToString();
                    }
                    catch { }
                }

                // ── LAYER 1: Use search results (already running in parallel) ──
                if (string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    try
                    {
                        var resp1 = await searchTask1;
                        token.ThrowIfCancellationRequested();
                        var arr1 = JArray.Parse(resp1);
                        if (arr1.Count > 0)
                        {
                            var match1 = pickBestMatch(arr1, trackDurationSec);
                            syncedLyrics = match1[0];
                            plainLyrics = match1[1];
                        }
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(syncedLyrics))
                    {
                        try
                        {
                            var resp2 = await searchTask2;
                            token.ThrowIfCancellationRequested();
                            var arr2 = JArray.Parse(resp2);
                            if (arr2.Count > 0)
                            {
                                var match2 = pickBestMatch(arr2, trackDurationSec);
                                if (!string.IsNullOrWhiteSpace(match2[0])) syncedLyrics = match2[0];
                                if (string.IsNullOrWhiteSpace(plainLyrics)) plainLyrics = match2[1];
                            }
                        }
                        catch { }
                    }
                }

                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    // Cache for later
                    if (_lyricsCache.Count >= MAX_LYRICS_CACHE) _lyricsCache.Remove(_lyricsCache.Keys.First());
                    _lyricsCache[cacheKey] = syncedLyrics;
                    ParseAndDisplaySyncedLyrics(syncedLyrics);
                }
                else if (!string.IsNullOrWhiteSpace(plainLyrics))
                {
                    if (_plainLyricsCache.Count >= MAX_LYRICS_CACHE) _plainLyricsCache.Remove(_plainLyricsCache.Keys.First());
                    _plainLyricsCache[cacheKey] = plainLyrics;
                    LyricsFallbackText.Text = plainLyrics;
                    LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Fallback: try YouTube captions/subtitles
                    bool captionFound = false;
                    if (currentTrack != null && !string.IsNullOrEmpty(currentTrack.VideoId) && !currentTrack.VideoId.StartsWith("LOCAL:"))
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            var captionTracks = await InnerTubeClient.GetCaptionTracksAsync(currentTrack.VideoId);
                            if (captionTracks.Count > 0)
                            {
                                // Prefer user language, then English, then first available
                                var preferred = captionTracks.FirstOrDefault(c => c.LanguageCode == "vi")
                                    ?? captionTracks.FirstOrDefault(c => c.LanguageCode == "en")
                                    ?? captionTracks.FirstOrDefault(c => c.LanguageCode.StartsWith("en"))
                                    ?? captionTracks[0];

                                token.ThrowIfCancellationRequested();
                                var captionLines = await InnerTubeClient.FetchCaptionTextAsync(preferred.BaseUrl);
                                if (captionLines.Count > 0)
                                {
                                    foreach (var cl in captionLines) currentLyrics.Add(cl);
                                    currentLyrics.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "", FontSize = _lyricFontSize });
                                    captionFound = true;
                                    System.Diagnostics.Debug.WriteLine("[Lyrics] Caption fallback: " + captionLines.Count + " lines (" + preferred.LanguageName + ")");
                                }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }

                    if (!captionFound)
                    {
                        LyricsFallbackText.Text = "Lyrics not found for this track.";
                        LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                        LyricsListView.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                LyricsFallbackText.Text = "Failed to load lyrics. Please check your connection.";
                LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                LyricsListView.Visibility = Visibility.Collapsed;
            }
            finally
            {
                LyricsLoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ParseAndDisplaySyncedLyrics(string syncedLyrics)
        {
            var lines = syncedLyrics.Split('\n');
            var parsedLines = new List<LyricLine>(lines.Length);

            foreach (var line in lines)
            {
                string tempLine = line.Trim();
                var times = new List<TimeSpan>(2);

                while (tempLine.StartsWith("[") && tempLine.IndexOf(']') > 0)
                {
                    int bracketEnd = tempLine.IndexOf(']');
                    string timeStr = tempLine.Substring(1, bracketEnd - 1);
                    times.Add(ParseLrcTime(timeStr));
                    tempLine = tempLine.Substring(bracketEnd + 1).Trim();
                }

                if (times.Count > 0)
                {
                    string text = string.IsNullOrWhiteSpace(tempLine) ? "♪" : tempLine;
                    foreach (var t in times)
                        parsedLines.Add(new LyricLine { Time = t, Text = text, FontSize = _lyricFontSize });
                }
            }

            parsedLines.Sort((a, b) => a.Time.CompareTo(b.Time));
            foreach (var p in parsedLines) currentLyrics.Add(p);
            currentLyrics.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "", FontSize = _lyricFontSize });
        }

        private void LyricsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var line = e.ClickedItem as LyricLine;
            if (line != null && line.Text != "♪" && line.Text != "")
            {
                try
                {
                    if (_appMediaPlayer.CurrentState != MediaPlayerState.Closed)
                    {
                        _appMediaPlayer.Position = line.Time;
                        if (_appMediaPlayer.CurrentState == MediaPlayerState.Paused) _appMediaPlayer.Play();
                    }
                }
                catch { }
            }
        }
        private void LyricsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer != null)
            {
                // Start all items in "inactive" state
                args.ItemContainer.Opacity = 0.35;
                var st = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 0.85, ScaleY = 0.85 };
                args.ItemContainer.RenderTransformOrigin = new Point(0, 0.5);
                args.ItemContainer.RenderTransform = st;
            }
        }

        // ══════════════════════════════════════════
        // FULLSCREEN LYRICS
        // ══════════════════════════════════════════
        private void ToggleFullscreenLyrics_Click(object sender, RoutedEventArgs e)
        {
            if (currentLyrics == null || currentLyrics.Count == 0)
            {
                ShowToast("No lyrics available");
                return;
            }

            // Set track info
            if (currentTrack != null)
            {
                FullscreenLyricsTitle.Text = currentTrack.Title;
                FullscreenLyricsArtist.Text = currentTrack.ChannelName;
            }

            // Copy gradient from Now Playing
            try
            {
                FullscreenLyricsGradientTop.Color = NowPlayingGradientTop.Color;
            }
            catch { }

            // Bind same lyrics data
            FullscreenLyricsListView.ItemsSource = currentLyrics;

            // Show with fade-in
            FullscreenLyricsView.Visibility = Visibility.Visible;
            FullscreenLyricsView.Opacity = 0;
            var fadeIn = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var anim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(300))
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, FullscreenLyricsView);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
            fadeIn.Children.Add(anim);
            fadeIn.Completed += (s, a) =>
            {
                // Set correct opacity on all containers after layout is ready
                _cachedFullscreenLyricsScrollViewer = null;
                FullscreenLyricsListView.UpdateLayout();
                for (int i = 0; i < currentLyrics.Count; i++)
                {
                    var container = FullscreenLyricsListView.ContainerFromIndex(i) as FrameworkElement;
                    if (container != null)
                        container.Opacity = (i == currentLyricIndex) ? 1.0 : 0.5;
                }
                // Scroll to current lyric
                if (currentLyricIndex >= 0 && currentLyricIndex < currentLyrics.Count)
                    FullscreenLyricsListView.ScrollIntoView(currentLyrics[currentLyricIndex]);
            };
            fadeIn.Begin();
        }

        private void CloseFullscreenLyrics_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // Fade out
            var fadeOut = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var anim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200))
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, FullscreenLyricsView);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
            fadeOut.Children.Add(anim);
            fadeOut.Completed += (s, a) =>
            {
                FullscreenLyricsView.Visibility = Visibility.Collapsed;
                // Refresh regular lyrics containers to match current sync state
                RefreshRegularLyricsContainers();
            };
            fadeOut.Begin();
        }

        private void RefreshRegularLyricsContainers()
        {
            for (int i = 0; i < currentLyrics.Count; i++)
            {
                var container = LyricsListView.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                if (i == currentLyricIndex)
                {
                    container.Opacity = 1.0;
                    var st = container.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                    if (st != null) { st.ScaleX = 1.0; st.ScaleY = 1.0; }
                }
                else
                {
                    container.Opacity = 0.35;
                    var st = container.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                    if (st != null) { st.ScaleX = 0.85; st.ScaleY = 0.85; }
                }
            }

            // Scroll to current lyric (center it)
            if (currentLyricIndex >= 0 && currentLyricIndex < currentLyrics.Count)
            {
                LyricsListView.ScrollIntoView(currentLyrics[currentLyricIndex]);

                // Delay slightly to let layout update, then center-scroll
                LyricsListView.UpdateLayout();
                if (_cachedLyricsScrollViewer == null)
                    _cachedLyricsScrollViewer = GetScrollViewer(LyricsListView);
                var activeContainer = LyricsListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                if (_cachedLyricsScrollViewer != null && activeContainer != null)
                {
                    var transform = activeContainer.TransformToVisual(_cachedLyricsScrollViewer);
                    var lyricPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    double targetOff = _cachedLyricsScrollViewer.VerticalOffset + lyricPos.Y
                                    - (_cachedLyricsScrollViewer.ViewportHeight / 2.0)
                                    + (activeContainer.ActualHeight / 2.0);
                    _cachedLyricsScrollViewer.ChangeView(null, targetOff, null, false);
                }
            }
        }

        private void FullscreenLyricsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer != null)
            {
                // Set all non-active lines to dim, active line to bright
                args.ItemContainer.Opacity = (args.ItemIndex == currentLyricIndex) ? 1.0 : 0.5;
            }
        }

    }
}
