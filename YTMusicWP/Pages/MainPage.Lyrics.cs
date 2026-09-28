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
        // ── Word-by-Word Synchronized Lyrics Brushes (Apple Music & SimpMusic v2.2.0 Parity) ──
        // Pending: future words/characters waiting to be sung (~36% opacity)
        private static readonly SolidColorBrush _lyricPendingBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(92, 255, 255, 255));
        // Flare Head: character immediately ahead of the travelling light (~55% opacity)
        private static readonly SolidColorBrush _lyricFlareHeadBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 255, 255, 255));
        // Travelling Spotlight: the exact character being sung right now (100% pure blazing white flare)
        private static readonly SolidColorBrush _lyricSpotlightBrush = new SolidColorBrush(Windows.UI.Colors.White);
        // Flare Tail: character that just finished being sung (~92% opacity wake)
        private static readonly SolidColorBrush _lyricFlareTailBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 255, 255, 255));
        // Past: words/characters already sung and settled (~78% clean soft white)
        private static readonly SolidColorBrush _lyricPastBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 255, 255, 255));

        // Interlude Dots • • •:
        private static readonly SolidColorBrush _lyricDotActiveBrush = new SolidColorBrush(Windows.UI.Colors.White);
        private static readonly SolidColorBrush _lyricDotPastBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 255, 255));
        private static readonly SolidColorBrush _lyricDotPendingBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255));

        private struct LyricCharInfo
        {
            public TimeSpan StartTime;
            public TimeSpan EndTime;
            public int WordIndex;
        }


        // ── Spicy-Lyrics-style per-word animation overlay ──
        // Run trong TextBlock không có transform riêng, nên khi một dòng word-by-word được hát, ta đo vị trí thật
        // của từng từ (TextPointer.GetCharacterRect) rồi phủ lên đúng chỗ đó mỗi từ một TextBlock riêng:
        //   • từ:        gradient trắng quét mượt (mép mềm 20%), phóng 1.00 → 1.045 → 1.00 + nhấc rất nhẹ, dừng kiểu lò xo
        //   • dấu chấm:  nghỉ ở 0.75 / 35% → tới lượt thì nảy lên 1.05 + nhấc + sáng dần; cuối đoạn cả nhóm thu lại rồi biến mất
        // Toàn bộ chạy bằng Storyboard (60fps), timer 60ms chỉ quyết định khi nào từ nào bắt đầu.
        private static readonly Windows.UI.Color _wordDimColor = Windows.UI.Color.FromArgb(92, 255, 255, 255);
        private static readonly SolidColorBrush _wordHiddenBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);
        private static readonly SolidColorBrush _dotBrush = new SolidColorBrush(Windows.UI.Colors.White);

        private sealed class WordVisual
        {
            public TextBlock Text;
            public CompositeTransform Transform;
            public GradientStop LitStop, DimStop;           // null với dấu chấm
            public Windows.UI.Xaml.Media.Animation.Storyboard Anim;
            public TimeSpan Start, End;
            public int State = -1;                           // 0 = chưa hát, 1 = đang hát, 2 = đã hát
        }

        private sealed class WordOverlayState
        {
            public int LineIndex = -1;
            public bool Pending;                             // đã dựng run từng ký tự, chờ layout để đo vị trí từ
            public readonly List<LyricCharInfo> Chars = new List<LyricCharInfo>(128);
            public TextBlock Sharp;
            public Canvas Layer;
            public bool IsInterlude;
            public double BuiltWidth, BuiltFontSize;
            public TimeSpan LineEnd;
            public Windows.UI.Xaml.Media.Animation.Storyboard GroupExit; // chỉ dùng cho dấu chấm
            public bool GroupExitStarted;
            public readonly List<WordVisual> Words = new List<WordVisual>(16);
        }

        // Nhiều dòng chạy song song: dòng hiện tại + dòng vừa qua đang chạy nốt chữ cuối + dòng hát chồng (song ca/bè)
        private readonly List<WordOverlayState> _regularOverlays = new List<WordOverlayState>(3);
        private readonly List<WordOverlayState> _fsOverlays = new List<WordOverlayState>(3);
        private readonly List<int> _liveLineScratch = new List<int>(4);
        private readonly Dictionary<Canvas, Windows.UI.Xaml.Media.Animation.Storyboard> _retiringLayers =
            new Dictionary<Canvas, Windows.UI.Xaml.Media.Animation.Storyboard>();
        private const double RetireFadeMs = 280;

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
            ClearLitLines();
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

                // Lưu vào LRU cache
                Action<string, string, List<LyricLine>> cacheLyrics = (synced, plain, lines) =>
                {
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
                    _lyricsCache[cacheKey] = new LyricsCacheEntry { Synced = synced, Plain = plain, Lines = lines };
                };

                // --- APPLE MUSIC LYRICS (TTML) ---
                AppleMusicLyricsResult amResult = null;
                List<LyricLine> amLineSyncedLines = null; // Apple Music chỉ có đồng bộ theo dòng → để dành, ưu tiên thử KuGou word-by-word trước
                int durSecs = 0;
                double knownDuration = KnownDurationSeconds(title);
                try
                {
                    // The Apple Music lyrics service only answers songs it has cached, and its cache is keyed by duration
                    // (±2 s): without the right duration it returns 401 and the word-by-word lyrics are lost
                    if (knownDuration > 10) durSecs = (int)Math.Round(knownDuration);
                    else durSecs = await WaitForTrackDurationAsync(currentTrack != null && currentTrack.Title == title ? currentTrack.VideoId : null, token);
                    if (token.IsCancellationRequested) return;

                    amResult = await YTMusicWP.Services.AppleMusicLyricsApi.GetLyricsResultAsync(cleanTitle, cleanArtist, durSecs);
                    if (token.IsCancellationRequested) return;
                    if (amResult == null)
                    {
                        // Its cache also keys the artist text: Apple joins several artists with ", " where YouTube Music
                        // shows "A & B" (a duet like "Stay" is only found as "The Kid LAROI, Justin Bieber")
                        var artists = SplitArtistNames(cleanArtist);
                        string appleArtists = string.Join(", ", artists);
                        if (artists.Count > 1 && !string.Equals(appleArtists, cleanArtist, StringComparison.OrdinalIgnoreCase))
                        {
                            amResult = await YTMusicWP.Services.AppleMusicLyricsApi.GetLyricsResultAsync(cleanTitle, appleArtists, durSecs);
                            if (token.IsCancellationRequested) return;
                        }
                    }
                    if (amResult == null && durSecs > 10)
                    {
                        // A list duration can be a few seconds off the cached one: try either side once
                        amResult = await YTMusicWP.Services.AppleMusicLyricsApi.GetLyricsResultAsync(cleanTitle, cleanArtist, durSecs + 3);
                        if (token.IsCancellationRequested) return;
                        if (amResult == null)
                            amResult = await YTMusicWP.Services.AppleMusicLyricsApi.GetLyricsResultAsync(cleanTitle, cleanArtist, durSecs - 3);
                    }
                    // The song changed while this request ran: its lyrics must not land on the new song
                    if (token.IsCancellationRequested) return;
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
                                l.FontSize = l.IsInterlude ? (_lyricFontSize * InterludeFontScale) : _lyricFontSize;
                                displayLines.Add(l);
                            }
                            displayLines.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "Lyrics provided by Apple Music", FontSize = _lyricFontSize * 0.65 });
                            displayLines.Add(new LyricLine { Time = TimeSpan.FromHours(2), Text = "", FontSize = _lyricFontSize });

                            if (amResult.HasWordSync)
                            {
                                cacheLyrics(syncedLyrics, plainLyrics, displayLines);
                                DisplayLoadedLyrics(displayLines);
                                LyricsLoadingBar.Visibility = Visibility.Collapsed;
                                return;
                            }
                            amLineSyncedLines = displayLines;
                        }
                    }
                }
                catch { }

                // --- KUGOU (KRC word-by-word) — khi Apple Music không có lyrics theo từ ---
                KuGouLyricsApi.Result kgResult = null;
                try
                {
                    kgResult = await KuGouLyricsApi.GetLyricsAsync(cleanTitle, cleanArtist, durSecs, token);
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                token.ThrowIfCancellationRequested();

                if (kgResult != null && kgResult.HasWordSync)
                {
                    System.Diagnostics.Debug.WriteLine("Fetched word-synced lyrics from KuGou (KRC)");
                    string kgSynced = kgResult.Lrc + "\n[99:99.99] Lyrics provided by KuGou";
                    cacheLyrics(kgSynced, null, null);
                    ParseAndDisplaySyncedLyrics(kgSynced);
                    LyricsLoadingBar.Visibility = Visibility.Collapsed;
                    return;
                }

                if (amLineSyncedLines != null)
                {
                    cacheLyrics(syncedLyrics, plainLyrics, amLineSyncedLines);
                    DisplayLoadedLyrics(amLineSyncedLines);
                    LyricsLoadingBar.Visibility = Visibility.Collapsed;
                    return;
                }

                if (string.IsNullOrWhiteSpace(syncedLyrics) && string.IsNullOrWhiteSpace(plainLyrics))
                {
                // Fire search requests with cancellation support
                string url1 = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) + "&artist_name=" + Uri.EscapeDataString(cleanArtist);
                string url2 = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(cleanTitle + " " + cleanArtist);
                var searchTask1 = SafeGetStringWithTokenAsync(_apiClient, url1, token);
                var searchTask2 = SafeGetStringWithTokenAsync(_apiClient, url2, token);

                // Quick duration poll (max 500ms) — in parallel with searches
                if (knownDuration > 10) trackDurationSec = knownDuration;
                else if (durSecs > 10) trackDurationSec = durSecs; // already waited for the real one above
                for (int attempt = 0; attempt < 5 && trackDurationSec <= 10; attempt++)
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
                                // Plain-only matches used to become a "synced" text holding just the credit line
                                if (!string.IsNullOrWhiteSpace(match1[0])) syncedLyrics = match1[0] + "\n[99:99.99] Lyrics provided by LRCLIB";
                                if (!string.IsNullOrWhiteSpace(match1[1])) plainLyrics = match1[1] + "\n\nLyrics provided by LRCLIB";
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
                                    if (string.IsNullOrWhiteSpace(plainLyrics) && !string.IsNullOrWhiteSpace(match2[1])) plainLyrics = match2[1] + "\n\nLyrics provided by LRCLIB";
                                }
                            }
                        }
                        catch { }
                    }
                }

                }
                token.ThrowIfCancellationRequested();

                // KuGou LRC theo dòng: mạnh ở nhạc châu Á, dùng khi LRCLIB không có bản đồng bộ
                if (string.IsNullOrWhiteSpace(syncedLyrics) && kgResult != null && !string.IsNullOrWhiteSpace(kgResult.Lrc))
                {
                    syncedLyrics = kgResult.Lrc + "\n[99:99.99] Lyrics provided by KuGou";
                }

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
                                token.ThrowIfCancellationRequested();
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
                // A cancelled load leaves the bar to the load that replaced it (it would hide it mid-load)
                if (!token.IsCancellationRequested) LyricsLoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// The song's length from its list metadata ("3:45", "1:02:10"), 0 when unknown. Right after a song change the
        /// player still reports the previous song's duration, which made the lyrics lookups match the wrong version.
        /// </summary>
        private double KnownDurationSeconds(string title)
        {
            var track = currentTrack;
            if (track == null || track.Title != title || string.IsNullOrEmpty(track.Duration)) return 0;
            double total = 0;
            foreach (var part in track.Duration.Split(':'))
            {
                int n;
                if (!int.TryParse(part.Trim(), out n) || n < 0) return 0;
                total = total * 60 + n;
            }
            return total;
        }

        /// <summary>
        /// This song's duration in seconds as the player reports it, waiting (up to 6 s) until the player is really on it.
        /// Right after a song change the player still holds the previous song; the audio task records the new song's id in
        /// LocalSettings as it switches, sometimes before it has even resolved the new URL (old song still loaded, paused).
        /// So the id must match and the player must be playing (new source open). A player left paused (app reopened on a
        /// paused song) never plays: after the wait its duration is taken anyway. 0 if nothing usable comes.
        /// </summary>
        private async Task<int> WaitForTrackDurationAsync(string videoId, CancellationToken token)
        {
            const int attempts = 30;
            for (int attempt = 0; attempt <= attempts; attempt++)
            {
                try
                {
                    bool sameTrack = videoId == null;
                    if (!sameTrack)
                    {
                        object stored;
                        sameTrack = Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("CurrentVideoId", out stored)
                                    && string.Equals(stored as string, videoId, StringComparison.Ordinal);
                    }
                    if (sameTrack && _appMediaPlayer != null)
                    {
                        var state = _appMediaPlayer.CurrentState;
                        bool open = state == MediaPlayerState.Playing || state == MediaPlayerState.Buffering;
                        double seconds = _appMediaPlayer.NaturalDuration.TotalSeconds;
                        if (seconds > 10 && (open || attempt == attempts)) return (int)Math.Round(seconds);
                    }
                }
                catch { }
                if (attempt == attempts) break;
                try { await Task.Delay(200, token); }
                catch (OperationCanceledException) { return 0; }
            }
            return 0;
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

            // Interlude dots injection for word-by-word synced lines
            bool anyWords = false;
            for (int i = 0; i < parsedLines.Count; i++)
            {
                if (parsedLines[i].HasWords) { anyWords = true; break; }
            }

            if (anyWords && parsedLines.Count > 0)
            {
                var finalEnhanced = new List<LyricLine>(parsedLines.Count + 4);
                if (parsedLines[0].Time.TotalSeconds >= 5.0)
                {
                    double introStart = 1.0;
                    double introEnd = parsedLines[0].Time.TotalSeconds;
                    double step = (introEnd - introStart) / 3.0;
                    finalEnhanced.Add(new LyricLine
                    {
                        Time = TimeSpan.FromSeconds(introStart),
                        EndTime = parsedLines[0].Time,
                        Text = "• • •",
                        IsInterlude = true,
                        FontSize = _lyricFontSize * InterludeFontScale,
                        Words = new List<LyricWord>
                        {
                            new LyricWord { Text = "• ", StartTime = TimeSpan.FromSeconds(introStart), EndTime = TimeSpan.FromSeconds(introStart + step) },
                            new LyricWord { Text = "• ", StartTime = TimeSpan.FromSeconds(introStart + step), EndTime = TimeSpan.FromSeconds(introStart + step * 2) },
                            new LyricWord { Text = "•",     StartTime = TimeSpan.FromSeconds(introStart + step * 2), EndTime = parsedLines[0].Time }
                        }
                    });
                }
                for (int i = 0; i < parsedLines.Count; i++)
                {
                    var cur = parsedLines[i];
                    finalEnhanced.Add(cur);
                    if (i < parsedLines.Count - 1)
                    {
                        var next = parsedLines[i + 1];
                        double curEnd = cur.EndTime > cur.Time ? cur.EndTime.TotalSeconds : (cur.HasWords ? cur.Words[cur.Words.Count - 1].EndTime.TotalSeconds : cur.Time.TotalSeconds + 3.0);
                        double nextStart = next.Time.TotalSeconds;
                        if (nextStart - curEnd >= 3.0)
                        {
                            double step = (nextStart - curEnd) / 3.0;
                            finalEnhanced.Add(new LyricLine
                            {
                                Time = TimeSpan.FromSeconds(curEnd),
                                EndTime = next.Time,
                                Text = "• • •",
                                IsInterlude = true,
                                FontSize = _lyricFontSize * InterludeFontScale,
                                Words = new List<LyricWord>
                                {
                                    new LyricWord { Text = "• ", StartTime = TimeSpan.FromSeconds(curEnd), EndTime = TimeSpan.FromSeconds(curEnd + step) },
                                    new LyricWord { Text = "• ", StartTime = TimeSpan.FromSeconds(curEnd + step), EndTime = TimeSpan.FromSeconds(curEnd + step * 2) },
                                    new LyricWord { Text = "•",     StartTime = TimeSpan.FromSeconds(curEnd + step * 2), EndTime = next.Time }
                                }
                            });
                        }
                    }
                }
                parsedLines = finalEnhanced;
            }

            parsedLines.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "", FontSize = _lyricFontSize });

            DisplayLoadedLyrics(parsedLines);
        }

        private void DisplayLoadedLyrics(List<LyricLine> lines)
        {
            ResetAllLyricEffects();
            if (LyricsListView != null) LyricsListView.ItemsSource = null;
            if (FullscreenLyricsListView != null) FullscreenLyricsListView.ItemsSource = null;

            currentLyrics.Clear();
            ClearLitLines();
            if (lines != null)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    // Lời bè "(Ah-ah, ah-ah)": nhận diện cho mọi nguồn lyrics, không chỉ TTML của Apple Music
                    if (!l.IsInterlude && !l.IsBackground && IsParenthesizedLine(l.Text))
                    {
                        l.IsBackground = true;
                        l.FontStyle = Windows.UI.Text.FontStyle.Italic;
                    }
                    l.FontSize = GetLyricLineFontSize(l);
                    currentLyrics.Add(l);
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
                // Spotify: scale + dim (every line sung right now stays full size)
                bool lit = IsLyricLineLit(args.ItemIndex);
                args.ItemContainer.Opacity = lit ? 1.0 : 0.5;
                double targetScale = lit ? 1.0 : 0.85;
                var st = EnsureLyricScale(args.ItemContainer, args.ItemIndex, targetScale);
                st.ScaleX = targetScale;
                st.ScaleY = targetScale;
            }

            SyncRecycledContainerVisibility(args);
            ReleaseLyricEffectsInContainer(args.ItemContainer, args.ItemIndex, false);

            // Dòng không có overlay đang sống → trả chữ gốc. Dòng đang hát do bộ quản lý overlay dựng ở tick kế tiếp.
            var sharpText = FindChildByName(args.ItemContainer, "LyricSharpText") as TextBlock;
            if (sharpText != null && !IsSharpInOverlay(sharpText, false) && sharpText.Inlines.Count > 0)
            {
                var line = args.Item as LyricLine;
                sharpText.Inlines.Clear();
                if (line != null) sharpText.Text = line.Text ?? "";
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

            // Show: fade in while settling from a slight zoom
            var lyricsT = Services.MotionHelper.EnsureTransform(FullscreenLyricsView);
            if (FullscreenLyricsView.Visibility != Visibility.Visible)
            {
                FullscreenLyricsView.Opacity = 0;
                lyricsT.ScaleX = 0.96;
                lyricsT.ScaleY = 0.96;
            }
            FullscreenLyricsView.Visibility = Visibility.Visible;
            UpdateStatusBarColor(true, animate: true, durationMs: 300);
            _fullscreenLyricsMotion.Animate(Services.MotionHelper.EnterMs + 50, Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut, () =>
            {
                // Set correct opacity on all containers after layout is ready
                _cachedFullscreenLyricsScrollViewer = null;
                FullscreenLyricsListView.UpdateLayout();
                for (int i = 0; i < currentLyrics.Count; i++)
                {
                    var container = FullscreenLyricsListView.ContainerFromIndex(i) as FrameworkElement;
                    if (container != null)
                        container.Opacity = IsLyricLineLit(i) ? 1.0 : 0.5;
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
            },
            Services.MotionHelper.To(FullscreenLyricsView, "Opacity", 1),
            Services.MotionHelper.To(lyricsT, "ScaleX", 1),
            Services.MotionHelper.To(lyricsT, "ScaleY", 1));
        }

        private void CloseFullscreenLyrics_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            bool npOpen = (NowPlayingView != null && NowPlayingView.Visibility == Visibility.Visible);
            if (!npOpen)
            {
                UpdateStatusBarColor(false, animate: true, durationMs: 200);
            }

            // Fade out while easing back a touch; reopening mid-way reverses instead of hiding the new view
            var lyricsT = Services.MotionHelper.EnsureTransform(FullscreenLyricsView);
            _fullscreenLyricsMotion.Animate(Services.MotionHelper.ExitMs, Windows.UI.Xaml.Media.Animation.EasingMode.EaseIn, () =>
            {
                FullscreenLyricsView.Visibility = Visibility.Collapsed;
                UpdateStatusBarColor(npOpen, animate: false);
                // Refresh regular lyrics containers to match current sync state
                RefreshRegularLyricsContainers();
                UpdateWordTimerState();
            },
            Services.MotionHelper.To(FullscreenLyricsView, "Opacity", 0),
            Services.MotionHelper.To(lyricsT, "ScaleX", 0.98),
            Services.MotionHelper.To(lyricsT, "ScaleY", 0.98));
        }

        private readonly Services.MotionGroup _fullscreenLyricsMotion = new Services.MotionGroup();

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

            // Dấu chấm dạo nhạc: chỉ hiện đúng lúc tới đoạn đó, còn lại thu gọn hẳn.
            // Phải ẩn cả ListViewItem (container) vì container mặc định vẫn giữ chiều cao khi nội dung bị ẩn.
            bool regularChanged = false, fsChanged = false;
            for (int i = 0; i < currentLyrics.Count; i++)
            {
                if (currentLyrics[i].IsInterlude)
                {
                    var vis = (i == currentLyricIndex) ? Visibility.Visible : Visibility.Collapsed;
                    currentLyrics[i].LineVisibility = vis;
                    regularChanged |= SetLyricContainerVisibility(LyricsListView, i, vis);
                    fsChanged |= SetLyricContainerVisibility(FullscreenLyricsListView, i, vis);
                }
            }
            // Tính lại layout ngay: auto-scroll căn giữa và phép đo vị trí từ/chấm ngay sau đó cần kích thước mới
            try
            {
                if (regularChanged && LyricsListView != null) LyricsListView.UpdateLayout();
                if (fsChanged && FullscreenLyricsListView != null) FullscreenLyricsListView.UpdateLayout();
            }
            catch { }

            if (_isAppleMusicStyle)
            {
                int startIdx = 0;
                int endIdx = currentLyrics.Count - 1;
                if (oldIndex >= 0)
                {
                    int minCandidate = (currentLyricIndex >= 0) ? Math.Min(oldIndex, currentLyricIndex) : oldIndex;
                    int maxCandidate = (currentLyricIndex >= 0) ? Math.Max(oldIndex, currentLyricIndex) : oldIndex;
                    // Also the lines that are (or just stopped being) lit together with the current one
                    foreach (var list in new[] { _litLyricLines, _litLyricLinesPrev })
                    {
                        for (int n = 0; n < list.Count; n++)
                        {
                            minCandidate = Math.Min(minCandidate, list[n]);
                            maxCandidate = Math.Max(maxCandidate, list[n]);
                        }
                    }
                    startIdx = Math.Max(0, minCandidate - 3);
                    endIdx = Math.Min(currentLyrics.Count - 1, maxCandidate + 3);
                }

                for (int i = startIdx; i <= endIdx; i++)
                {

                    if (currentLyricIndex < 0)
                    {
                        currentLyrics[i].Opacity = LineOpacity(currentLyrics[i], 0.50);
                        currentLyrics[i].BlurOpacity = 0.0;
                        currentLyrics[i].FarBlurOpacity = 0.0;
                        currentLyrics[i].ColorBrush = _lyricActiveBrush;
                    }
                    else if (IsLyricLineLit(i))
                    {
                        currentLyrics[i].Opacity = LineOpacity(currentLyrics[i], 1.0);
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

                        currentLyrics[i].Opacity = LineOpacity(currentLyrics[i], coreOp);
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
                        currentLyrics[oldIndex].Opacity = LineOpacity(currentLyrics[oldIndex], 1.0);
                        currentLyrics[oldIndex].ColorBrush = IsLyricLineLit(oldIndex) ? _lyricActiveBrush : _lyricInactiveBrush;
                    }
                    if (currentLyricIndex >= 0 && currentLyricIndex < currentLyrics.Count)
                    {
                        currentLyrics[currentLyricIndex].BlurOpacity = 0.0;
                        currentLyrics[currentLyricIndex].FarBlurOpacity = 0.0;
                        currentLyrics[currentLyricIndex].Opacity = LineOpacity(currentLyrics[currentLyricIndex], 1.0);
                        currentLyrics[currentLyricIndex].ColorBrush = _lyricActiveBrush;
                    }
                    // Lines lit together with the current one, or that just stopped being lit
                    foreach (var list in new[] { _litLyricLines, _litLyricLinesPrev })
                    {
                        for (int n = 0; n < list.Count; n++)
                        {
                            int i = list[n];
                            if (i < currentLyrics.Count)
                                currentLyrics[i].ColorBrush = IsLyricLineLit(i) ? _lyricActiveBrush : _lyricInactiveBrush;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < currentLyrics.Count; i++)
                    {
                        currentLyrics[i].BlurOpacity = 0.0;
                        currentLyrics[i].FarBlurOpacity = 0.0;
                        currentLyrics[i].Opacity = LineOpacity(currentLyrics[i], 1.0);
                        if (IsLyricLineLit(i))
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
                    bool lit = IsLyricLineLit(i);
                    double scale = lit ? 1.0 : 0.85;
                    container.Opacity = lit ? 1.0 : 0.5;
                    var st = EnsureLyricScale(container, i, scale);
                    st.ScaleX = scale; st.ScaleY = scale;
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
                if (IsLyricLineLit(i)) continue;
                var c = LyricsListView.ContainerFromIndex(i) as FrameworkElement;
                if (c != null)
                {
                    var st = FindChildByName(c, "LyricSharpText") as TextBlock;
                    // A line still animating its last words keeps its hidden runs under the word overlay
                    if (st != null && st.Inlines.Count > 0 && !IsSharpInOverlay(st, false))
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
                // Set all non-active lines to dim, lines being sung to bright
                args.ItemContainer.Opacity = IsLyricLineLit(args.ItemIndex) ? 1.0 : 0.5;

                SyncRecycledContainerVisibility(args);
                ReleaseLyricEffectsInContainer(args.ItemContainer, args.ItemIndex, true);

                var sharpText = FindChildByName(args.ItemContainer, "FullscreenLyricSharpText") as TextBlock;
                if (sharpText != null && !IsSharpInOverlay(sharpText, true) && sharpText.Inlines.Count > 0)
                {
                    var line = args.Item as LyricLine;
                    sharpText.Inlines.Clear();
                    if (line != null) sharpText.Text = line.Text ?? "";
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
                    _lyricsWordTimer.Interval = TimeSpan.FromMilliseconds(60);
                    _lyricsWordTimer.Tick += LyricsWordTimer_Tick;
                }
                if (!_lyricsWordTimer.IsEnabled)
                {
                    InvalidateLyricsClock();
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

        // The word timer ticks ~16x/s. Asking the background audio process for its state and position on every tick
        // cost two cross-process calls per tick (~33/s). The position is now read for real every 500 ms (and on every
        // tick while a seek settles) and advanced with a Stopwatch at the playback rate in between; pause/resume
        // arrive through CurrentStateChanged, which resets this clock and stops the timer.
        private readonly System.Diagnostics.Stopwatch _lyricsClock = new System.Diagnostics.Stopwatch();
        private TimeSpan _lyricsClockBase;
        private const int LyricsClockResyncMs = 500;

        private void InvalidateLyricsClock()
        {
            _lyricsClock.Reset();
        }

        private bool TryGetLyricsPosition(out TimeSpan pos)
        {
            pos = TimeSpan.Zero;
            bool resync = !_lyricsClock.IsRunning
                          || _lyricsClock.ElapsedMilliseconds >= LyricsClockResyncMs
                          || _lastSeekTimestamp != DateTime.MinValue;
            if (!resync)
            {
                double rate = _playbackSpeeds[_playbackSpeedIndex];
                pos = _lyricsClockBase + TimeSpan.FromTicks((long)(_lyricsClock.Elapsed.Ticks * rate));
                return true;
            }

            MediaPlayerState state;
            try
            {
                state = _appMediaPlayer.CurrentState;
                pos = _appMediaPlayer.Position;
            }
            catch
            {
                MarkPlayerDisconnected();
                _lyricsWordTimer.Stop();
                return false;
            }
            if (state != MediaPlayerState.Playing)
            {
                InvalidateLyricsClock();
                UpdateWordTimerState();
                return false;
            }
            _lyricsClockBase = pos;
            _lyricsClock.Reset();
            _lyricsClock.Start();
            return true;
        }

        private void LyricsWordTimer_Tick(object sender, object e)
        {
            try
            {
                if (_isSliderManipulating) return;
                if (_appMediaPlayer == null) return;
                if (_playerDisconnected)
                {
                    _lyricsWordTimer.Stop();
                    return;
                }
                TimeSpan pos;
                if (!TryGetLyricsPosition(out pos)) return;

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

                int newIndex = FindLyricIndexAt(pos);

                if (newIndex != currentLyricIndex && newIndex >= 0)
                {
                    int oldIndex = currentLyricIndex;
                    currentLyricIndex = newIndex;
                    RefreshLitLines(pos);

                    if (isFs) ResetWordInlines(oldIndex, true);
                    if (isNpLyrics) ResetWordInlines(oldIndex, false);

                    UpdateLyricsVisualState(oldIndex);
                    ForceUpdateLyricUI(oldIndex);

                    if (isFs) UpdateActiveLineWordProgress(pos, true);
                    if (isNpLyrics) UpdateActiveLineWordProgress(pos, false);
                }
                else if (currentLyricIndex >= 0 && currentLyricIndex < currentLyrics.Count)
                {
                    // Same line followed, but an overlapping line (duet / background vocals) may have started or ended
                    if (RefreshLitLines(pos)) ApplyLitLinesChange(isFs, isNpLyrics);
                    if (isFs) UpdateActiveLineWordProgress(pos, true);
                    if (isNpLyrics) UpdateActiveLineWordProgress(pos, false);
                }
            }
            catch { }
        }

        private static List<string> SplitIntoGraphemes(string text, out int trailingSpaces)
        {
            trailingSpaces = 0;
            if (string.IsNullOrEmpty(text)) return new List<string>(0);

            string trimmed = text.TrimEnd(' ');
            trailingSpaces = text.Length - trimmed.Length;
            if (trimmed.Length == 0) return new List<string>(0);

            var list = new List<string>(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (char.IsHighSurrogate(trimmed[i]) && i + 1 < trimmed.Length && char.IsLowSurrogate(trimmed[i + 1]))
                {
                    list.Add(trimmed.Substring(i, 2));
                    i++;
                }
                else
                {
                    list.Add(trimmed[i].ToString());
                }
            }
            return list;
        }

        private void SetupWordInlines(TextBlock tb, LyricLine line, TimeSpan pos, List<LyricCharInfo> charList)
        {
            if (tb == null || line == null || line.Words == null || charList == null) return;
            tb.Text = "";
            tb.Inlines.Clear();
            charList.Clear();

            if (line.IsInterlude)
            {
                for (int i = 0; i < line.Words.Count; i++)
                {
                    var w = line.Words[i];
                    var run = new Run { Text = w.Text ?? "" };
                    if (pos >= w.EndTime) run.Foreground = _lyricDotPastBrush;
                    else if (pos >= w.StartTime) run.Foreground = _lyricDotActiveBrush;
                    else run.Foreground = _lyricDotPendingBrush;

                    tb.Inlines.Add(run);
                    charList.Add(new LyricCharInfo { StartTime = w.StartTime, EndTime = w.EndTime, WordIndex = i });
                }
                return;
            }

            for (int i = 0; i < line.Words.Count; i++)
            {
                var w = line.Words[i];
                string rawText = w.Text ?? "";
                if (rawText.Length == 0) continue;

                int trailingSpaces;
                var graphemes = SplitIntoGraphemes(rawText, out trailingSpaces);
                if (graphemes.Count == 0)
                {
                    var run = new Run { Text = rawText, Foreground = _lyricPendingBrush };
                    tb.Inlines.Add(run);
                    charList.Add(new LyricCharInfo { StartTime = w.StartTime, EndTime = w.EndTime, WordIndex = i });
                    continue;
                }

                int count = graphemes.Count;
                double wordDurMs = (w.EndTime - w.StartTime).TotalMilliseconds;
                if (wordDurMs <= 0) wordDurMs = 250;
                double charDurMs = wordDurMs / count;

                for (int c = 0; c < count; c++)
                {
                    string cText = (c == count - 1 && trailingSpaces > 0)
                        ? (graphemes[c] + new string(' ', trailingSpaces))
                        : graphemes[c];

                    TimeSpan cStart = w.StartTime + TimeSpan.FromMilliseconds(c * charDurMs);
                    TimeSpan cEnd = w.StartTime + TimeSpan.FromMilliseconds((c + 1) * charDurMs);

                    var run = new Run { Text = cText };
                    if (pos >= cEnd) run.Foreground = _lyricPastBrush;
                    else if (pos >= cStart) run.Foreground = _lyricSpotlightBrush;
                    else run.Foreground = _lyricPendingBrush;

                    tb.Inlines.Add(run);
                    charList.Add(new LyricCharInfo { StartTime = cStart, EndTime = cEnd, WordIndex = i });
                }
            }
        }

        private void UpdateWordInlines(TextBlock tb, LyricLine line, TimeSpan pos, List<LyricCharInfo> charList)
        {
            if (tb == null || line == null || line.Words == null || charList == null) return;
            int count = tb.Inlines.Count;
            if (count != charList.Count || count == 0)
            {
                SetupWordInlines(tb, line, pos, charList);
                return;
            }

            if (line.IsInterlude)
            {
                for (int i = 0; i < count; i++)
                {
                    var run = tb.Inlines[i] as Run;
                    if (run == null) continue;
                    var info = charList[i];

                    Brush target;
                    if (pos >= info.EndTime) target = _lyricDotPastBrush;
                    else if (pos >= info.StartTime) target = _lyricDotActiveBrush;
                    else target = _lyricDotPendingBrush;

                    if (!object.ReferenceEquals(run.Foreground, target))
                    {
                        run.Foreground = target;
                    }
                }
                return;
            }

            int activeCharIndex = FindActiveCharIndex(charList, pos);

            for (int i = 0; i < count; i++)
            {
                var run = tb.Inlines[i] as Run;
                if (run == null) continue;
                var info = charList[i];

                Brush target;
                if (pos < info.StartTime)
                {
                    if (activeCharIndex >= 0 && i == activeCharIndex + 1)
                    {
                        target = _lyricFlareHeadBrush;
                    }
                    else
                    {
                        target = _lyricPendingBrush;
                    }
                }
                else if (pos < info.EndTime)
                {
                    target = _lyricSpotlightBrush;
                }
                else
                {
                    if (activeCharIndex >= 0 && i == activeCharIndex - 1)
                    {
                        target = _lyricFlareTailBrush;
                    }
                    else
                    {
                        target = _lyricPastBrush;
                    }
                }

                if (!object.ReferenceEquals(run.Foreground, target))
                {
                    run.Foreground = target;
                }
            }
        }

        /// <summary>
        /// Gọi khi dòng active đổi. Dòng cũ nếu còn chữ đang chạy thì để bộ quản lý overlay tự cho "về hưu"
        /// (mờ dần sau khi chữ cuối chạy xong); nếu không có overlay thì trả chữ gốc ngay.
        /// </summary>
        public void ResetWordInlines(int index, bool isFullscreen)
        {
            if (currentLyrics == null || index < 0 || index >= currentLyrics.Count) return;
            if (FindOverlay(isFullscreen ? _fsOverlays : _regularOverlays, index) != null) return;

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

        private static WordOverlayState FindOverlay(List<WordOverlayState> overlays, int lineIndex)
        {
            for (int i = 0; i < overlays.Count; i++)
            {
                if (overlays[i].LineIndex == lineIndex) return overlays[i];
            }
            return null;
        }

        /// <summary>Dòng còn cần hiệu ứng: từ lúc bắt đầu (sớm 0.2s) tới khi chữ cuối hát xong + phần lò xo.</summary>
        private static bool IsLineAnimating(LyricLine l, TimeSpan pos)
        {
            if (l == null || !l.HasWords) return false;
            TimeSpan end = TimeSpan.Zero;
            for (int i = 0; i < l.Words.Count; i++)
            {
                var w = l.Words[i];
                if (!string.IsNullOrEmpty(w.Text) && w.EndTime > end) end = w.EndTime;
            }
            if (end <= l.Time) end = l.EndTime > l.Time ? l.EndTime : l.Time + TimeSpan.FromSeconds(1);
            end += TimeSpan.FromMilliseconds(SpringTailMs + 80);
            return pos >= l.Time - TimeSpan.FromMilliseconds(200) && pos <= end;
        }

        /// <summary>
        /// Mỗi tick: quyết định các dòng đang "sống" (dòng hiện tại + dòng liền trước còn chạy nốt / hát chồng),
        /// dựng overlay cho dòng mới, cập nhật mọi overlay, và cho overlay hết sống mờ dần rồi gỡ.
        /// </summary>
        public void UpdateActiveLineWordProgress(TimeSpan pos, bool isFullscreen)
        {
            if (currentLyrics == null || currentLyrics.Count == 0) return;
            var targetListView = isFullscreen ? FullscreenLyricsListView : LyricsListView;
            if (targetListView == null) return;
            var overlays = isFullscreen ? _fsOverlays : _regularOverlays;
            string sharpName = isFullscreen ? "FullscreenLyricSharpText" : "LyricSharpText";

            // 1. Dòng đang sống: quanh dòng hiện tại cả hai phía (dòng bè nằm ngay dưới dòng chính có thể bắt đầu sau
            //    dòng kế tiếp, và song ca có thể chồng nhiều dòng)
            _liveLineScratch.Clear();
            int cur = currentLyricIndex;
            if (cur >= 0 && cur < currentLyrics.Count)
            {
                for (int i = Math.Max(0, cur - 3); i <= Math.Min(currentLyrics.Count - 1, cur + 2); i++)
                {
                    var l = currentLyrics[i];
                    if (!l.HasWords) continue;
                    if (i == cur || IsLineAnimating(l, pos)) _liveLineScratch.Add(i);
                }
            }

            // 2. Overlay hết sống / container đã bị recycle / đổi cỡ chữ → gỡ (mờ dần nếu vẫn đang hiển thị đúng dòng)
            for (int k = overlays.Count - 1; k >= 0; k--)
            {
                var ov = overlays[k];
                TextBlock sharpNow = null;
                var c = targetListView.ContainerFromIndex(ov.LineIndex) as FrameworkElement;
                if (c != null) sharpNow = FindChildByName(c, sharpName) as TextBlock;

                bool recycled = sharpNow == null || !object.ReferenceEquals(sharpNow, ov.Sharp);
                bool resized = !ov.Pending && ov.Sharp != null &&
                               (Math.Abs(ov.BuiltWidth - ov.Sharp.ActualWidth) > 0.5 || Math.Abs(ov.BuiltFontSize - ov.Sharp.FontSize) > 0.01);
                bool alive = _liveLineScratch.Contains(ov.LineIndex);
                if (!alive || recycled || resized)
                {
                    RetireWordOverlay(ov, alive == false && !recycled && !resized);
                    overlays.RemoveAt(k);
                }
            }

            // 3. Dựng overlay cho dòng sống chưa có, rồi cập nhật tất cả
            for (int n = 0; n < _liveLineScratch.Count; n++)
            {
                int idx = _liveLineScratch[n];
                var line = currentLyrics[idx];
                var ov = FindOverlay(overlays, idx);
                if (ov == null)
                {
                    var container = targetListView.ContainerFromIndex(idx) as FrameworkElement;
                    var sharp = container != null ? FindChildByName(container, sharpName) as TextBlock : null;
                    if (sharp == null) continue;
                    ov = new WordOverlayState { LineIndex = idx, Sharp = sharp, Pending = true };
                    SetupWordInlines(sharp, line, pos, ov.Chars); // vị trí từ chỉ đo được sau khi layout xong → tick sau
                    overlays.Add(ov);
                    continue;
                }

                if (ov.Pending)
                {
                    if (ov.Sharp.Inlines.Count != ov.Chars.Count)
                    {
                        SetupWordInlines(ov.Sharp, line, pos, ov.Chars);
                        continue;
                    }
                    if (TryBuildWordOverlay(ov, line))
                    {
                        ov.Pending = false;
                    }
                    else
                    {
                        UpdateWordInlines(ov.Sharp, line, pos, ov.Chars); // dự phòng: tô màu từng ký tự như cũ
                        continue;
                    }
                }
                UpdateWordOverlay(ov, pos);
            }
        }

        private static int FindActiveCharIndex(List<LyricCharInfo> charList, TimeSpan pos)
        {
            for (int i = 0; i < charList.Count; i++)
            {
                if (pos >= charList[i].StartTime && pos < charList[i].EndTime) return i;
            }
            return -1;
        }

        // ── Per-word overlay engine (Spicy Lyrics parity) ──

        private static readonly Windows.UI.Xaml.Media.Animation.CubicEase _wordEaseOut =
            new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        private static readonly Windows.UI.Xaml.Media.Animation.ElasticEase _wordSpring =
            new Windows.UI.Xaml.Media.Animation.ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut };

        private const double WordPeakScale = 1.045;       // Spicy: 1.05 ở 70% thời lượng từ
        private const double WordLiftFactor = 0.045;      // nhấc tối đa = 4.5% cỡ chữ (~1px ở cỡ 22)
        private const double DotRestScale = 0.75;         // Spicy: dấu chấm nghỉ ở 0.75 / 35%
        private const double DotRestOpacity = 0.35;
        private const double DotLiftFactor = 0.12;        // Spicy: dấu chấm nhấc -0.12em khi tới lượt
        private const double SpringTailMs = 320;          // phần "lò xo" sau khi từ hát xong

        private static Windows.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames KeyFrames(
            DependencyObject target, string property, double v0, double t1, double v1, double t2, double v2,
            Windows.UI.Xaml.Media.Animation.EasingFunctionBase ease1, Windows.UI.Xaml.Media.Animation.EasingFunctionBase ease2)
        {
            var a = new Windows.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new Windows.UI.Xaml.Media.Animation.DiscreteDoubleKeyFrame
            {
                KeyTime = Windows.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero), Value = v0
            });
            a.KeyFrames.Add(new Windows.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                KeyTime = Windows.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t1)), Value = v1, EasingFunction = ease1
            });
            a.KeyFrames.Add(new Windows.UI.Xaml.Media.Animation.EasingDoubleKeyFrame
            {
                KeyTime = Windows.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t2)), Value = v2, EasingFunction = ease2
            });
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, target);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, property);
            return a;
        }

        private static Windows.UI.Xaml.Media.Animation.DoubleAnimation OffsetSweep(GradientStop stop, double from, double to, double durMs)
        {
            var a = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = from, To = to, Duration = TimeSpan.FromMilliseconds(durMs),
                EnableDependentAnimation = true // GradientStop.Offset là dependent animation
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, stop);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, "Offset");
            return a;
        }

        private WordVisual CreateWordVisual(string text, TextBlock sharp, bool isDot, TimeSpan start, TimeSpan end)
        {
            double fs = sharp.FontSize;
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fs,
                FontFamily = sharp.FontFamily,
                FontWeight = sharp.FontWeight,
                FontStyle = sharp.FontStyle,
                CharacterSpacing = sharp.CharacterSpacing,
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false
            };
            var ct = new CompositeTransform();
            tb.RenderTransform = ct;
            tb.RenderTransformOrigin = new Point(0.5, 0.6);

            var w = new WordVisual { Text = tb, Transform = ct, Start = start, End = end };
            double d = Math.Max(120, (end - start).TotalMilliseconds);
            double tail = d + SpringTailMs;
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard { FillBehavior = Windows.UI.Xaml.Media.Animation.FillBehavior.Stop };

            if (isDot)
            {
                tb.Foreground = _dotBrush;
                sb.Children.Add(KeyFrames(tb, "Opacity", DotRestOpacity, d * 0.6, 1.0, tail, 1.0, _wordEaseOut, _wordEaseOut));
                sb.Children.Add(KeyFrames(ct, "ScaleX", DotRestScale, d * 0.7, 1.05, tail, 1.0, _wordEaseOut, _wordSpring));
                sb.Children.Add(KeyFrames(ct, "ScaleY", DotRestScale, d * 0.7, 1.05, tail, 1.0, _wordEaseOut, _wordSpring));
                sb.Children.Add(KeyFrames(ct, "TranslateY", 0, d * 0.9, -fs * DotLiftFactor, tail, 0, _wordEaseOut, _wordEaseOut));
            }
            else
            {
                // Gradient quét trái → phải: trắng tới LitStop, mờ dần sang màu chờ trong 20% chiều rộng từ
                w.LitStop = new GradientStop { Color = Windows.UI.Colors.White, Offset = -0.2 };
                w.DimStop = new GradientStop { Color = _wordDimColor, Offset = 0.0 };
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
                brush.GradientStops.Add(w.LitStop);
                brush.GradientStops.Add(w.DimStop);
                tb.Foreground = brush;

                sb.Children.Add(OffsetSweep(w.LitStop, -0.2, 1.0, d));
                sb.Children.Add(OffsetSweep(w.DimStop, 0.0, 1.2, d));
                sb.Children.Add(KeyFrames(ct, "ScaleX", 1.0, d * 0.7, WordPeakScale, tail, 1.0, _wordEaseOut, _wordSpring));
                sb.Children.Add(KeyFrames(ct, "ScaleY", 1.0, d * 0.7, WordPeakScale, tail, 1.0, _wordEaseOut, _wordSpring));
                sb.Children.Add(KeyFrames(ct, "TranslateY", 0, d * 0.9, -fs * WordLiftFactor, tail, 0, _wordEaseOut, _wordEaseOut));
            }

            w.Anim = sb;
            ApplyWordState(w, 0, isDot);
            return w;
        }

        /// <summary>Đặt giá trị tĩnh (local) của một từ. Storyboard dùng FillBehavior.Stop nên khi kết thúc sẽ về đúng giá trị này.</summary>
        private static void ApplyWordState(WordVisual w, int state, bool isDot)
        {
            bool sung = state == 2;
            w.Transform.TranslateY = 0;
            if (isDot)
            {
                w.Text.Opacity = sung ? 1.0 : DotRestOpacity;
                w.Transform.ScaleX = sung ? 1.0 : DotRestScale;
                w.Transform.ScaleY = sung ? 1.0 : DotRestScale;
            }
            else
            {
                w.Transform.ScaleX = 1.0;
                w.Transform.ScaleY = 1.0;
                w.LitStop.Offset = sung ? 1.0 : -0.2;
                w.DimStop.Offset = sung ? 1.2 : 0.0;
            }
        }

        private bool TryBuildWordOverlay(WordOverlayState ov, LyricLine line)
        {
            Canvas layer = null;
            var sharp = ov.Sharp;
            try
            {
                if (sharp == null) return false;
                var panel = sharp.Parent as Panel;
                if (panel != null)
                {
                    for (int c = 0; c < panel.Children.Count && layer == null; c++) layer = panel.Children[c] as Canvas;
                }
                if (layer == null || sharp.ActualWidth <= 0 || layer.ActualWidth <= 0) return false; // chưa layout (vd dòng vừa hiện lại)
                StopLayerRetire(layer); // Canvas này có thể đang mờ dần overlay của dòng cũ (container tái sử dụng)

                var chars = ov.Chars;
                var inlines = sharp.Inlines;
                if (inlines.Count == 0 || inlines.Count != chars.Count) return false;

                var toLayer = sharp.TransformToVisual(layer);
                bool isDot = line.IsInterlude;
                double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;

                int i = 0;
                while (i < chars.Count)
                {
                    int word = chars[i].WordIndex;
                    int j = i;
                    var sbText = new System.Text.StringBuilder();
                    while (j < chars.Count && chars[j].WordIndex == word)
                    {
                        var r = inlines[j] as Run;
                        if (r != null) sbText.Append(r.Text);
                        j++;
                    }

                    string text = sbText.ToString().Trim();
                    int k = i;
                    while (k < j && string.IsNullOrWhiteSpace((inlines[k] as Run) != null ? ((Run)inlines[k]).Text : null)) k++;
                    if (text.Length > 0 && k < j)
                    {
                        var rc = ((Run)inlines[k]).ContentStart.GetCharacterRect(LogicalDirection.Forward);
                        if (rc.IsEmpty || rc.Height <= 0)
                        {
                            ClearPartialOverlay(ov, layer);
                            return false; // chưa layout xong → thử lại tick sau
                        }
                        var p = toLayer.TransformPoint(new Point(rc.X, rc.Y));
                        var wv = CreateWordVisual(text, sharp, isDot, chars[i].StartTime, chars[j - 1].EndTime);
                        Canvas.SetLeft(wv.Text, p.X);
                        Canvas.SetTop(wv.Text, p.Y);
                        layer.Children.Add(wv.Text);
                        ov.Words.Add(wv);

                        wv.Text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X + wv.Text.DesiredSize.Width);
                        minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y + rc.Height);
                    }
                    i = j;
                }
                if (ov.Words.Count == 0)
                {
                    ClearPartialOverlay(ov, layer);
                    return false;
                }

                // Lớp chữ gốc giữ nguyên layout nhưng ẩn đi, chỉ lớp phủ hiển thị
                for (int r = 0; r < inlines.Count; r++)
                {
                    var run = inlines[r] as Run;
                    if (run != null) run.Foreground = _wordHiddenBrush;
                }

                ov.Layer = layer;
                ov.IsInterlude = isDot;
                ov.BuiltWidth = sharp.ActualWidth;
                ov.BuiltFontSize = sharp.FontSize;
                ov.LineEnd = line.EndTime > line.Time ? line.EndTime : chars[chars.Count - 1].EndTime;
                ov.GroupExitStarted = false;
                layer.Opacity = LineOpacity(line, 1.0);

                if (isDot && layer.ActualWidth > 0 && layer.ActualHeight > 0)
                {
                    // Cuối đoạn dạo nhạc: cả nhóm chấm phồng nhẹ rồi thu về 0 và mờ đi (Spicy DotGroup)
                    var lct = layer.RenderTransform as CompositeTransform;
                    if (lct == null)
                    {
                        lct = new CompositeTransform();
                        layer.RenderTransform = lct;
                    }
                    layer.RenderTransformOrigin = new Point(
                        Math.Max(0, Math.Min(1, (minX + maxX) / 2 / layer.ActualWidth)),
                        Math.Max(0, Math.Min(1, (minY + maxY) / 2 / layer.ActualHeight)));
                    var exit = new Windows.UI.Xaml.Media.Animation.Storyboard(); // HoldEnd: giữ ẩn tới khi đổi dòng
                    exit.Children.Add(KeyFrames(lct, "ScaleX", 1.0, 110, 1.12, 400, 0.0, _wordEaseOut, _wordEaseOut));
                    exit.Children.Add(KeyFrames(lct, "ScaleY", 1.0, 110, 1.12, 400, 0.0, _wordEaseOut, _wordEaseOut));
                    exit.Children.Add(KeyFrames(layer, "Opacity", layer.Opacity, 150, layer.Opacity, 400, 0.0, _wordEaseOut, _wordEaseOut));
                    ov.GroupExit = exit;
                }
                return true;
            }
            catch
            {
                ClearPartialOverlay(ov, layer);
                return false;
            }
        }

        /// <summary>Hoàn tác phần overlay dựng dở (đo thất bại) nhưng giữ trạng thái chờ để thử lại ở tick sau.</summary>
        private static void ClearPartialOverlay(WordOverlayState ov, Canvas layer)
        {
            if (layer != null) layer.Children.Clear();
            ov.Words.Clear();
            if (ov.GroupExit != null)
            {
                try { ov.GroupExit.Stop(); } catch { }
                ov.GroupExit = null;
            }
        }

        private void UpdateWordOverlay(WordOverlayState ov, TimeSpan pos)
        {
            for (int i = 0; i < ov.Words.Count; i++)
            {
                var w = ov.Words[i];
                int state = pos < w.Start ? 0 : (pos < w.End ? 1 : 2);
                if (state == w.State) continue;
                int prev = w.State;
                w.State = state;

                // Bắt đầu hát — kể cả khi từ quá ngắn bị lọt giữa 2 tick (0 → 2 trong < 250ms)
                bool startAnim = state == 1 || (state == 2 && prev == 0 && (pos - w.End).TotalMilliseconds < 250);
                if (startAnim)
                {
                    w.Anim.Stop();
                    ApplyWordState(w, 2, ov.IsInterlude);
                    w.Anim.Begin();
                    var elapsed = pos - w.Start;
                    if (elapsed.TotalMilliseconds > 30) w.Anim.Seek(elapsed); // vào giữa từ (seek / vừa mở lyrics)
                }
                else if (state == 2)
                {
                    if (prev != 1)
                    {
                        w.Anim.Stop();
                        ApplyWordState(w, 2, ov.IsInterlude); // tua qua: hiện trạng thái đã hát ngay
                    }
                    // prev == 1: để animation tự chạy nốt phần lò xo
                }
                else
                {
                    w.Anim.Stop();
                    ApplyWordState(w, 0, ov.IsInterlude); // tua ngược
                }
            }

            if (ov.IsInterlude && ov.GroupExit != null && !ov.GroupExitStarted &&
                ov.LineEnd > TimeSpan.Zero && pos >= ov.LineEnd - TimeSpan.FromMilliseconds(400))
            {
                ov.GroupExitStarted = true;
                ov.GroupExit.Begin();
            }
        }

        /// <summary>Gỡ overlay ngay lập tức (đổi bài, container bị recycle, đo lại vị trí).</summary>
        private void TeardownWordOverlay(WordOverlayState ov, Canvas layerOverride = null)
        {
            if (ov.GroupExit != null)
            {
                try { ov.GroupExit.Stop(); } catch { }
                ov.GroupExit = null;
            }
            for (int i = 0; i < ov.Words.Count; i++)
            {
                try { ov.Words[i].Anim.Stop(); } catch { }
            }

            var layer = layerOverride ?? ov.Layer;
            if (layer != null)
            {
                StopLayerRetire(layer);
                layer.Children.Clear();
                layer.Opacity = 1.0;
                var lct = layer.RenderTransform as CompositeTransform;
                if (lct != null) { lct.ScaleX = 1.0; lct.ScaleY = 1.0; }
            }

            RestoreSharpText(ov);
            ov.Words.Clear();
            ov.Chars.Clear();
            ov.LineIndex = -1;
            ov.Sharp = null;
            ov.Layer = null;
            ov.Pending = false;
            ov.GroupExitStarted = false;
        }

        /// <summary>
        /// Dòng đã chạy xong hiệu ứng: trả chữ gốc (đang ở trạng thái mờ của dòng không active) ở dưới,
        /// còn lớp phủ sáng phía trên mờ dần → chuyển mượt thay vì tắt phụt.
        /// </summary>
        private void RetireWordOverlay(WordOverlayState ov, bool fade)
        {
            var layer = ov.Layer;
            if (!fade || ov.Pending || layer == null || layer.Children.Count == 0 || ov.GroupExitStarted)
            {
                TeardownWordOverlay(ov); // dấu chấm đã tự thu lại/biến mất, hoặc không cần mờ dần
                return;
            }

            for (int i = 0; i < ov.Words.Count; i++)
            {
                try { ov.Words[i].Anim.Stop(); } catch { }
            }
            if (ov.GroupExit != null)
            {
                try { ov.GroupExit.Stop(); } catch { }
                ov.GroupExit = null;
            }
            RestoreSharpText(ov);

            StopLayerRetire(layer);
            var fadeOut = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 0.0, Duration = TimeSpan.FromMilliseconds(RetireFadeMs), EasingFunction = _wordEaseOut
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeOut, layer);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeOut, "Opacity");
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            sb.Children.Add(fadeOut);
            sb.Completed += (s, e) =>
            {
                Windows.UI.Xaml.Media.Animation.Storyboard current;
                if (_retiringLayers.TryGetValue(layer, out current) && object.ReferenceEquals(current, sb))
                {
                    _retiringLayers.Remove(layer);
                    try { sb.Stop(); } catch { }
                    layer.Children.Clear();
                    layer.Opacity = 1.0;
                }
            };
            _retiringLayers[layer] = sb;
            sb.Begin();

            ov.Words.Clear();
            ov.Chars.Clear();
            ov.LineIndex = -1;
            ov.Sharp = null;
            ov.Layer = null;
            ov.GroupExitStarted = false;
        }

        /// <summary>Nếu Canvas đang mờ dần overlay cũ → dừng ngay và dọn sạch (để dựng overlay mới lên).</summary>
        private void StopLayerRetire(Canvas layer)
        {
            Windows.UI.Xaml.Media.Animation.Storyboard sb;
            if (layer != null && _retiringLayers.TryGetValue(layer, out sb))
            {
                _retiringLayers.Remove(layer);
                try { sb.Stop(); } catch { }
                layer.Children.Clear();
                layer.Opacity = 1.0;
            }
        }

        /// <summary>Trả lại chữ gốc cho TextBlock của dòng (các run đang bị ẩn / tô màu dự phòng).</summary>
        private static void RestoreSharpText(WordOverlayState ov)
        {
            if (ov.Sharp == null || ov.LineIndex < 0) return;
            try
            {
                var bound = ov.Sharp.DataContext as LyricLine;
                ov.Sharp.Inlines.Clear();
                ov.Sharp.Text = bound != null ? (bound.Text ?? "") : "";
            }
            catch { }
        }

        private bool IsSharpInOverlay(TextBlock sharp, bool isFullscreen)
        {
            var overlays = isFullscreen ? _fsOverlays : _regularOverlays;
            for (int i = 0; i < overlays.Count; i++)
            {
                if (object.ReferenceEquals(overlays[i].Sharp, sharp)) return true;
            }
            return false;
        }

        private static bool SetLyricContainerVisibility(ListViewBase lv, int index, Visibility vis)
        {
            if (lv == null) return false;
            var container = lv.ContainerFromIndex(index) as UIElement;
            if (container == null || container.Visibility == vis) return false;
            container.Visibility = vis;
            return true;
        }

        /// <summary>Container được recycle: đồng bộ lại trạng thái ẩn/hiện theo dòng mới (tránh dòng thường bị "kẹt" ẩn).</summary>
        private static void SyncRecycledContainerVisibility(ContainerContentChangingEventArgs args)
        {
            var line = args.Item as LyricLine;
            var vis = line != null ? line.LineVisibility : Visibility.Visible;
            if (args.ItemContainer.Visibility != vis) args.ItemContainer.Visibility = vis;
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            var cur = child;
            for (int depth = 0; cur != null && depth < 16; depth++)
            {
                if (object.ReferenceEquals(cur, ancestor)) return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        /// <summary>Container bị recycle sang dòng khác → gỡ lớp phủ để không "dính" từ của dòng cũ.</summary>
        private void ReleaseLyricEffectsInContainer(FrameworkElement container, int itemIndex, bool isFullscreen)
        {
            if (container == null) return;
            var overlays = isFullscreen ? _fsOverlays : _regularOverlays;
            for (int k = overlays.Count - 1; k >= 0; k--)
            {
                var ov = overlays[k];
                if (ov.Sharp != null && itemIndex != ov.LineIndex && IsDescendantOf(ov.Sharp, container))
                {
                    TeardownWordOverlay(ov);
                    overlays.RemoveAt(k);
                }
            }

            var layer = FindChildByName(container, isFullscreen ? "FullscreenLyricWordLayer" : "LyricWordLayer") as Canvas;
            if (layer == null) return;
            for (int k = 0; k < overlays.Count; k++)
            {
                if (object.ReferenceEquals(overlays[k].Layer, layer)) return; // đang dùng bởi overlay còn sống
            }
            StopLayerRetire(layer);
            if (layer.Children.Count > 0)
            {
                layer.Children.Clear();
                layer.Opacity = 1.0;
            }
        }

        private void ResetAllLyricEffects()
        {
            foreach (var overlays in new[] { _regularOverlays, _fsOverlays })
            {
                for (int k = 0; k < overlays.Count; k++) TeardownWordOverlay(overlays[k]);
                overlays.Clear();
            }
            foreach (var kv in new List<KeyValuePair<Canvas, Windows.UI.Xaml.Media.Animation.Storyboard>>(_retiringLayers))
            {
                try { kv.Value.Stop(); } catch { }
                kv.Key.Children.Clear();
                kv.Key.Opacity = 1.0;
            }
            _retiringLayers.Clear();
        }

        // ── Several lines sung at once (duets, background vocals under their line) ──
        // currentLyricIndex is the line the view follows (scroll, mini lyric). Lines that started earlier and are still
        // being sung stay lit with it: _litLyricLines holds them (never currentLyricIndex itself).
        private List<int> _litLyricLines = new List<int>(4);
        private List<int> _litLyricLinesPrev = new List<int>(4);
        private static readonly TimeSpan LyricLeadTime = TimeSpan.FromSeconds(0.2);
        // A line ending within this of the next one's start is a hand-over, not a duet: it is not kept lit
        private static readonly TimeSpan LitOverlapGrace = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// The line to follow at <paramref name="pos"/>: the last one that has started (0.2 s early). Lines are not strictly
        /// ordered by start time any more (a background line sits under its line even when the next line starts first),
        /// so the whole list is scanned instead of stopping at the first line not started.
        /// </summary>
        private int FindLyricIndexAt(TimeSpan pos)
        {
            int idx = -1;
            var early = pos + LyricLeadTime;
            for (int i = 0; i < currentLyrics.Count; i++)
            {
                if (currentLyrics[i].Time <= early) idx = i;
            }
            return idx;
        }

        private bool IsLyricLineLit(int index)
        {
            return index >= 0 && (index == currentLyricIndex || _litLyricLines.Contains(index));
        }

        /// <summary>Recomputes the lines lit besides the current one. True when the set changed (old set kept in _litLyricLinesPrev).</summary>
        private bool RefreshLitLines(TimeSpan pos)
        {
            var next = _litLyricLinesPrev;
            next.Clear();
            int cur = currentLyricIndex;
            if (currentLyrics != null && cur >= 0 && cur < currentLyrics.Count)
            {
                var early = pos + LyricLeadTime;
                for (int i = Math.Max(0, cur - 3); i <= Math.Min(currentLyrics.Count - 1, cur + 3); i++)
                {
                    if (i == cur) continue;
                    var l = currentLyrics[i];
                    if (l.IsInterlude) continue;
                    var end = l.SungEnd;
                    if (end > TimeSpan.Zero && l.Time <= early && pos < end - LitOverlapGrace) next.Add(i);
                }
            }

            bool changed = next.Count != _litLyricLines.Count;
            for (int i = 0; !changed && i < next.Count; i++) changed = next[i] != _litLyricLines[i];
            // Swap: _litLyricLines = new set, _litLyricLinesPrev = old set (for the callers that animate the difference)
            _litLyricLinesPrev = _litLyricLines;
            _litLyricLines = next;
            return changed;
        }

        private void ClearLitLines()
        {
            _litLyricLines.Clear();
            _litLyricLinesPrev.Clear();
        }

        /// <summary>
        /// The lit set changed while the current line stayed: repaint the lines that joined or left it (bound opacity /
        /// colour for both views, plus the container scale of the Spotify style and the container fade of fullscreen).
        /// </summary>
        private void ApplyLitLinesChange(bool isFullscreen, bool isRegular)
        {
            UpdateLyricsVisualState(currentLyricIndex);
            foreach (var list in new[] { _litLyricLines, _litLyricLinesPrev })
            {
                for (int n = 0; n < list.Count; n++)
                {
                    int i = list[n];
                    if (i == currentLyricIndex || i >= currentLyrics.Count) continue;
                    bool lit = IsLyricLineLit(i);
                    if (isFullscreen && FullscreenLyricsListView != null)
                    {
                        var fc = FullscreenLyricsListView.ContainerFromIndex(i) as FrameworkElement;
                        if (fc != null) AnimateOpacity(fc, lit ? 1.0 : 0.5);
                    }
                    if (isRegular && !_isAppleMusicStyle && LyricsListView != null)
                    {
                        var c = LyricsListView.ContainerFromIndex(i) as FrameworkElement;
                        if (c != null) AnimateLyricContainer(c, i, lit ? 1.0 : 0.85, lit ? 1.0 : 0.5);
                    }
                }
            }
        }

        /// <summary>Spotify style: eases a line container to a scale / opacity (independent animations, own storyboard).</summary>
        private void AnimateLyricContainer(FrameworkElement container, int index, double scale, double opacity)
        {
            var st = EnsureLyricScale(container, index, container.RenderTransform is ScaleTransform ? ((ScaleTransform)container.RenderTransform).ScaleX : scale);
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            foreach (var t in new[] { new { Target = (DependencyObject)st, Prop = "ScaleX", To = scale },
                                      new { Target = (DependencyObject)st, Prop = "ScaleY", To = scale },
                                      new { Target = (DependencyObject)container, Prop = "Opacity", To = opacity } })
            {
                var a = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = t.To, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = _wordEaseOut
                };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, t.Target);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, t.Prop);
                sb.Children.Add(a);
            }
            sb.Begin();
        }

        /// <summary>
        /// Spotify style: the container's ScaleTransform (created at <paramref name="initialScale"/> if missing), anchored on
        /// the side the line is drawn on — a right-aligned duet line scales around its right edge. Recycled containers
        /// keep their transform, so the origin is set every time.
        /// </summary>
        private ScaleTransform EnsureLyricScale(FrameworkElement container, int index, double initialScale)
        {
            var st = container.RenderTransform as ScaleTransform;
            if (st == null)
            {
                st = new ScaleTransform { ScaleX = initialScale, ScaleY = initialScale };
                container.RenderTransform = st;
            }
            bool right = currentLyrics != null && index >= 0 && index < currentLyrics.Count && currentLyrics[index].IsOppositeSide;
            container.RenderTransformOrigin = new Point(right ? 1 : 0, 0.5);
            return st;
        }

        /// <summary>The line the mini lyric (player page) shows: a background line never replaces the line it belongs to.</summary>
        private int MiniLyricIndex()
        {
            int cur = currentLyricIndex;
            if (cur < 0 || cur >= currentLyrics.Count || !currentLyrics[cur].IsBackground) return cur;
            for (int n = _litLyricLines.Count - 1; n >= 0; n--)
            {
                int i = _litLyricLines[n];
                if (i < currentLyrics.Count && !currentLyrics[i].IsBackground) return i;
            }
            return cur;
        }

        // ── Background vocals (lời bè) ──

        // Cỡ chữ dòng dấu chấm dạo nhạc so với lyric thường (glyph "•" vốn nhỏ nên cần phóng to hơn chữ)
        private const double InterludeFontScale = 1.9;

        private const double BackgroundVocalOpacityFactor = 0.72;

        private static double LineOpacity(LyricLine l, double value)
        {
            return l.IsBackground ? value * BackgroundVocalOpacityFactor : value;
        }

        private double GetLyricLineFontSize(LyricLine l)
        {
            if (l.IsInterlude) return _lyricFontSize * InterludeFontScale;
            if (l.Time >= TimeSpan.FromHours(1) && !string.IsNullOrEmpty(l.Text)) return _lyricFontSize * 0.65; // dòng ghi công nguồn lyrics
            if (l.IsBackground) return _lyricFontSize * 0.82;
            return _lyricFontSize;
        }

        /// <summary>True khi CẢ dòng nằm trong một cặp ngoặc, vd "(Ah-ah, ah-ah)" — không tính "(Ah) I love you (yeah)".</summary>
        private static bool IsParenthesizedLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim();
            if (t.Length < 3 || t[0] != '(' || t[t.Length - 1] != ')') return false;
            int depth = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '(') depth++;
                else if (t[i] == ')')
                {
                    depth--;
                    if (depth == 0 && i < t.Length - 1) return false;
                }
            }
            return depth == 0;
        }

    }
}





