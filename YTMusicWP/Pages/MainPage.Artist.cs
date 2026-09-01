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

        public async void OpenYouTubePlaylist(string playlistId, string playlistName, string coverUrl = null)
        {
            try
            {
                PlaylistDetailsTitle.Text = playlistName;
                PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetSquareThumbnail(coverUrl), UriKind.Absolute)) { DecodePixelWidth = 220 };
                    PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                }
                PlaylistSongsList.ItemsSource = null;
                PlaylistDetailsView.Visibility = Visibility.Visible;
                PlaylistSlideInStoryboard.Begin();
                
                var tracks = new System.Collections.ObjectModel.ObservableCollection<YouTubeTrack>();
                _playlistContinuationToken = null;
                _isLoadingMorePlaylist = false;

                bool isLocalPlaylist = playlistId.StartsWith("LOCAL_");
                if (isLocalPlaylist)
                {
                    var localTracks = await LoadLocalPlaylistTracksAsync(playlistId);
                    foreach (var t in localTracks) tracks.Add(t);
                    PlaylistDetailsSubtitle.Text = localTracks.Count + " tracks";
                }
                else
                {
                    var plResult = await InnerTubeClient.BrowsePlaylistAsync(playlistId);
                    if (!string.IsNullOrEmpty(plResult.Title))
                        PlaylistDetailsTitle.Text = plResult.Title;

                    if (!string.IsNullOrEmpty(plResult.Subtitle))
                    {
                        PlaylistDetailsSubtitle.Text = plResult.Subtitle;
                        // Sometimes Subtitle string can be dirty with redundant bullets, cleanup
                        PlaylistDetailsSubtitle.Text = PlaylistDetailsSubtitle.Text.Trim(' ', '•');
                    }
                    else
                        PlaylistDetailsSubtitle.Text = plResult.Tracks.Count + " tracks";

                    foreach (var t in plResult.Tracks)
                        tracks.Add(t);

                    _playlistContinuationToken = plResult.ContinuationToken;

                    // If no cover was set, try proxy thumbnail or first track's thumbnail
                    if (PlaylistDetailsCoverRect.Visibility == Visibility.Collapsed && tracks.Count > 0)
                    {
                        string fallbackCover = plResult.ThumbnailUrl;
                        if (string.IsNullOrEmpty(fallbackCover))
                        {
                            fallbackCover = tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.ThumbnailUrl))?.ThumbnailUrl;
                        }
                        if (!string.IsNullOrEmpty(fallbackCover))
                        {
                            try
                            {
                                PlaylistDetailsCoverBrush.ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(GetSquareThumbnail(fallbackCover), UriKind.Absolute)) { DecodePixelWidth = 220 };
                                PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                            }
                            catch { }
                        }
                    }
                }
                
                _currentViewingYtPlaylistId = playlistId;
                _currentViewingPlaylist = new UserPlaylist { Name = playlistName, Tracks = tracks };
                PlaylistSongsList.ItemsSource = _currentViewingPlaylist.Tracks;
                PlaylistDetailsTrackCount.Text = tracks.Count + (string.IsNullOrEmpty(_playlistContinuationToken) ? "" : "+") + " tracks";

                HookPlaylistSongsScroll(); // Make sure scroll is hooked for continuation
            }
            catch { ShowToast("Failed to load playlist"); }
        }

        private bool ApplyArtistProfileResult(ArtistResult artistResult, ref List<YouTubeTrack> tracks, ref List<ArtistAlbum> albums, ref string subscriberCount, ref string description, ref string avatarUrl)
        {
            if (artistResult != null && artistResult.Tracks != null && artistResult.Tracks.Count > 0)
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
                return true;
            }
            return false;
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
            ArtistSectionsControl.ItemsSource = null;
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
                    ApplyArtistProfileResult(artistResult, ref tracks, ref albums, ref subscriberCount, ref description, ref avatarUrl);
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
                        ApplyArtistProfileResult(artistResult, ref tracks, ref albums, ref subscriberCount, ref description, ref avatarUrl);
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
                    ApplyArtistProfileResult(artistResult, ref tracks, ref albums, ref subscriberCount, ref description, ref avatarUrl);
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

            // Sections carousel
            if (albums != null && albums.Count > 0)
            {
                var groups = albums.GroupBy(a => a.SectionTitle)
                                   .Select(g => new ArtistSectionGroup { Title = g.Key, Items = g.ToList() })
                                   .ToList();
                ArtistSectionsControl.ItemsSource = groups;
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
            if (string.IsNullOrEmpty(accessToken) && !InnerTubeClient.HasCookieAuth)
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
                        var result = await InnerTubeClient.AuthInnerTubePostAsync("subscription/subscribe", extra, accessToken);
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
                        var result = await InnerTubeClient.AuthInnerTubePostAsync("subscription/unsubscribe", extra, accessToken);
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
            ArtistSectionsControl.ItemsSource = null;
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

            // If it has a videoId, play it!
            if (!string.IsNullOrEmpty(album.VideoId))
            {
                var track = new YouTubeTrack
                {
                    VideoId = album.VideoId,
                    Title = album.Title,
                    ChannelName = ArtistProfileTitle.Text,
                    ThumbnailUrl = album.ThumbnailUrl
                };

                // If there's a playlist context attached, we could pass it, but for single video just play it
                PlayTrack(track);
                return;
            }

            // If browseId looks like a playlist or artist, open it
            if (!string.IsNullOrEmpty(album.BrowseId))
            {
                string id = album.BrowseId;
                if (id.StartsWith("UC") || id.StartsWith("FEmusic_library_privately_owned_artist"))
                {
                    // It's an artist! Open Artist profile
                    OpenArtistProfile(id, album.Title, true);
                }
                else if (id.StartsWith("MPREb_"))
                {
                    // Album browseId — browse as playlist
                    OpenYouTubePlaylist(id, album.Title, album.ThumbnailUrl);
                }
                else if (id.StartsWith("VL") || id.StartsWith("PL"))
                {
                    OpenYouTubePlaylist(id.Replace("VL", ""), album.Title, album.ThumbnailUrl);
                }
                else
                {
                    // Try to browse as playlist anyway  
                    OpenYouTubePlaylist(id, album.Title, album.ThumbnailUrl);
                }
            }
        }
        private void ArtistAbout_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (ArtistAboutDescription.MaxLines == 3)
            {
                ArtistAboutDescription.MaxLines = 0;
            }
            else
            {
                ArtistAboutDescription.MaxLines = 3;
            }
        }

    }

    public class ArtistSectionGroup
    {
        public string Title { get; set; }
        public List<ArtistAlbum> Items { get; set; }
    }
}

