using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        private async Task LoadFavoritesAsync()
        {
            if (favoriteTracks.Count > 0) return;
            try
            {
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                StorageFile file = await folder.GetFileAsync("favorites.json");
                string json = await FileIO.ReadTextAsync(file);
                JArray array = JArray.Parse(json);
                favoriteTracks.Clear();
                foreach (var item in array)
                {
                    favoriteTracks.Add(new YouTubeTrack
                    {
                        VideoId = item["VideoId"]?.ToString(),
                        Title = item["Title"]?.ToString(),
                        ChannelName = item["ChannelName"]?.ToString(),
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString()
                    });
                }
            }
            catch { }
        }

        private async Task LoadHistoryAsync()
        {
            if (historyTracks.Count > 0) return;
            try
            {
                StorageFolder folder = ApplicationData.Current.LocalFolder;
                StorageFile file = await folder.GetFileAsync("history.json");
                string json = await FileIO.ReadTextAsync(file);
                JArray array = JArray.Parse(json);
                historyTracks.Clear();
                foreach (var item in array)
                {
                    historyTracks.Add(new YouTubeTrack
                    {
                        VideoId = item["VideoId"]?.ToString(),
                        Title = item["Title"]?.ToString(),
                        ChannelName = item["ChannelName"]?.ToString(),
                        ChannelId = item["ChannelId"]?.ToString(),
                        ThumbnailUrl = item["ThumbnailUrl"]?.ToString()
                    });
                }
                RefreshHomeHistorySections();
            }
            catch { }
        }

        private async Task LoadDownloadsAsync()
        {
            try
            {
                var files = await ApplicationData.Current.LocalFolder.GetFilesAsync();

                // FIX #4: Smart diff — chỉ thêm/xóa item thay đổi, tránh UI flicker
                var currentFileNames = new HashSet<string>();
                foreach (var file in files)
                {
                    if (file.Name.EndsWith(".m4a") && !file.Name.StartsWith("temp_play_"))
                    {
                        currentFileNames.Add(file.Name);
                        string localId = "LOCAL:" + file.Name;
                        if (!downloadedTracks.Any(t => t.VideoId == localId))
                        {
                            downloadedTracks.Add(new YouTubeTrack
                            {
                                VideoId = localId,
                                Title = file.Name.Replace(".m4a", ""),
                                ChannelName = "Offline Track",
                                ThumbnailUrl = "ms-appx:///Assets/Square71x71Logo.scale-240.png"
                            });
                        }
                    }
                }

                // Xóa các track đã bị xóa khỏi ổ đĩa (chỉ xử lý LOCAL: tracks)
                for (int i = downloadedTracks.Count - 1; i >= 0; i--)
                {
                    if (!downloadedTracks[i].VideoId.StartsWith("LOCAL:")) continue;
                    string fileName = downloadedTracks[i].VideoId.Substring(6); // "LOCAL:".Length = 6
                    if (!currentFileNames.Contains(fileName))
                    {
                        downloadedTracks.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        private async Task LoadPlaylistsAsync()
        {
            if (userPlaylists.Count > 0) return;
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync("playlists.json");
                string json = await FileIO.ReadTextAsync(file);
                JArray pArray = JArray.Parse(json);
                userPlaylists.Clear();
                foreach (var pObj in pArray)
                {
                    UserPlaylist up = new UserPlaylist { Name = pObj["Name"]?.ToString() };
                    var tArray = pObj["Tracks"] as JArray;
                    if (tArray != null)
                    {
                        foreach (var item in tArray)
                        {
                            up.Tracks.Add(new YouTubeTrack
                            {
                                VideoId = item["VideoId"]?.ToString(),
                                Title = item["Title"]?.ToString(),
                                ChannelName = item["ChannelName"]?.ToString(),
                                ThumbnailUrl = item["ThumbnailUrl"]?.ToString()
                            });
                        }
                    }
                    userPlaylists.Add(up);
                }
            }
            catch { }
        }

        private async void SavePlaylistsAsync()
        {
            try
            {
                JArray pArray = new JArray();
                foreach (var p in userPlaylists)
                {
                    JObject pObj = new JObject();
                    pObj["Name"] = p.Name;
                    JArray tArray = new JArray();
                    foreach (var t in p.Tracks)
                    {
                        JObject tObj = new JObject();
                        tObj["VideoId"] = t.VideoId; tObj["Title"] = t.Title;
                        tObj["ChannelName"] = t.ChannelName; tObj["ThumbnailUrl"] = t.ThumbnailUrl;
                        tArray.Add(tObj);
                    }
                    pObj["Tracks"] = tArray;
                    pArray.Add(pObj);
                }
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("playlists.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, pArray.ToString());
            }
            catch { }
        }

        private void OpenCreatePlaylistDialog_Click(object sender, RoutedEventArgs e)
        {
            NewPlaylistNameTextBox.Text = "";
            CreatePlaylistDialog.Visibility = Visibility.Visible;
        }

    }
}
