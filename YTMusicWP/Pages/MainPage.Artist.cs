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
        private string _currentArtistChannelId;
        private bool _isFollowingArtist;

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
                    // Fallback: search by playlist name
                    var searchResults = await FetchMusicList(playlistName);
                    if (searchResults != null)
                    {
                        foreach (var t in searchResults)
                        {
                            if (t.VideoId != null && !t.VideoId.StartsWith("CHANNEL:") && !t.VideoId.StartsWith("PLAYLIST:"))
                                tracks.Add(t);
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
            _currentArtistChannelId = channelId;
            _isFollowingArtist = false;
            _currentSubscriptionId = null;
            ArtistProfileView.Visibility = Visibility.Visible;
            ArtistSlideInStoryboard.Begin();
            ArtistLoadingBar.Visibility = Visibility.Visible;
            ArtistSongsList.Visibility = Visibility.Collapsed;
            ArtistProfileTitle.Text = channelName ?? "Unknown Artist";
            ArtistProfileCover.Source = null;
            UpdateFollowButton();
            ArtistMonthlyListeners.Text = "";
            ArtistAlbumsSection.Visibility = Visibility.Collapsed;
            ArtistAlbumsList.ItemsSource = null;
            ArtistAboutSection.Visibility = Visibility.Collapsed;
            ArtistAboutDescription.Text = "";
            ArtistAboutListeners.Text = "";

            List<YouTubeTrack> tracks = null;
            List<ArtistAlbum> albums = null;
            string subscriberCount = "";
            string description = "";
            string avatarUrl = "";

            // InnerTube artist profile
            if (!string.IsNullOrEmpty(channelId))
            {
                try
                {
                    var artistResult = await InnerTubeClient.BrowseArtistAsync(channelId);
                    if (artistResult.Tracks != null && artistResult.Tracks.Count > 0)
                        tracks = artistResult.Tracks;

                    avatarUrl = artistResult.AvatarUrl;

                    if (!string.IsNullOrEmpty(artistResult.CoverUrl))
                    {
                        ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(artistResult.CoverUrl))) { DecodePixelWidth = 480 };
                    }
                    else if (!string.IsNullOrEmpty(avatarUrl))
                    {
                        ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(avatarUrl))) { DecodePixelWidth = 480 };
                    }

                    if (!string.IsNullOrEmpty(artistResult.Name) && artistResult.Name != "Artist")
                    {
                        ArtistProfileTitle.Text = artistResult.Name;
                    }
                    if (artistResult.Albums != null && artistResult.Albums.Count > 0)
                    {
                        albums = artistResult.Albums;
                    }
                    subscriberCount = artistResult.SubscriberCount;
                    description = artistResult.Description;
                }
                catch { }
            }

            // Fallback search
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
                
                if (list.Count > 0 && ArtistProfileCover.Source == null)
                {
                    try {
                        var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(list[0].ThumbnailUrl))) { DecodePixelWidth = 480 };
                        ArtistProfileCover.Source = bmp;
                    } catch {}
                }

                if (ArtistProfileTitle.Text == "Nghệ sĩ" || ArtistProfileTitle.Text == "Artist" || ArtistProfileTitle.Text == "Unknown Artist")
                {
                    var trackWithArtist = list.FirstOrDefault(t => !string.IsNullOrEmpty(t.ChannelName) && t.ChannelName != "Nghệ sĩ" && t.ChannelName != "Artist");
                    if (trackWithArtist != null) ArtistProfileTitle.Text = trackWithArtist.ChannelName;
                    else if (list.Count > 0 && !string.IsNullOrEmpty(list[0].ChannelName)) ArtistProfileTitle.Text = list[0].ChannelName;
                }
            }

            // Monthly listeners
            if (!string.IsNullOrEmpty(subscriberCount))
            {
                ArtistMonthlyListeners.Text = subscriberCount + " followers";
                ArtistAboutListeners.Text = subscriberCount;
            }
            else
            {
                ArtistMonthlyListeners.Text = "";
            }

            ArtistSongsList.ItemsSource = list;
            ArtistLoadingBar.Visibility = Visibility.Collapsed;
            ArtistSongsList.Visibility = Visibility.Visible;

            // Albums carousel
            if (albums != null && albums.Count > 0)
            {
                var firstSection = albums[0].SectionTitle;
                ArtistAlbumsTitle.Text = !string.IsNullOrEmpty(firstSection) ? firstSection : "Releases";
                ArtistAlbumsList.ItemsSource = albums;
                ArtistAlbumsSection.Visibility = Visibility.Visible;
            }

            // About section
            if (!string.IsNullOrEmpty(subscriberCount) || !string.IsNullOrEmpty(description))
            {
                ArtistAboutListeners.Text = !string.IsNullOrEmpty(subscriberCount) ? subscriberCount : "";
                ArtistAboutDescription.Text = description;
                // Use avatar or cover for about background
                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    ArtistAboutImage.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(avatarUrl))) { DecodePixelWidth = 300 };
                }
                ArtistAboutSection.Visibility = Visibility.Visible;
            }

            // Check if already following
            await CheckFollowStatusAsync(channelId);
        }

        private async Task CheckFollowStatusAsync(string channelId)
        {
            if (string.IsNullOrEmpty(channelId)) return;
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (!settings.ContainsKey("GoogleAccessToken") || string.IsNullOrEmpty(settings["GoogleAccessToken"]?.ToString()))
                return;

            try
            {
                string accessToken = settings["GoogleAccessToken"].ToString();
                var request = new HttpRequestMessage(HttpMethod.Get,
                    "https://www.googleapis.com/youtube/v3/subscriptions?part=snippet&mine=true&forChannelId=" + channelId);
                request.Headers.Add("Authorization", "Bearer " + accessToken);
                var response = await _apiClient.SendAsync(
                    request);

                if (response.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                    var items = json["items"] as JArray;
                    if (items != null && items.Count > 0)
                    {
                        _isFollowingArtist = true;
                        _currentSubscriptionId = items[0]["id"]?.ToString();
                        UpdateFollowButton();
                    }
                }
            }
            catch { }
        }

        private void UpdateFollowButton()
        {
            if (_isFollowingArtist)
            {
                ArtistFollowBtn.Content = "Following";
                ArtistFollowBtn.Foreground = _greenBrush;
            }
            else
            {
                ArtistFollowBtn.Content = "Follow";
                ArtistFollowBtn.Foreground = _whiteBrush;
            }
        }

        private string _currentSubscriptionId; // For unsubscribe

        private async void ArtistFollow_Click(object sender, RoutedEventArgs e)
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (!settings.ContainsKey("GoogleAccessToken") || string.IsNullOrEmpty(settings["GoogleAccessToken"]?.ToString()))
            {
                ShowToast("Please sign in to follow artists");
                return;
            }

            if (string.IsNullOrEmpty(_currentArtistChannelId))
            {
                ShowToast("Cannot follow this artist");
                return;
            }

            string accessToken = settings["GoogleAccessToken"].ToString();

            try
            {
                if (!_isFollowingArtist)
                {
                    // Subscribe via YouTube Data API
                    var requestBody = new JObject
                    {
                        ["snippet"] = new JObject
                        {
                            ["resourceId"] = new JObject
                            {
                                ["kind"] = "youtube#channel",
                                ["channelId"] = _currentArtistChannelId
                            }
                        }
                    };

                    var content = new StringContent(requestBody.ToString(), System.Text.Encoding.UTF8, "application/json");
                    var subRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/youtube/v3/subscriptions?part=snippet") { Content = content };
                    subRequest.Headers.Add("Authorization", "Bearer " + accessToken);
                    var response = await _apiClient.SendAsync(
                        subRequest);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = JObject.Parse(await response.Content.ReadAsStringAsync());
                        _currentSubscriptionId = result["id"]?.ToString();
                        _isFollowingArtist = true;
                        UpdateFollowButton();
                        ShowToast("Following " + ArtistProfileTitle.Text);
                    }
                    else
                    {
                        ShowToast("Failed to follow artist");
                    }
                }
                else
                {
                    // Unsubscribe
                    if (!string.IsNullOrEmpty(_currentSubscriptionId))
                    {
                        var delRequest = new HttpRequestMessage(HttpMethod.Delete,
                            "https://www.googleapis.com/youtube/v3/subscriptions?id=" + _currentSubscriptionId);
                        delRequest.Headers.Add("Authorization", "Bearer " + accessToken);
                        var response = await _apiClient.SendAsync(delRequest);

                        if (response.IsSuccessStatusCode)
                        {
                            _isFollowingArtist = false;
                            _currentSubscriptionId = null;
                            UpdateFollowButton();
                            ShowToast("Unfollowed " + ArtistProfileTitle.Text);
                        }
                    }
                    else
                    {
                        _isFollowingArtist = false;
                        UpdateFollowButton();
                        ShowToast("Unfollowed " + ArtistProfileTitle.Text);
                    }
                }
            }
            catch
            {
                ShowToast("Network error");
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
            ArtistSongsList.ItemsSource = null;
            ArtistAlbumsList.ItemsSource = null;
            ArtistAlbumsSection.Visibility = Visibility.Collapsed;
            ArtistAboutSection.Visibility = Visibility.Collapsed;
            ArtistAboutImage.ImageSource = null;
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
