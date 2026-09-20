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
                    var coverBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    coverBmp.DecodePixelWidth = 220;
                    coverBmp.UriSource = new Uri(GetSquareThumbnail(coverUrl), UriKind.Absolute);
                    PlaylistDetailsCoverBrush.ImageSource = coverBmp;
                    PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                }
                PlaylistSongsList.ItemsSource = null;
                ResetPlaylistFilter();
                _currentPlaylistFullTracks = null;
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
                    string token = await GetAccessTokenAsync();
                    var plResult = await InnerTubeClient.BrowsePlaylistAsync(playlistId, null, token);
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

                    string effectiveCover = !string.IsNullOrEmpty(plResult.ThumbnailUrl) ? plResult.ThumbnailUrl : coverUrl;
                    if (playlistId.StartsWith("MPREb_") || playlistId.StartsWith("OLAK5uy_"))
                    {
                        if (!string.IsNullOrEmpty(effectiveCover))
                        {
                            foreach (var t in plResult.Tracks)
                            {
                                t.ThumbnailUrl = effectiveCover;
                            }
                        }
                    }

                    foreach (var t in plResult.Tracks)
                        tracks.Add(t);

                    _playlistContinuationToken = plResult.ContinuationToken;

                    // If cover is available, ensure header cover is set
                    if (!string.IsNullOrEmpty(effectiveCover) && (PlaylistDetailsCoverRect.Visibility == Visibility.Collapsed || !string.IsNullOrEmpty(plResult.ThumbnailUrl)))
                    {
                        try
                        {
                            var headerCoverBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            headerCoverBmp.DecodePixelWidth = 220;
                            headerCoverBmp.UriSource = new Uri(GetSquareThumbnail(effectiveCover), UriKind.Absolute);
                            PlaylistDetailsCoverBrush.ImageSource = headerCoverBmp;
                            PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                        }
                        catch { }
                    }
                    else if (PlaylistDetailsCoverRect.Visibility == Visibility.Collapsed && tracks.Count > 0)
                    {
                        string fallbackCover = tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.ThumbnailUrl))?.ThumbnailUrl;
                        if (!string.IsNullOrEmpty(fallbackCover))
                        {
                            try
                            {
                                var fallbackCoverBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                                fallbackCoverBmp.DecodePixelWidth = 220;
                                fallbackCoverBmp.UriSource = new Uri(GetSquareThumbnail(fallbackCover), UriKind.Absolute);
                                PlaylistDetailsCoverBrush.ImageSource = fallbackCoverBmp;
                                PlaylistDetailsCoverRect.Visibility = Visibility.Visible;
                            }
                            catch { }
                        }
                    }
                }
                
                _currentViewingYtPlaylistId = playlistId;
                _currentViewingPlaylist = new UserPlaylist { Name = playlistName, Tracks = tracks };
                SetPlaylistViewTracks(_currentViewingPlaylist.Tracks, tracks.Count + (string.IsNullOrEmpty(_playlistContinuationToken) ? "" : "+") + " tracks");

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
                {
                    var artistCoverBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    artistCoverBmp.DecodePixelWidth = Services.MemoryHelper.IsLowMemoryDevice ? 320 : 480;
                    artistCoverBmp.UriSource = new Uri(GetHighResThumbnail(artistResult.CoverUrl), UriKind.Absolute);
                    ArtistProfileCover.Source = artistCoverBmp;
                }
                else if (!string.IsNullOrEmpty(avatarUrl))
                {
                    var artistAvatarBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    artistAvatarBmp.DecodePixelWidth = Services.MemoryHelper.IsLowMemoryDevice ? 320 : 480;
                    artistAvatarBmp.UriSource = new Uri(GetHighResThumbnail(avatarUrl), UriKind.Absolute);
                    ArtistProfileCover.Source = artistAvatarBmp;
                }

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
                    // Verify that the browsed artist name matches the requested channelName (prevent mismatched show/label channel hijacking)
                    if (artistResult != null && !string.IsNullOrEmpty(artistResult.Name) && !string.IsNullOrEmpty(channelName))
                    {
                        if (!InnerTubeClient.IsArtistNameMatch(artistResult.Name, channelName))
                        {
                            // Mismatched channel! Purge poisoned cache and reject
                            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                            string ckId = "AvatarChId_" + channelName.ToLowerInvariant();
                            string ckAv = "AvatarCache_" + channelName.ToLowerInvariant();
                            if (localSettings.ContainsKey(ckId)) localSettings.Remove(ckId);
                            if (localSettings.ContainsKey(ckAv)) localSettings.Remove(ckAv);

                            artistResult = null;
                        }
                    }

                    if (artistResult != null)
                    {
                        ApplyArtistProfileResult(artistResult, ref tracks, ref albums, ref subscriberCount, ref description, ref avatarUrl);
                    }
                }
                catch { }
            }

            // Search YouTube Music for artist using high-precision FindArtistAsync
            if ((tracks == null || tracks.Count == 0) && !string.IsNullOrEmpty(channelName))
            {
                try
                {
                    var artistMatch = await InnerTubeClient.FindArtistAsync(channelName);
                    if (artistMatch != null && !string.IsNullOrEmpty(artistMatch.ChannelId))
                    {
                        string ytmChannelId = artistMatch.ChannelId.Replace("CHANNEL:", "");
                        _currentArtistChannelId = ytmChannelId;

                        // Save verified channelId into cache
                        var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                        localSettings["AvatarChId_" + channelName.ToLowerInvariant()] = ytmChannelId;
                        if (!string.IsNullOrEmpty(artistMatch.ThumbnailUrl))
                        {
                            string av = GetArtistAvatar(artistMatch.ThumbnailUrl);
                            if (!string.IsNullOrEmpty(av))
                                localSettings["AvatarCache_" + channelName.ToLowerInvariant()] = av;
                        }

                        var artistResult = await InnerTubeClient.BrowseArtistAsync(ytmChannelId);
                        ApplyArtistProfileResult(artistResult, ref tracks, ref albums, ref subscriberCount, ref description, ref avatarUrl);
                    }
                }
                catch { }
            }

            // Fallback to channelId browse if channelId was not trusted but passed
            if ((tracks == null || tracks.Count == 0) && !string.IsNullOrEmpty(channelId) && !trustChannelId)
            {
                try
                {
                    var artistResult = await InnerTubeClient.BrowseArtistAsync(channelId);
                    if (artistResult != null && !string.IsNullOrEmpty(artistResult.Name) && !string.IsNullOrEmpty(channelName))
                    {
                        if (!InnerTubeClient.IsArtistNameMatch(artistResult.Name, channelName))
                            artistResult = null;
                    }
                    if (artistResult != null)
                    {
                        ApplyArtistProfileResult(artistResult, ref tracks, ref albums, ref subscriberCount, ref description, ref avatarUrl);
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
                        var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                        bmp.DecodePixelWidth = Services.MemoryHelper.IsLowMemoryDevice ? 320 : 480;
                        bmp.UriSource = new Uri(GetHighResThumbnail(list[0].ThumbnailUrl), UriKind.Absolute);
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
                    var aboutBmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    aboutBmp.DecodePixelWidth = 300;
                    aboutBmp.UriSource = new Uri(GetHighResThumbnail(avatarUrl), UriKind.Absolute);
                    ArtistAboutImage.ImageSource = aboutBmp;
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

            // If browseId looks like an artist profile, open artist
            if (!string.IsNullOrEmpty(album.BrowseId) && (album.BrowseId.StartsWith("UC") || album.BrowseId.StartsWith("FEmusic_library_privately_owned_artist")))
            {
                OpenArtistProfile(album.BrowseId, album.Title, true);
                return;
            }

            // Open album, single, or playlist
            string targetId = !string.IsNullOrEmpty(album.BrowseId) ? album.BrowseId : album.PlaylistId;
            if (!string.IsNullOrEmpty(targetId))
            {
                if (targetId.StartsWith("VL") || targetId.StartsWith("PL"))
                    OpenYouTubePlaylist(targetId.Replace("VL", ""), album.Title, album.ThumbnailUrl);
                else
                    OpenYouTubePlaylist(targetId, album.Title, album.ThumbnailUrl);
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

