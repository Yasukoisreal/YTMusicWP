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
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using YTMusicWP.Services;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private static readonly SolidColorBrush _lyricPendingBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(130, 255, 255, 255));
        private DispatcherTimer _lyricsWordTimer;

        private bool TryParseLrcTime(string timeStr, out TimeSpan result)
        {
            return Services.LrcParser.TryParseLrcTime(timeStr, out result);
        }

        private TimeSpan ParseLrcTime(string timeStr)
        {
            return Services.LrcParser.ParseLrcTime(timeStr);
        }
        private class LyricsCacheEntry
        {
            public string Synced { get; set; }
            public string Plain { get; set; }
            public List<LyricLine> Lines { get; set; }
        }

        // ── Lyrics Cache (in-memory LRU, keyed by cleaned title+artist) ──
        private static readonly Dictionary<string, LyricsCacheEntry> _lyricsCache = new Dictionary<string, LyricsCacheEntry>();
        private static readonly List<string> _lyricsCacheOrder = new List<string>();
        private const int MAX_LYRICS_CACHE = 20;

        internal static void ClearLyricsCache()
        {
            try
            {
                _lyricsCache.Clear();
                _lyricsCacheOrder.Clear();
            }
            catch { }
        }

        private async Task UpdateLyricsAsync(string title, string artist)
        {
            var oldLyricsCts = _lyricsCts;
            _lyricsCts = new CancellationTokenSource();
            if (oldLyricsCts != null)
            {
                try { oldLyricsCts.Cancel(); } catch { }
            }
            var token = _lyricsCts.Token;

            currentLyrics.Clear();
            currentLyricIndex = -1;
            ClearMiniLyric();
            _cachedLyricsScrollViewer = null;
            _cachedFullscreenLyricsScrollViewer = null;
            UpdateWordTimerState();

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

                // ── Check Cache First (LRU Touch) ──
                LyricsCacheEntry cachedEntry;
                if (_lyricsCache.TryGetValue(cacheKey, out cachedEntry))
                {
                    _lyricsCacheOrder.Remove(cacheKey);
                    _lyricsCacheOrder.Add(cacheKey);

                    if (cachedEntry.Lines != null && cachedEntry.Lines.Count > 0)
                    {
                        DisplayLoadedLyrics(cachedEntry.Lines);
                        LyricsLoadingBar.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(cachedEntry.Synced))
                    {
                        ParseAndDisplaySyncedLyrics(cachedEntry.Synced);
                        LyricsLoadingBar.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(cachedEntry.Plain))
                    {
                        LyricsFallbackText.Text = cachedEntry.Plain;
                        LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                        LyricsListView.Visibility = Visibility.Collapsed;
                        LyricsLoadingBar.Visibility = Visibility.Collapsed;
                        return;
                    }
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

                // --- APPLE MUSIC LYRICS (TTML) ---
                AppleMusicLyricsResult amResult = null;
                try
                {
                    int durSecs = 0;
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        try { durSecs = (int)Math.Round(_appMediaPlayer.NaturalDuration.TotalSeconds); } catch { }
                        if (durSecs > 10) break;
                        await Task.Delay(100);
                    }

                    amResult = await YTMusicWP.Services.AppleMusicLyricsApi.GetLyricsResultAsync(cleanTitle, cleanArtist, durSecs);
                    if (amResult != null && (amResult.Lines != null || !string.IsNullOrWhiteSpace(amResult.SyncedLrc) || !string.IsNullOrWhiteSpace(amResult.PlainLyrics)))
                    {
                        syncedLyrics = amResult.SyncedLrc;
                        plainLyrics = amResult.PlainLyrics;
                        if (!string.IsNullOrWhiteSpace(syncedLyrics)) syncedLyrics += "\n[99:99.99] Lyrics provided by Apple Music";
                        if (!string.IsNullOrWhiteSpace(plainLyrics)) plainLyrics += "\n\nLyrics provided by Apple Music";
                        System.Diagnostics.Debug.WriteLine("Fetched lyrics from Apple Music (HasWordSync=" + amResult.HasWordSync + ")");

                        if (amResult.Lines != null && amResult.Lines.Count > 0)
                        {
                            var displayLines = new List<LyricLine>(amResult.Lines.Count + 2);
                            foreach (var l in amResult.Lines)
                            {
                                l.FontSize = _lyricFontSize;
                                displayLines.Add(l);
                            }
                            displayLines.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "Lyrics provided by Apple Music", FontSize = _lyricFontSize * 0.65 });
                            displayLines.Add(new LyricLine { Time = TimeSpan.FromHours(2), Text = "", FontSize = _lyricFontSize });

                            // Cache for later (LRU)
                            if (_lyricsCache.ContainsKey(cacheKey))
                            {
                                _lyricsCacheOrder.Remove(cacheKey);
                            }
                            else if (_lyricsCacheOrder.Count >= MAX_LYRICS_CACHE)
                            {
                                string oldest = _lyricsCacheOrder[0];
                                _lyricsCacheOrder.RemoveAt(0);
                                _lyricsCache.Remove(oldest);
                            }
                            _lyricsCacheOrder.Add(cacheKey);
                            _lyricsCache[cacheKey] = new LyricsCacheEntry { Synced = syncedLyrics, Plain = plainLyrics, Lines = displayLines };

                            DisplayLoadedLyrics(displayLines);
                            LyricsLoadingBar.Visibility = Visibility.Collapsed;
                            return;
                        }
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(syncedLyrics) && string.IsNullOrWhiteSpace(plainLyrics))
                {
                // Fire search requests with cancellation support
                string url1 = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) + "&artist_name=" + Uri.EscapeDataString(cleanArtist);
                string url2 = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(cleanTitle + " " + cleanArtist);
                var searchTask1 = SafeGetStringWithTokenAsync(_apiClient, url1, token);
                var searchTask2 = SafeGetStringWithTokenAsync(_apiClient, url2, token);

                // Quick duration poll (max 500ms) — in parallel with searches
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try { trackDurationSec = _appMediaPlayer.NaturalDuration.TotalSeconds; } catch { }
                    if (trackDurationSec > 10) break;
                    await Task.Delay(100);
                    token.ThrowIfCancellationRequested();
                }

                // Fire /api/get with duration in parallel too
                Task<string> getTask = null;
                if (trackDurationSec > 10)
                {
                    string getUrl = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(cleanTitle)
                        + "&artist_name=" + Uri.EscapeDataString(cleanArtist)
                        + "&duration=" + ((int)Math.Round(trackDurationSec)).ToString();
                    getTask = SafeGetStringWithTokenAsync(_apiClient, getUrl, token);
                }

                // LAYER 0: /api/get with exact duration (best, fastest)
                if (getTask != null)
                {
                    try
                    {
                        var getResp = await getTask;
                        token.ThrowIfCancellationRequested();
                        if (!string.IsNullOrEmpty(getResp))
                        {
                            var getJson = JObject.Parse(getResp);
                            syncedLyrics = getJson["syncedLyrics"]?.ToString();
                            plainLyrics = getJson["plainLyrics"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(syncedLyrics)) syncedLyrics += "\n[99:99.99] Lyrics provided by LRCLIB";
                            if (!string.IsNullOrWhiteSpace(plainLyrics)) plainLyrics += "\n\nLyrics provided by LRCLIB";
                        }
                    }
                    catch { }
                }

                // LAYER 1: Use search results (already running in parallel)
                if (string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    try
                    {
                        var resp1 = await searchTask1;
                        token.ThrowIfCancellationRequested();
                        if (!string.IsNullOrEmpty(resp1))
                        {
                            var arr1 = JArray.Parse(resp1);
                            if (arr1.Count > 0)
                            {
                                var match1 = pickBestMatch(arr1, trackDurationSec);
                                syncedLyrics = match1[0] + "\n[99:99.99] Lyrics provided by LRCLIB";
                                plainLyrics = match1[1] + "\n\nLyrics provided by LRCLIB";
                            }
                        }
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(syncedLyrics))
                    {
                        try
                        {
                            var resp2 = await searchTask2;
                            token.ThrowIfCancellationRequested();
                            if (!string.IsNullOrEmpty(resp2))
                            {
                                var arr2 = JArray.Parse(resp2);
                                if (arr2.Count > 0)
                                {
                                    var match2 = pickBestMatch(arr2, trackDurationSec);
                                    if (!string.IsNullOrWhiteSpace(match2[0])) syncedLyrics = match2[0] + "\n[99:99.99] Lyrics provided by LRCLIB";
                                    if (string.IsNullOrWhiteSpace(plainLyrics)) plainLyrics = match2[1] + "\n\nLyrics provided by LRCLIB";
                                }
                            }
                        }
                        catch { }
                    }
                }

                }
                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(syncedLyrics) || !string.IsNullOrWhiteSpace(plainLyrics))
                {
                    // Cache for later (LRU)
                    if (_lyricsCache.ContainsKey(cacheKey))
                    {
                        _lyricsCacheOrder.Remove(cacheKey);
                    }
                    else if (_lyricsCacheOrder.Count >= MAX_LYRICS_CACHE)
                    {
                        string oldest = _lyricsCacheOrder[0];
                        _lyricsCacheOrder.RemoveAt(0);
                        _lyricsCache.Remove(oldest);
                    }
                    _lyricsCacheOrder.Add(cacheKey);
                    _lyricsCache[cacheKey] = new LyricsCacheEntry { Synced = syncedLyrics, Plain = plainLyrics };

                    if (!string.IsNullOrWhiteSpace(syncedLyrics))
                    {
                        ParseAndDisplaySyncedLyrics(syncedLyrics);
                    }
                    else
                    {
                        LyricsFallbackText.Text = plainLyrics;
                        LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                        LyricsListView.Visibility = Visibility.Collapsed;
                    }
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
                                    UpdateLyricsVisualState();
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
                    TimeSpan parsedTime;
                    if (TryParseLrcTime(timeStr, out parsedTime))
                    {
                        times.Add(parsedTime);
                    }
                    tempLine = tempLine.Substring(bracketEnd + 1).Trim();
                }

                if (times.Count > 0)
                {
                    string text = string.IsNullOrWhiteSpace(tempLine) ? "♪" : tempLine;
                    foreach (var t in times)
                    {
                        double fSize = text.StartsWith("Lyrics provided by") ? _lyricFontSize * 0.65 : _lyricFontSize;
                        string linePlainText = text;
                        List<LyricWord> lineWords = null;
                        if (text.IndexOf('<') >= 0 && text.IndexOf('>') >= 0)
                        {
                            if (Services.LrcParser.TryParseEnhancedWords(text, t, out linePlainText, out lineWords))
                            {
                                text = linePlainText;
                            }
                        }
                        var lyricLine = new LyricLine { Time = t, Text = linePlainText, FontSize = fSize };
                        if (lineWords != null && lineWords.Count > 0)
                        {
                            lyricLine.Words = lineWords;
                        }
                        parsedLines.Add(lyricLine);
                    }
                }
            }

            parsedLines.Sort((a, b) => a.Time.CompareTo(b.Time));
            parsedLines.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "", FontSize = _lyricFontSize });

            DisplayLoadedLyrics(parsedLines);
        }

        private void DisplayLoadedLyrics(List<LyricLine> lines)
        {
            if (LyricsListView != null) LyricsListView.ItemsSource = null;
            if (FullscreenLyricsListView != null) FullscreenLyricsListView.ItemsSource = null;

            currentLyrics.Clear();
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    currentLyrics.Add(lines[i]);
                }
            }

            if (LyricsListView != null) LyricsListView.ItemsSource = currentLyrics;
            if (FullscreenLyricsListView != null) FullscreenLyricsListView.ItemsSource = currentLyrics;

            UpdateLyricsVisualState();
            UpdateWordTimerState();
        }

        private async void LyricsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var line = e.ClickedItem as LyricLine;
            if (line == null || line.Time >= TimeSpan.FromHours(1)) return;

            try
            {
                if (_appMediaPlayer == null || _appMediaPlayer.CurrentState == MediaPlayerState.Closed)
                    return;

                var mgr = YTMusicWP.Services.ListenTogether.ListenTogetherManager.Instance;
                if (mgr.InRoom && !mgr.IsHost)
                {
                    // In Listen Together, only the host can control playback position
                    return;
                }

                int targetIndex = currentLyrics.IndexOf(line);
                if (targetIndex < 0 || targetIndex == currentLyricIndex) return;

                // Suppress timer ticks from reverting UI while IPC seek settles
                _lastSeekTarget = line.Time;
                _lastSeekTimestamp = DateTime.UtcNow;

                _appMediaPlayer.Position = line.Time;
                if (_appMediaPlayer.CurrentState == MediaPlayerState.Paused)
                {
                    _appMediaPlayer.Play();
                    OnPlayPauseChangedAsHost(true);
                }
                OnSeekOccurredAsHost((long)line.Time.TotalMilliseconds);

                // Immediate UI feedback for sliders & time
                double sec = line.Time.TotalSeconds;
                if (MusicSlider != null) MusicSlider.Value = sec;
                if (AppleMusicSlider != null) AppleMusicSlider.Value = sec;
                if (MiniProgressBar != null) MiniProgressBar.Value = sec;

                string curText = line.Time.ToString(@"m\:ss");
                if (CurrentTimeText != null) CurrentTimeText.Text = curText;
                if (AppleMusicCurrentTime != null) AppleMusicCurrentTime.Text = curText;

                double totalSec = 0;
                try { totalSec = _appMediaPlayer.NaturalDuration.TotalSeconds; } catch { }
                if (AppleMusicRemainingTime != null && totalSec > 0)
                {
                    var remain = Math.Max(0, totalSec - sec);
                    AppleMusicRemainingTime.Text = "-" + string.Format("{0}:{1:D2}", (int)remain / 60, (int)remain % 60);
                }

                // Immediately highlight the clicked lyric text
                int oldIndex = currentLyricIndex;
                currentLyricIndex = targetIndex;
                ResetWordInlines(oldIndex, false);
                UpdateLyricsVisualState(oldIndex);

                // Yield to allow the tap/pointer-up interaction on the ListView to fully finish,
                // so DirectManipulation doesn't abort the smooth scroll animation.
                await Task.Delay(30);
                if (currentLyricIndex != targetIndex) return;

                ForceUpdateLyricUI(oldIndex);
                UpdateActiveLineWordProgress(line.Time, false);
            }
            catch { }
        }
        private void LyricsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer == null) return;

            if (_isAppleMusicStyle)
            {
                args.ItemContainer.RenderTransform = null;
                args.ItemContainer.Opacity = 1.0;
            }
            else
            {
                // Spotify: scale + dim
                args.ItemContainer.Opacity = (args.ItemIndex == currentLyricIndex) ? 1.0 : 0.5;
                var st = args.ItemContainer.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                if (st == null)
                {
                    st = new Windows.UI.Xaml.Media.ScaleTransform();
                    args.ItemContainer.RenderTransformOrigin = new Point(0, 0.5);
                    args.ItemContainer.RenderTransform = st;
                }
                double targetScale = (args.ItemIndex == currentLyricIndex) ? 1.0 : 0.85;
                st.ScaleX = targetScale;
                st.ScaleY = targetScale;
            }

            var sharpText = FindChildByName(args.ItemContainer, "LyricSharpText") as TextBlock;
            if (sharpText != null)
            {
                var line = args.Item as LyricLine;
                if (args.ItemIndex != currentLyricIndex)
                {
                    if (sharpText.Inlines.Count > 0)
                    {
                        sharpText.Inlines.Clear();
                        if (line != null) sharpText.Text = line.Text ?? "";
                    }
                }
                else if (line != null && line.HasWords)
                {
                    TimeSpan pos = TimeSpan.Zero;
                    try { if (_appMediaPlayer != null) pos = _appMediaPlayer.Position; } catch { }
                    SetupWordInlines(sharpText, line, pos);
                }
            }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // FULLSCREEN LYRICS
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
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
                if (FullscreenLyricsGradientMid != null && NowPlayingGradientMid != null)
                {
                    FullscreenLyricsGradientMid.Color = NowPlayingGradientMid.Color;
                }
            }
            catch { }

            // Bind same lyrics data
            FullscreenLyricsListView.ItemsSource = currentLyrics;

            // Show with fade-in
            FullscreenLyricsView.Visibility = Visibility.Visible;
            UpdateStatusBarColor(true, animate: true, durationMs: 300);
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
                // Center-scroll to current lyric
                if (currentLyricIndex >= 0 && currentLyricIndex < currentLyrics.Count)
                {
                    FullscreenLyricsListView.ScrollIntoView(currentLyrics[currentLyricIndex]);
                    FullscreenLyricsListView.UpdateLayout();

                    if (_cachedFullscreenLyricsScrollViewer == null)
                        _cachedFullscreenLyricsScrollViewer = GetScrollViewer(FullscreenLyricsListView);
                    var activeContainer = FullscreenLyricsListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
                    if (_cachedFullscreenLyricsScrollViewer != null && activeContainer != null)
                    {
                        var transform = activeContainer.TransformToVisual(_cachedFullscreenLyricsScrollViewer);
                        var lyricPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                        double targetOff = _cachedFullscreenLyricsScrollViewer.VerticalOffset + lyricPos.Y
                                        - (_cachedFullscreenLyricsScrollViewer.ViewportHeight / 2.0)
                                        + (activeContainer.ActualHeight / 2.0);
                        _cachedFullscreenLyricsScrollViewer.ChangeView(null, targetOff, null, false);
                    }
                    UpdateActiveLineWordProgress(_appMediaPlayer != null ? _appMediaPlayer.Position : TimeSpan.Zero, true);
                }
                UpdateWordTimerState();
            };
            fadeIn.Begin();
        }

        private void CloseFullscreenLyrics_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            bool npOpen = (NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible);
            if (!npOpen)
            {
                UpdateStatusBarColor(false, animate: true, durationMs: 200);
            }

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
                UpdateStatusBarColor(npOpen, animate: false);
                // Refresh regular lyrics containers to match current sync state
                RefreshRegularLyricsContainers();
                UpdateWordTimerState();
            };
            fadeOut.Begin();
        }

        private static async Task<string> SafeGetStringWithTokenAsync(System.Net.Http.HttpClient client, string url, CancellationToken token)
        {
            try
            {
                using (var resp = await client.GetAsync(url, token))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    return await resp.Content.ReadAsStringAsync();
                }
            }
            catch { return null; }
        }

        private void UpdateLyricsVisualState(int oldIndex = -1)
        {
            if (currentLyrics == null || currentLyrics.Count == 0) return;

            if (_isAppleMusicStyle)
            {
                int startIdx = 0;
                int endIdx = currentLyrics.Count - 1;
                if (oldIndex >= 0)
                {
                    int minCandidate = (currentLyricIndex >= 0) ? Math.Min(oldIndex, currentLyricIndex) : oldIndex;
                    int maxCandidate = (currentLyricIndex >= 0) ? Math.Max(oldIndex, currentLyricIndex) : oldIndex;
                    startIdx = Math.Max(0, minCandidate - 3);
                    endIdx = Math.Min(currentLyrics.Count - 1, maxCandidate + 3);
                }

                for (int i = startIdx; i <= endIdx; i++)
                {

                    if (currentLyricIndex < 0)
                    {
                        currentLyrics[i].Opacity = 0.50;
                        currentLyrics[i].BlurOpacity = 0.0;
                        currentLyrics[i].FarBlurOpacity = 0.0;
                        currentLyrics[i].ColorBrush = _lyricActiveBrush;
                    }
                    else if (i == currentLyricIndex)
                    {
                        currentLyrics[i].Opacity = 1.0;
                        currentLyrics[i].BlurOpacity = 0.0;
                        currentLyrics[i].FarBlurOpacity = 0.0;
                        currentLyrics[i].ColorBrush = _lyricActiveBrush;
                    }
                    else
                    {
                        int dist = Math.Abs(i - currentLyricIndex);
                        bool isPast = i < currentLyricIndex;

                        double coreOp;
                        double nearBlur;
                        double farBlur;

                        if (dist == 1)
                        {
                            if (!isPast)
                            {
                                // Next line: soft glow with visible dispersion (~0.58 total visual weight)
                                coreOp = 0.16;
                                nearBlur = 0.070;
                                farBlur = 0.035;
                            }
                            else
                            {
                                // Just-sung line (~0.42 total visual weight)
                                coreOp = 0.12;
                                nearBlur = 0.050;
                                farBlur = 0.025;
                            }
                        }
                        else if (dist == 2)
                        {
                            if (!isPast)
                            {
                                // 2 lines away: foggy background dispersion (~0.38 total visual weight)
                                coreOp = 0.08;
                                nearBlur = 0.050;
                                farBlur = 0.025;
                            }
                            else
                            {
                                coreOp = 0.06;
                                nearBlur = 0.035;
                                farBlur = 0.018;
                            }
                        }
                        else
                        {
                            // 3+ lines away: faint dreamlike ambient text (~0.16 total visual weight)
                            if (!isPast)
                            {
                                coreOp = 0.04;
                                nearBlur = 0.020;
                                farBlur = 0.010;
                            }
                            else
                            {
                                coreOp = 0.03;
                                nearBlur = 0.015;
                                farBlur = 0.008;
                            }
                        }

                        currentLyrics[i].Opacity = coreOp;
                        currentLyrics[i].BlurOpacity = nearBlur;
                        currentLyrics[i].FarBlurOpacity = farBlur;
                        currentLyrics[i].ColorBrush = _lyricActiveBrush;
                    }
                }
            }
            else
            {
                if (oldIndex >= 0 && Math.Abs(currentLyricIndex - oldIndex) == 1)
                {
                    if (oldIndex < currentLyrics.Count)
                    {
                        currentLyrics[oldIndex].BlurOpacity = 0.0;
                        currentLyrics[oldIndex].FarBlurOpacity = 0.0;
                        currentLyrics[oldIndex].Opacity = 1.0;
                        currentLyrics[oldIndex].ColorBrush = _lyricInactiveBrush;
                    }
                    if (currentLyricIndex >= 0 && currentLyricIndex < currentLyrics.Count)
                    {
                        currentLyrics[currentLyricIndex].BlurOpacity = 0.0;
                        currentLyrics[currentLyricIndex].FarBlurOpacity = 0.0;
                        currentLyrics[currentLyricIndex].Opacity = 1.0;
                        currentLyrics[currentLyricIndex].ColorBrush = _lyricActiveBrush;
                    }
                }
                else
                {
                    for (int i = 0; i < currentLyrics.Count; i++)
                    {
                        currentLyrics[i].BlurOpacity = 0.0;
                        currentLyrics[i].FarBlurOpacity = 0.0;
                        currentLyrics[i].Opacity = 1.0;
                        if (i == currentLyricIndex)
                        {
                            currentLyrics[i].ColorBrush = _lyricActiveBrush;
                        }
                        else
                        {
                            currentLyrics[i].ColorBrush = _lyricInactiveBrush;
                        }
                    }
                }
            }
        }

        private void RefreshRegularLyricsContainers()
        {
            if (currentLyrics == null || currentLyrics.Count == 0) return;

            UpdateLyricsVisualState();

            if (_isAppleMusicStyle)
            {
                if (_lyricInSb != null) _lyricInSb.Stop();
                if (_lyricOutSb != null) _lyricOutSb.Stop();
                _lastInContainer = null;
                _lastInScale = null;
                _lastOutContainer = null;
                _lastOutScale = null;

                for (int i = 0; i < currentLyrics.Count; i++)
                {
                    var container = LyricsListView.ContainerFromIndex(i) as FrameworkElement;
                    if (container == null) continue;
                    container.RenderTransform = null;
                    container.Opacity = 1.0;
                }
            }
            else
            {
                for (int i = 0; i < currentLyrics.Count; i++)
                {
                    var container = LyricsListView.ContainerFromIndex(i) as FrameworkElement;
                    if (container == null) continue;
                    if (i == currentLyricIndex)
                    {
                        container.Opacity = 1.0;
                        var st = container.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                        if (st == null)
                        {
                            st = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
                            container.RenderTransformOrigin = new Point(0, 0.5);
                            container.RenderTransform = st;
                        }
                        else
                        {
                            st.ScaleX = 1.0; st.ScaleY = 1.0;
                        }
                    }
                    else
                    {
                        container.Opacity = 0.5;
                        var st = container.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
                        if (st == null)
                        {
                            st = new Windows.UI.Xaml.Media.ScaleTransform { ScaleX = 0.85, ScaleY = 0.85 };
                            container.RenderTransformOrigin = new Point(0, 0.5);
                            container.RenderTransform = st;
                        }
                        else
                        {
                            st.ScaleX = 0.85; st.ScaleY = 0.85;
                        }
                    }
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
                UpdateActiveLineWordProgress(_appMediaPlayer != null ? _appMediaPlayer.Position : TimeSpan.Zero, false);
            }

            for (int i = 0; i < currentLyrics.Count; i++)
            {
                if (i == currentLyricIndex) continue;
                var c = LyricsListView.ContainerFromIndex(i) as FrameworkElement;
                if (c != null)
                {
                    var st = FindChildByName(c, "LyricSharpText") as TextBlock;
                    if (st != null && st.Inlines.Count > 0)
                    {
                        st.Inlines.Clear();
                        st.Text = currentLyrics[i].Text ?? "";
                    }
                }
            }
        }

        private void FullscreenLyricsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer != null)
            {
                // Set all non-active lines to dim, active line to bright
                args.ItemContainer.Opacity = (args.ItemIndex == currentLyricIndex) ? 1.0 : 0.5;

                var sharpText = FindChildByName(args.ItemContainer, "FullscreenLyricSharpText") as TextBlock;
                if (sharpText != null)
                {
                    var line = args.Item as LyricLine;
                    if (args.ItemIndex != currentLyricIndex)
                    {
                        if (sharpText.Inlines.Count > 0)
                        {
                            sharpText.Inlines.Clear();
                            if (line != null) sharpText.Text = line.Text ?? "";
                        }
                    }
                    else if (line != null && line.HasWords)
                    {
                        TimeSpan pos = TimeSpan.Zero;
                        try { if (_appMediaPlayer != null) pos = _appMediaPlayer.Position; } catch { }
                        SetupWordInlines(sharpText, line, pos);
                    }
                }
            }
        }

        // ── Word-by-Word Lyrics Synchronization Engine (SimpMusic v2.2.0 Parity) ──
        public void UpdateWordTimerState()
        {
            bool isFs = FullscreenLyricsView != null && FullscreenLyricsView.Visibility == Visibility.Visible;
            bool isNpLyrics = NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible
                           && NowPlayingPivot != null && NowPlayingPivot.SelectedIndex == 1
                           && LyricsListView != null && LyricsListView.Visibility == Visibility.Visible;

            bool isVisible = isFs || isNpLyrics;
            bool isPlaying = false;
            try
            {
                if (_appMediaPlayer != null)
                {
                    var st = _appMediaPlayer.CurrentState;
                    isPlaying = (st == MediaPlayerState.Playing || st == MediaPlayerState.Buffering || st == MediaPlayerState.Opening);
                }
            }
            catch { }

            if (isVisible && isPlaying && currentLyrics != null && currentLyrics.Count > 0)
            {
                if (_lyricsWordTimer == null)
                {
                    _lyricsWordTimer = new DispatcherTimer();
                    _lyricsWordTimer.Interval = TimeSpan.FromMilliseconds(80);
                    _lyricsWordTimer.Tick += LyricsWordTimer_Tick;
                }
                if (!_lyricsWordTimer.IsEnabled)
                {
                    _lyricsWordTimer.Start();
                }
            }
            else
            {
                if (_lyricsWordTimer != null && _lyricsWordTimer.IsEnabled)
                {
                    _lyricsWordTimer.Stop();
                }
            }
        }

        private void LyricsWordTimer_Tick(object sender, object e)
        {
            try
            {
                if (_isSliderManipulating) return;
                if (_appMediaPlayer == null) return;
                if (_appMediaPlayer.CurrentState != MediaPlayerState.Playing)
                {
                    UpdateWordTimerState();
                    return;
                }

                TimeSpan pos = _appMediaPlayer.Position;

                // Guard against stale position reads right after a seek
                if (_lastSeekTimestamp != DateTime.MinValue)
                {
                    if ((DateTime.UtcNow - _lastSeekTimestamp).TotalMilliseconds < 1500)
                    {
                        if (Math.Abs((pos - _lastSeekTarget).TotalSeconds) > 1.5)
                        {
                            return;
                        }
                        else
                        {
                            _lastSeekTimestamp = DateTime.MinValue;
                        }
                    }
                    else
                    {
                        _lastSeekTimestamp = DateTime.MinValue;
                    }
                }

                if (currentLyrics == null || currentLyrics.Count == 0) return;

                bool isFs = FullscreenLyricsView != null && FullscreenLyricsView.Visibility == Visibility.Visible;
                bool isNpLyrics = NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible
                               && NowPlayingPivot != null && NowPlayingPivot.SelectedIndex == 1
                               && LyricsListView != null && LyricsListView.Visibility == Visibility.Visible;

                if (!isFs && !isNpLyrics)
                {
                    UpdateWordTimerState();
                    return;
                }

                int newIndex = -1;
                for (int i = 0; i < currentLyrics.Count; i++)
                {
                    if (pos >= currentLyrics[i].Time.Subtract(TimeSpan.FromSeconds(0.2))) newIndex = i;
                    else break;
                }

                if (newIndex != currentLyricIndex && newIndex >= 0)
                {
                    int oldIndex = currentLyricIndex;
                    currentLyricIndex = newIndex;

                    ResetWordInlines(oldIndex, isFs);
                    UpdateLyricsVisualState(oldIndex);
                    ForceUpdateLyricUI(oldIndex);
                    UpdateActiveLineWordProgress(pos, isFs);
                }
                else if (currentLyricIndex >= 0 && currentLyricIndex < currentLyrics.Count)
                {
                    UpdateActiveLineWordProgress(pos, isFs);
                }
            }
            catch { }
        }

        private void SetupWordInlines(TextBlock tb, LyricLine line, TimeSpan pos)
        {
            if (tb == null || line == null || line.Words == null) return;
            tb.Text = "";
            tb.Inlines.Clear();
            for (int i = 0; i < line.Words.Count; i++)
            {
                var w = line.Words[i];
                bool isSung = pos >= w.StartTime;
                var run = new Run
                {
                    Text = w.Text ?? "",
                    Foreground = isSung ? _lyricActiveBrush : _lyricPendingBrush
                };
                tb.Inlines.Add(run);
            }
        }

        private void UpdateWordInlines(TextBlock tb, LyricLine line, TimeSpan pos)
        {
            if (tb == null || line == null || line.Words == null) return;
            for (int i = 0; i < line.Words.Count; i++)
            {
                var w = line.Words[i];
                var run = tb.Inlines[i] as Run;
                if (run != null)
                {
                    bool isSung = pos >= w.StartTime;
                    var targetBrush = isSung ? _lyricActiveBrush : _lyricPendingBrush;
                    if (!object.ReferenceEquals(run.Foreground, targetBrush))
                    {
                        run.Foreground = targetBrush;
                    }
                }
            }
        }

        public void ResetWordInlines(int index, bool isFullscreen)
        {
            if (currentLyrics == null || index < 0 || index >= currentLyrics.Count) return;

            var targetListView = isFullscreen ? FullscreenLyricsListView : LyricsListView;
            if (targetListView == null) return;

            var container = targetListView.ContainerFromIndex(index) as FrameworkElement;
            if (container == null) return;

            string sharpName = isFullscreen ? "FullscreenLyricSharpText" : "LyricSharpText";
            var sharpText = FindChildByName(container, sharpName) as TextBlock;
            if (sharpText != null && sharpText.Inlines.Count > 0)
            {
                sharpText.Inlines.Clear();
                sharpText.Text = currentLyrics[index].Text ?? "";
            }
        }

        public void UpdateActiveLineWordProgress(TimeSpan pos, bool isFullscreen)
        {
            if (currentLyrics == null || currentLyricIndex < 0 || currentLyricIndex >= currentLyrics.Count) return;

            var line = currentLyrics[currentLyricIndex];
            if (!line.HasWords || line.Words == null || line.Words.Count == 0) return;

            var targetListView = isFullscreen ? FullscreenLyricsListView : LyricsListView;
            if (targetListView == null) return;

            var container = targetListView.ContainerFromIndex(currentLyricIndex) as FrameworkElement;
            if (container == null) return;

            string sharpName = isFullscreen ? "FullscreenLyricSharpText" : "LyricSharpText";
            var sharpText = FindChildByName(container, sharpName) as TextBlock;
            if (sharpText == null) return;

            if (sharpText.Inlines.Count != line.Words.Count)
            {
                SetupWordInlines(sharpText, line, pos);
            }
            else
            {
                UpdateWordInlines(sharpText, line, pos);
            }
        }

    }
}





