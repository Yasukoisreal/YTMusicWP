using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        private async Task UpdateLyricsAsync(string title, string artist)
        {
            // [OPT-C2] Hủy Task lyrics cũ trước khi bắt đầu Task mới — tránh race condition khi skip bài nhanh
            var oldLyricsCts = _lyricsCts;
            _lyricsCts = new CancellationTokenSource();
            if (oldLyricsCts != null) { oldLyricsCts.Cancel(); oldLyricsCts.Dispose(); }
            var token = _lyricsCts.Token;

            currentLyrics.Clear();
            currentLyricIndex = -1;
            _cachedLyricsScrollViewer = null;

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
                    .Replace(" (Official MV)", "").Trim();
                string cleanArtist = CleanChannelName(artist);

                token.ThrowIfCancellationRequested();

                string syncedLyrics = null;
                string plainLyrics = null;

                // If artist is a type label ("Song", "Video", etc.), try to extract from title
                string[] typeLabels = { "Song", "Video", "Artist", "Playlist", "Album", "EP", "Single", "" };
                if (Array.IndexOf(typeLabels, cleanArtist) >= 0)
                {
                    // Title often has "ArtistName - SongName" format from YouTube  
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
                    // Artist is known, title might be "ArtistName - SongName" → take song part
                    var titleParts = cleanTitle.Split(new[] { " - " }, StringSplitOptions.None);
                    if (titleParts.Length >= 2)
                    {
                        // Check if first part matches the artist → take second part as song title
                        if (titleParts[0].Trim().Equals(cleanArtist, StringComparison.OrdinalIgnoreCase))
                        {
                            cleanTitle = titleParts[1].Trim();
                        }
                    }
                }

                // --- LỚP 1 (ƯU TIÊN): LRCLIB.NET exact match ---
                try
                {
                    string lrcUrl = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) + "&artist_name=" + Uri.EscapeDataString(cleanArtist);
                    var lrcResp = await _apiClient.GetStringAsync(lrcUrl);
                    token.ThrowIfCancellationRequested();
                    var lrcArr = JArray.Parse(lrcResp);
                    if (lrcArr.Count > 0)
                    {
                        // Prefer result with synced lyrics
                        foreach (var item in lrcArr)
                        {
                            string s = item["syncedLyrics"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(s))
                            {
                                syncedLyrics = s;
                                plainLyrics = item["plainLyrics"]?.ToString();
                                break;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(syncedLyrics))
                        {
                            syncedLyrics = lrcArr[0]["syncedLyrics"]?.ToString();
                            plainLyrics = lrcArr[0]["plainLyrics"]?.ToString();
                        }
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine("Lyrics Lớp 1 lỗi, chuyển sang Lớp 2."); }

                // --- LỚP 2 (DỰ PHÒNG): LRCLIB.NET q= broad search ---
                if (string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    try
                    {
                        string url2 = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(cleanTitle + " " + cleanArtist);
                        var response2 = await _apiClient.GetStringAsync(url2);
                        token.ThrowIfCancellationRequested();
                        var jsonArray2 = JArray.Parse(response2);
                        if (jsonArray2.Count > 0)
                        {
                            // Prefer result with synced lyrics
                            foreach (var item in jsonArray2)
                            {
                                string s = item["syncedLyrics"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(s))
                                {
                                    syncedLyrics = s;
                                    if (string.IsNullOrWhiteSpace(plainLyrics))
                                        plainLyrics = item["plainLyrics"]?.ToString();
                                    break;
                                }
                            }
                            if (string.IsNullOrWhiteSpace(syncedLyrics) && string.IsNullOrWhiteSpace(plainLyrics))
                            {
                                plainLyrics = jsonArray2[0]["plainLyrics"]?.ToString();
                            }
                        }
                    }
                    catch { }
                }

                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(syncedLyrics))
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
                                parsedLines.Add(new LyricLine { Time = t, Text = text });
                        }
                    }

                    parsedLines.Sort((a, b) => a.Time.CompareTo(b.Time));
                    foreach (var p in parsedLines) currentLyrics.Add(p);
                    currentLyrics.Add(new LyricLine { Time = TimeSpan.FromHours(1), Text = "" });
                }
                else if (!string.IsNullOrWhiteSpace(plainLyrics))
                {
                    LyricsFallbackText.Text = plainLyrics;
                    LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LyricsFallbackText.Text = "Lyrics not found for this track.";
                    LyricsFallbackScrollViewer.Visibility = Visibility.Visible;
                    LyricsListView.Visibility = Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
                // Bài hát đã đổi — bỏ qua, Task mới sẽ lo
                return;
            }
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

    }
}
