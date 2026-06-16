using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private async void OpenYouTubePlaylist(string playlistId, string playlistName, string coverUrl = null)
        {
            try
            {
                // Close artist profile if it's open, so playlist view is visible above it
                ArtistProfileView.Visibility = Visibility.Collapsed;

                PlaylistDetailsTitle.Text = playlistName;
                PlaylistDetailsCoverBrush.ImageSource = null;
                PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(coverUrl), UriKind.Absolute)) { DecodePixelWidth = 150 };
                    PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                }
                PlaylistSongsList.ItemsSource = null;
                PlaylistDetailsView.Visibility = Visibility.Visible;
                PlaylistSlideInStoryboard.Begin();
                
                var tracks = new ObservableCollection<YouTubeTrack>();
                bool useFallback = false;
                string proxyThumbnail = null;

                try
                {
                    var plResult = await InnerTubeClient.BrowsePlaylistAsync(playlistId);
                    proxyThumbnail = plResult.ThumbnailUrl;
                    if (!string.IsNullOrEmpty(plResult.Title))
                    {
                        PlaylistDetailsTitle.Text = plResult.Title;
                    }
                    foreach (var t in plResult.Tracks)
                    {
                        tracks.Add(t);
                    }
                }
                catch { useFallback = true; }

                if (useFallback || tracks.Count == 0)
                {
                    string apiKey = GetApiKey();
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        ShowToast("API Key required! Set it in Settings.");
                        return;
                    }

                    string url = $"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&maxResults=50&playlistId={playlistId}&key={apiKey}";
                    var response = await _apiClient.GetStringAsync(url);
                    var json = Newtonsoft.Json.Linq.JObject.Parse(response);
                    
                    var items = json["items"];
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            try
                            {
                                var snippet = item["snippet"];
                                string title = snippet["title"]?.ToString();
                                if (title == "Private video" || title == "Deleted video") continue;
                                
                                string vidId = snippet["resourceId"]?["videoId"]?.ToString();
                                string channel = snippet["videoOwnerChannelTitle"]?.ToString() ?? snippet["channelTitle"]?.ToString();
                                string channelId = snippet["videoOwnerChannelId"]?.ToString() ?? snippet["channelId"]?.ToString();
                                var thumbs = snippet["thumbnails"];
                                string thumbUrl = thumbs?["maxres"]?["url"]?.ToString()
                                   ?? thumbs?["standard"]?["url"]?.ToString()
                                   ?? thumbs?["high"]?["url"]?.ToString()
                                   ?? thumbs?["medium"]?["url"]?.ToString()
                                   ?? thumbs?["default"]?["url"]?.ToString();
                                   
                                tracks.Add(new YouTubeTrack
                                {
                                    VideoId = vidId,
                                    Title = title,
                                    ChannelName = CleanChannelName(channel),
                                    ChannelId = channelId,
                                    ThumbnailUrl = thumbUrl
                                });
                            }
                            catch { continue; }
                        }
                    }
                }

                // If no cover was set, try proxy thumbnail or first track's thumbnail
                if (PlaylistDetailsCoverRect.Visibility == Visibility.Collapsed && tracks.Count > 0)
                {
                    string fallbackCover = proxyThumbnail;
                    if (string.IsNullOrEmpty(fallbackCover))
                    {
                        fallbackCover = tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.ThumbnailUrl))?.ThumbnailUrl;
                    }
                    if (!string.IsNullOrEmpty(fallbackCover))
                    {
                        try
                        {
                            PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(fallbackCover), UriKind.Absolute)) { DecodePixelWidth = 150 };
                            PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                        }
                        catch { }
                    }
                }
                
                _currentViewingPlaylist = new UserPlaylist { Name = playlistName, Tracks = tracks };
                PlaylistSongsList.ItemsSource = _currentViewingPlaylist.Tracks;
                PlaylistDetailsTrackCount.Text = tracks.Count + " tracks";
            }
            catch { ShowToast("Failed to load playlist"); }
        }

        private async void OpenArtistProfile(string channelId, string channelName)
        {
            ArtistProfileView.Visibility = Visibility.Visible;
            ArtistSlideInStoryboard.Begin();
            ArtistLoadingBar.Visibility = Visibility.Visible;
            ArtistSongsList.Visibility = Visibility.Collapsed;
            ArtistProfileTitle.Text = channelName ?? "Unknown Artist";
            ArtistProfileCover.Source = null;
            ArtistProfileAvatar.ImageSource = null;
            ArtistAlbumsSection.Visibility = Visibility.Collapsed;
            ArtistAlbumsList.ItemsSource = null;

            List<YouTubeTrack> tracks = null;
            List<ArtistAlbum> albums = null;
            bool hasCustomAvatar = false;

            // --- LẤY AVATAR THẬT TỪ YOUTUBE API (NẾU CÓ API KEY) ---
            string apiKey = GetApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                try
                {
                    string ytUrl = string.IsNullOrEmpty(channelId) 
                        ? "https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=1&q=" + Uri.EscapeDataString(channelName) + "&type=channel&key=" + apiKey
                        : "https://www.googleapis.com/youtube/v3/channels?part=snippet&id=" + Uri.EscapeDataString(channelId) + "&key=" + apiKey;
                    
                    var response = await _apiClient.GetStringAsync(ytUrl);
                    var json = Newtonsoft.Json.Linq.JObject.Parse(response);
                    var items = json["items"];
                    if (items != null && items.Any())
                    {
                        var snippet = items[0]["snippet"];
                        if (string.IsNullOrEmpty(channelId)) 
                        {
                            channelId = snippet["channelId"]?.ToString() ?? items[0]["id"]?["channelId"]?.ToString();
                        }
                        var thumbUrl = snippet["thumbnails"]?["high"]?["url"]?.ToString() ?? snippet["thumbnails"]?["default"]?["url"]?.ToString();
                        if (thumbUrl != null)
                        {
                            ArtistProfileAvatar.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(thumbUrl))) { DecodePixelWidth = 200 };
                            hasCustomAvatar = true;
                        }
                        var titleStr = snippet["title"]?.ToString();
                        if (!string.IsNullOrEmpty(titleStr)) ArtistProfileTitle.Text = titleStr;
                    }
                }
                catch { }
            }

            // Lớp 1: InnerTube trực tiếp (không cần proxy)
            if (!string.IsNullOrEmpty(channelId))
            {
                try
                {
                    var artistResult = await InnerTubeClient.BrowseArtistAsync(channelId);
                    if (artistResult.Tracks != null && artistResult.Tracks.Count > 0)
                        tracks = artistResult.Tracks;

                    if (!string.IsNullOrEmpty(artistResult.AvatarUrl) && !hasCustomAvatar)
                    {
                        ArtistProfileAvatar.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(artistResult.AvatarUrl))) { DecodePixelWidth = 200 };
                        hasCustomAvatar = true;
                    }
                    if (!string.IsNullOrEmpty(artistResult.CoverUrl))
                    {
                        ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(artistResult.CoverUrl))) { DecodePixelWidth = 400 };
                    }
                    if (!string.IsNullOrEmpty(artistResult.Name) && artistResult.Name != "Artist")
                    {
                        ArtistProfileTitle.Text = artistResult.Name;
                    }
                    if (artistResult.Albums != null && artistResult.Albums.Count > 0)
                    {
                        albums = artistResult.Albums;
                    }
                }
                catch { }
            }

            // Lớp 2: Dự phòng (Fallback) gọi /api/search như cũ
            if (tracks == null || tracks.Count == 0)
            {
                string query = channelName ?? "";
                if (!string.IsNullOrEmpty(channelId)) query += " \"Topic\""; 
                tracks = await FetchMusicList(query);
            }
            
            var list = new ObservableCollection<YouTubeTrack>();
            if (tracks != null)
            {
                foreach(var t in tracks) 
                {
                    if (t.VideoId != null && t.VideoId.StartsWith("CHANNEL:")) continue;
                    list.Add(t);
                }
                
                if (list.Count > 0)
                {
                    try {
                        var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(list[0].ThumbnailUrl))) { DecodePixelWidth = 400 };
                        if (ArtistProfileCover.Source == null) ArtistProfileCover.Source = bmp;
                        if (!hasCustomAvatar) ArtistProfileAvatar.ImageSource = bmp;
                        
                        if (ArtistProfileTitle.Text == "Nghệ sĩ" || ArtistProfileTitle.Text == "Artist" || ArtistProfileTitle.Text == "Unknown Artist")
                        {
                            var trackWithArtist = list.FirstOrDefault(t => !string.IsNullOrEmpty(t.ChannelName) && t.ChannelName != "Nghệ sĩ" && t.ChannelName != "Artist");
                            if (trackWithArtist != null) ArtistProfileTitle.Text = trackWithArtist.ChannelName;
                            else if (!string.IsNullOrEmpty(list[0].ChannelName)) ArtistProfileTitle.Text = list[0].ChannelName;
                        }
                    } catch {}
                }
            }

            ArtistSongsList.ItemsSource = list;
            ArtistLoadingBar.Visibility = Visibility.Collapsed;
            ArtistSongsList.Visibility = Visibility.Visible;

            // Populate albums carousel
            if (albums != null && albums.Count > 0)
            {
                // Group by section title (Albums, Singles, etc.)
                var firstSection = albums[0].SectionTitle;
                ArtistAlbumsTitle.Text = firstSection;
                ArtistAlbumsList.ItemsSource = albums;
                ArtistAlbumsSection.Visibility = Visibility.Visible;
            }
        }

        private void CloseArtistProfile_Click(object sender, RoutedEventArgs e)
        {
            ArtistSlideOutStoryboard.Begin();
        }

        private void ArtistSlideOutStoryboard_Completed(object sender, object e)
        {
            ArtistProfileView.Visibility = Visibility.Collapsed;
            // [OPT-M9] Giải phóng ảnh khi đóng — tiết kiệm RAM
            ArtistProfileCover.Source = null;
            ArtistProfileAvatar.ImageSource = null;
            ArtistSongsList.ItemsSource = null;
            ArtistAlbumsList.ItemsSource = null;
            ArtistAlbumsSection.Visibility = Visibility.Collapsed;
        }

        private void ArtistPlayAll_Click(object sender, RoutedEventArgs e)
        {
            var list = ArtistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>;
            if (list != null && list.Count > 0) PlayTrack(list[0]);
        }

        private void ArtistShuffle_Click(object sender, RoutedEventArgs e)
        {
            var list = ArtistSongsList.ItemsSource as ObservableCollection<YouTubeTrack>;
            if (list != null && list.Count > 0)
            {
                var rng = new Random();
                int idx = rng.Next(list.Count);
                PlayTrack(list[idx]);
            }
        }

        private void ArtistAlbum_ItemClick(object sender, ItemClickEventArgs e)
        {
            var album = e.ClickedItem as ArtistAlbum;
            if (album == null) return;

            // If browseId looks like a playlist, open it
            if (!string.IsNullOrEmpty(album.BrowseId))
            {
                string playlistId = album.BrowseId;
                if (playlistId.StartsWith("MPREb_"))
                {
                    // Album browseId — browse as playlist
                    OpenYouTubePlaylist(playlistId, album.Title, album.ThumbnailUrl);
                }
                else if (playlistId.StartsWith("VL") || playlistId.StartsWith("PL"))
                {
                    OpenYouTubePlaylist(playlistId.Replace("VL", ""), album.Title, album.ThumbnailUrl);
                }
                else
                {
                    // Try to browse as playlist anyway  
                    OpenYouTubePlaylist(playlistId, album.Title, album.ThumbnailUrl);
                }
            }
        }

    }
}
