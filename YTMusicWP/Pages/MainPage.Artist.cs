using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private string _currentArtistChannelId;
        private string _currentArtistAvatarUrl;
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
                    // Try authenticated browse first (needed for private/user playlists)
                    string token = await GetAccessTokenAsync();
                    if (!string.IsNullOrEmpty(token))
                    {
                        string browseId = playlistId.StartsWith("VL") ? playlistId : "VL" + playlistId;
                        var json = await AuthInnerTubePostAsync("browse", new JObject { ["browseId"] = browseId }, token);
                        if (json["_error"] == null)
                        {
                            // Parse title
                            string plTitle = json?["metadata"]?["playlistMetadataRenderer"]?["title"]?.ToString();
                            if (!string.IsNullOrEmpty(plTitle))
                                PlaylistDetailsTitle.Text = plTitle;

                            // Parse tracks using lockupViewModel format
                            var tabs = json?["contents"]?["twoColumnBrowseResultsRenderer"]?["tabs"];
                            if (tabs != null && tabs.HasValues)
                            {
                                var sections = tabs[0]?["tabRenderer"]?["content"]?["sectionListRenderer"]?["contents"];
                                if (sections != null)
                                {
                                    foreach (var sec in sections)
                                    {
                                        var isr = sec["itemSectionRenderer"];
                                        if (isr == null) continue;
                                        var items = isr["contents"];
                                        if (items == null) continue;
                                        foreach (var item in items)
                                        {
                                            try
                                            {
                                                // Try lockupViewModel (new YouTube format)
                                                var lvm = item["lockupViewModel"];
                                                if (lvm != null)
                                                {
                                                    string videoId = lvm["contentId"]?.ToString();
                                                    if (string.IsNullOrEmpty(videoId)) continue;
                                                    string title = lvm["metadata"]?["lockupMetadataViewModel"]
                                                        ?["title"]?["content"]?.ToString() ?? "";
                                                    string artist = "";
                                                    var rows = lvm["metadata"]?["lockupMetadataViewModel"]
                                                        ?["metadata"]?["contentMetadataViewModel"]?["metadataRows"];
                                                    if (rows != null && rows.HasValues)
                                                    {
                                                        var parts = rows[0]?["metadataParts"];
                                                        if (parts != null && parts.HasValues)
                                                            artist = parts[0]?["text"]?["content"]?.ToString() ?? "";
                                                    }
                                                    string thumbUrl = "";
                                                    var sources = lvm["contentImage"]?["collectionThumbnailViewModel"]
                                                        ?["primaryThumbnail"]?["thumbnailViewModel"]?["image"]?["sources"];
                                                    if (sources != null && sources.HasValues)
                                                        thumbUrl = sources[0]?["url"]?.ToString() ?? "";
                                                    if (string.IsNullOrEmpty(thumbUrl))
                                                        thumbUrl = "https://i.ytimg.com/vi/" + videoId + "/hqdefault.jpg";

                                                    tracks.Add(new YouTubeTrack
                                                    {
                                                        VideoId = videoId,
                                                        Title = title,
                                                        ChannelName = artist,
                                                        ThumbnailUrl = thumbUrl
                                                    });
                                                }

                                                // Try playlistVideoRenderer (classic format)
                                                var pvr = item["playlistVideoRenderer"];
                                                if (pvr != null)
                                                {
                                                    string videoId = pvr["videoId"]?.ToString();
                                                    if (string.IsNullOrEmpty(videoId)) continue;
                                                    string title = pvr["title"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                                    string artist = pvr["shortBylineText"]?["runs"]?[0]?["text"]?.ToString() ?? "";
                                                    string thumbUrl = "";
                                                    var thumbs = pvr["thumbnail"]?["thumbnails"];
                                                    if (thumbs != null && thumbs.HasValues)
                                                        thumbUrl = thumbs.Last?["url"]?.ToString() ?? "";
                                                    if (string.IsNullOrEmpty(thumbUrl))
                                                        thumbUrl = "https://i.ytimg.com/vi/" + videoId + "/hqdefault.jpg";

                                                    tracks.Add(new YouTubeTrack
                                                    {
                                                        VideoId = videoId,
                                                        Title = title,
                                                        ChannelName = artist,
                                                        ThumbnailUrl = thumbUrl
                                                    });
                                                }
                                            }
                                            catch { continue; }
                                        }
                                    }
                                }
                            }

                            // Try sidebar thumbnail
                            try
                            {
                                var sidebar = json?["sidebar"];
                                if (sidebar != null)
                                {
                                    string sidebarStr = sidebar.ToString();
                                    string tMarker = "\"url\":\"https://i.ytimg.com";
                                    int tIdx = sidebarStr.IndexOf(tMarker);
                                    if (tIdx >= 0)
                                    {
                                        int tStart = tIdx + 7;
                                        int tEnd = sidebarStr.IndexOf("\"", tStart);
                                        if (tEnd > tStart)
                                            proxyThumbnail = sidebarStr.Substring(tStart, tEnd - tStart);
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    // Fallback: unauthenticated browse (for public playlists/albums)
                    if (tracks.Count == 0)
                    {
                        var plResult = await InnerTubeClient.BrowsePlaylistAsync(playlistId);
                        proxyThumbnail = plResult.ThumbnailUrl;
                        if (!string.IsNullOrEmpty(plResult.Title))
                            PlaylistDetailsTitle.Text = plResult.Title;
                        foreach (var t in plResult.Tracks)
                            tracks.Add(t);
                    }
                }
                catch { useFallback = true; }

                if (useFallback)
                {
                    // Only search as fallback when InnerTube browse actually failed
                    // Do NOT search for empty playlists — they are intentionally empty
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
                
                _currentViewingYtPlaylistId = playlistId;
                _currentViewingPlaylist = new UserPlaylist { Name = playlistName, Tracks = tracks };
                PlaylistSongsList.ItemsSource = _currentViewingPlaylist.Tracks;
                PlaylistDetailsTrackCount.Text = tracks.Count + " tracks";
            }
            catch { ShowToast("Failed to load playlist"); }
        }

        private async void OpenArtistProfile(string channelId, string channelName, bool trustChannelId = false)
        {
            _currentArtistChannelId = channelId;
            _currentArtistAvatarUrl = "";
            _isFollowingArtist = _youtubeSubscriptions.Any(s => s.ChannelId == channelId);
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

            // When channelId is trusted (from Library/Search), browse directly first
            if (trustChannelId && !string.IsNullOrEmpty(channelId))
            {
                try
                {
                    var artistResult = await InnerTubeClient.BrowseArtistAsync(channelId);
                    if (artistResult.Tracks != null && artistResult.Tracks.Count > 0)
                    {
                        tracks = artistResult.Tracks;
                        avatarUrl = artistResult.AvatarUrl;
                        _currentArtistAvatarUrl = avatarUrl;

                        if (!string.IsNullOrEmpty(artistResult.CoverUrl))
                            ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(artistResult.CoverUrl))) { DecodePixelWidth = 480 };
                        else if (!string.IsNullOrEmpty(avatarUrl))
                            ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(avatarUrl))) { DecodePixelWidth = 480 };

                        if (!string.IsNullOrEmpty(artistResult.Name) && artistResult.Name != "Artist")
                            ArtistProfileTitle.Text = artistResult.Name;
                        if (artistResult.Albums != null && artistResult.Albums.Count > 0)
                            albums = artistResult.Albums;
                        subscriberCount = artistResult.SubscriberCount;
                        description = artistResult.Description;
                    }
                }
                catch { }
            }

            // Search YouTube Music for artist (preferred when channelId not trusted)
            if ((tracks == null || tracks.Count == 0) && !string.IsNullOrEmpty(channelName))
            {
                try
                {
                    var searchResults = await InnerTubeClient.SearchAsync(channelName, 10);
                    var artistMatch = searchResults.FirstOrDefault(r =>
                        r.VideoId != null && r.VideoId.StartsWith("CHANNEL:") &&
                        r.Title == channelName); // Exact case-sensitive match

                    // If no exact match, try case-insensitive
                    if (artistMatch == null)
                        artistMatch = searchResults.FirstOrDefault(r =>
                            r.VideoId != null && r.VideoId.StartsWith("CHANNEL:") &&
                            r.Title.Equals(channelName, StringComparison.OrdinalIgnoreCase));

                    if (artistMatch != null)
                    {
                        string ytmChannelId = artistMatch.VideoId.Replace("CHANNEL:", "");
                        _currentArtistChannelId = ytmChannelId;

                        var artistResult = await InnerTubeClient.BrowseArtistAsync(ytmChannelId);
                        if (artistResult.Tracks != null && artistResult.Tracks.Count > 0)
                            tracks = artistResult.Tracks;
                        avatarUrl = artistResult.AvatarUrl;
                        _currentArtistAvatarUrl = avatarUrl;

                        if (!string.IsNullOrEmpty(artistResult.CoverUrl))
                            ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(artistResult.CoverUrl))) { DecodePixelWidth = 480 };
                        else if (!string.IsNullOrEmpty(avatarUrl))
                            ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(avatarUrl))) { DecodePixelWidth = 480 };

                        if (!string.IsNullOrEmpty(artistResult.Name) && artistResult.Name != "Artist")
                            ArtistProfileTitle.Text = artistResult.Name;
                        if (artistResult.Albums != null && artistResult.Albums.Count > 0)
                            albums = artistResult.Albums;
                        subscriberCount = artistResult.SubscriberCount;
                        description = artistResult.Description;
                    }
                }
                catch { }
            }

            // Fallback to channelId browse
            if ((tracks == null || tracks.Count == 0) && !string.IsNullOrEmpty(channelId))
            {
                try
                {
                    var artistResult = await InnerTubeClient.BrowseArtistAsync(channelId);
                    if (artistResult.Tracks != null && artistResult.Tracks.Count > 0)
                    {
                        tracks = artistResult.Tracks;
                        avatarUrl = artistResult.AvatarUrl;
                        _currentArtistAvatarUrl = avatarUrl;

                        if (!string.IsNullOrEmpty(artistResult.CoverUrl))
                            ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(artistResult.CoverUrl))) { DecodePixelWidth = 480 };
                        else if (!string.IsNullOrEmpty(avatarUrl))
                            ArtistProfileCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetHighResThumbnail(avatarUrl))) { DecodePixelWidth = 480 };

                        if (!string.IsNullOrEmpty(artistResult.Name) && artistResult.Name != "Artist")
                            ArtistProfileTitle.Text = artistResult.Name;
                        if (artistResult.Albums != null && artistResult.Albums.Count > 0)
                            albums = artistResult.Albums;
                        subscriberCount = artistResult.SubscriberCount;
                        description = artistResult.Description;
                    }
                }
                catch { }
            }

            // Final fallback — search songs
            if (tracks == null || tracks.Count == 0)
            {
                string query = channelName ?? "";
                tracks = await FetchMusicList(query, "", "songs");
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

            // Re-check follow status now that artist name is resolved
            UpdateFollowButton();

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

            // Check if already following (from local cache)
            CheckFollowStatusLocal(channelId);
        }

        private void CheckFollowStatusLocal(string channelId)
        {
            if (string.IsNullOrEmpty(channelId)) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings.Values;
                string followedJson = settings.ContainsKey("FollowedArtists") ? settings["FollowedArtists"]?.ToString() : "[]";
                var followed = JArray.Parse(followedJson ?? "[]");
                _isFollowingArtist = followed.Any(f => f.ToString() == channelId);
                UpdateFollowButton();
            }
            catch { }
        }

        private void SaveFollowState(string channelId, bool isFollowing)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings.Values;
                string followedJson = settings.ContainsKey("FollowedArtists") ? settings["FollowedArtists"]?.ToString() : "[]";
                var followed = JArray.Parse(followedJson ?? "[]");

                if (isFollowing)
                {
                    if (!followed.Any(f => f.ToString() == channelId))
                        followed.Add(channelId);
                }
                else
                {
                    var toRemove = followed.FirstOrDefault(f => f.ToString() == channelId);
                    if (toRemove != null) followed.Remove(toRemove);
                }

                settings["FollowedArtists"] = followed.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch { }
        }

        private void UpdateFollowButton()
        {
            // Check subscriptions by channelId OR by artist name
            if (!string.IsNullOrEmpty(_currentArtistChannelId))
                _isFollowingArtist = _youtubeSubscriptions.Any(s => s.ChannelId == _currentArtistChannelId);

            // Also check by name if channelId didn't match (YTM channelId may differ from subscription channelId)
            if (!_isFollowingArtist)
            {
                string displayName = ArtistProfileTitle.Text;
                if (!string.IsNullOrEmpty(displayName) && displayName != "Unknown Artist")
                    _isFollowingArtist = _youtubeSubscriptions.Any(s =>
                        s.Title.Equals(displayName, StringComparison.OrdinalIgnoreCase));
            }

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


        private async void ArtistFollow_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentArtistChannelId))
            {
                ShowToast("Cannot follow this artist");
                return;
            }

            // Require login to follow/subscribe
            string accessToken = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                ShowToast("Sign in to follow artists");
                return;
            }

            ArtistFollowBtn.IsEnabled = false;

            try
            {
                if (!_isFollowingArtist)
                {
                    // Subscribe via InnerTube
                    bool apiSuccess = false;
                    try
                    {
                        var extra = new JObject
                        {
                            ["channelIds"] = new JArray { _currentArtistChannelId },
                            ["params"] = "EgIIAhgA"
                        };
                        var result = await AuthInnerTubePostAsync("subscription/subscribe", extra, accessToken);
                        apiSuccess = result["_error"] == null;
                    }
                    catch { }

                    if (apiSuccess)
                    {
                        _isFollowingArtist = true;
                        SaveFollowState(_currentArtistChannelId, true);
                        // Add to local subscriptions list so UpdateFollowButton stays in sync
                        if (!_youtubeSubscriptions.Any(s => s.ChannelId == _currentArtistChannelId))
                        {
                            _youtubeSubscriptions.Add(new YouTubeSubscription
                            {
                                ChannelId = _currentArtistChannelId,
                                Title = ArtistProfileTitle.Text,
                                ThumbnailUrl = _currentArtistAvatarUrl ?? ""
                            });
                        }
                        UpdateFollowButton();
                        RefreshLibraryList();
                        ShowToast("Subscribed to " + ArtistProfileTitle.Text);
                    }
                    else
                    {
                        ShowToast("Failed to subscribe");
                    }
                }
                else
                {
                    // Unsubscribe via InnerTube
                    bool apiSuccess = false;
                    try
                    {
                        var extra = new JObject
                        {
                            ["channelIds"] = new JArray { _currentArtistChannelId }
                        };
                        var result = await AuthInnerTubePostAsync("subscription/unsubscribe", extra, accessToken);
                        apiSuccess = result["_error"] == null;
                    }
                    catch { }

                    if (apiSuccess)
                    {
                        _isFollowingArtist = false;
                        SaveFollowState(_currentArtistChannelId, false);
                        // Remove from local subscriptions list so UpdateFollowButton stays in sync
                        var toRemove = _youtubeSubscriptions.FirstOrDefault(s => s.ChannelId == _currentArtistChannelId);
                        if (toRemove != null) _youtubeSubscriptions.Remove(toRemove);
                        // Also try by name
                        var byName = _youtubeSubscriptions.FirstOrDefault(s =>
                            s.Title.Equals(ArtistProfileTitle.Text, StringComparison.OrdinalIgnoreCase));
                        if (byName != null) _youtubeSubscriptions.Remove(byName);
                        UpdateFollowButton();
                        RefreshLibraryList();
                        ShowToast("Unsubscribed from " + ArtistProfileTitle.Text);
                    }
                    else
                    {
                        ShowToast("Failed to unsubscribe");
                    }
                }
            }
            catch { }
            finally
            {
                ArtistFollowBtn.IsEnabled = true;
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
