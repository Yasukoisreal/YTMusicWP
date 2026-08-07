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
        public class AppStorageStats
        {
            public ulong DownloadedBytes { get; set; }
            public int DownloadedCount { get; set; }
            public ulong ImageCacheBytes { get; set; }
            public int ImageCacheCount { get; set; }
            public ulong TempStreamBytes { get; set; }
            public int TempStreamCount { get; set; }
            public ulong DataCacheBytes { get; set; }
            public int DataCacheCount { get; set; }

            public ulong TotalBytes
            {
                get { return DownloadedBytes + ImageCacheBytes + TempStreamBytes + DataCacheBytes; }
            }
        }

        public static string FormatFileSize(ulong bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024 * 1024 * 1024)
                return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        }

        public async Task<AppStorageStats> CalculateStorageStatsAsync()
        {
            var stats = new AppStorageStats();

            try
            {
                // 1. Scan LocalFolder
                var localFolder = ApplicationData.Current.LocalFolder;
                var localFiles = await localFolder.GetFilesAsync();

                foreach (var file in localFiles)
                {
                    var props = await file.GetBasicPropertiesAsync();
                    ulong size = props.Size;
                    string nameLower = file.Name.ToLowerInvariant();

                    if (nameLower.EndsWith(".m4a") && !nameLower.StartsWith("temp_play_"))
                    {
                        stats.DownloadedBytes += size;
                        stats.DownloadedCount++;
                    }
                    else if (nameLower.EndsWith(".jpg") || nameLower.EndsWith(".jpeg") || nameLower.EndsWith(".png") || nameLower.EndsWith(".webp"))
                    {
                        stats.ImageCacheBytes += size;
                        stats.ImageCacheCount++;
                    }
                    else if (nameLower.StartsWith("temp_play_") || nameLower.EndsWith(".tmp"))
                    {
                        stats.TempStreamBytes += size;
                        stats.TempStreamCount++;
                    }
                    else if (nameLower.EndsWith(".json") || nameLower.EndsWith(".xml") || nameLower.EndsWith(".txt"))
                    {
                        stats.DataCacheBytes += size;
                        stats.DataCacheCount++;
                    }
                    else
                    {
                        stats.DataCacheBytes += size;
                        stats.DataCacheCount++;
                    }
                }
            }
            catch { }

            try
            {
                // 2. Scan TemporaryFolder
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                var tempFiles = await tempFolder.GetFilesAsync();

                foreach (var file in tempFiles)
                {
                    var props = await file.GetBasicPropertiesAsync();
                    stats.TempStreamBytes += props.Size;
                    stats.TempStreamCount++;
                }
            }
            catch { }

            return stats;
        }

        public async Task UpdateStorageDisplayAsync()
        {
            try
            {
                if (StorageLoadingRing != null) StorageLoadingRing.IsActive = true;
                if (StorageLoadingRing != null) StorageLoadingRing.Visibility = Visibility.Visible;

                var stats = await CalculateStorageStatsAsync();

                if (StorageTotalText != null)
                    StorageTotalText.Text = FormatFileSize(stats.TotalBytes) + " Used";

                if (StorageDownloadsSizeText != null)
                    StorageDownloadsSizeText.Text = FormatFileSize(stats.DownloadedBytes);

                if (StorageDownloadsCountText != null)
                    StorageDownloadsCountText.Text = "(" + stats.DownloadedCount + " songs)";

                if (StorageImagesSizeText != null)
                    StorageImagesSizeText.Text = FormatFileSize(stats.ImageCacheBytes);

                if (StorageImagesCountText != null)
                    StorageImagesCountText.Text = "(" + stats.ImageCacheCount + " files)";

                if (StorageTempSizeText != null)
                    StorageTempSizeText.Text = FormatFileSize(stats.TempStreamBytes);

                if (StorageTempCountText != null)
                    StorageTempCountText.Text = "(" + stats.TempStreamCount + " files)";

                if (StorageDataSizeText != null)
                    StorageDataSizeText.Text = FormatFileSize(stats.DataCacheBytes);
            }
            catch { }
            finally
            {
                if (StorageLoadingRing != null)
                {
                    StorageLoadingRing.IsActive = false;
                    StorageLoadingRing.Visibility = Visibility.Collapsed;
                }
            }
        }

        public async Task<int> CleanImageCacheInternalAsync()
        {
            int count = 0;
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var files = await folder.GetFilesAsync();
                foreach (var file in files)
                {
                    string name = file.Name.ToLowerInvariant();
                    if (name.EndsWith(".jpg") || name.EndsWith(".jpeg") || name.EndsWith(".png") || name.EndsWith(".webp"))
                    {
                        try { await file.DeleteAsync(); count++; } catch { }
                    }
                }
            }
            catch { }
            return count;
        }

        public async Task<int> CleanTempStreamsInternalAsync()
        {
            int count = 0;
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var files = await folder.GetFilesAsync();
                foreach (var file in files)
                {
                    string name = file.Name.ToLowerInvariant();
                    if (name.StartsWith("temp_play_") || name.EndsWith(".tmp"))
                    {
                        try { await file.DeleteAsync(); count++; } catch { }
                    }
                }
            }
            catch { }

            try
            {
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                var files = await tempFolder.GetFilesAsync();
                foreach (var file in files)
                {
                    try { await file.DeleteAsync(); count++; } catch { }
                }
            }
            catch { }

            return count;
        }

        public async Task<int> CleanAllCacheInternalAsync()
        {
            int count = 0;
            count += await CleanImageCacheInternalAsync();
            count += await CleanTempStreamsInternalAsync();

            // Clear YouTube browse/sub caches (safe: keeps user playlists & offline downloads & favorites/history)
            try
            {
                var f1 = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_playlists_cache.json");
                await f1.DeleteAsync();
                count++;
            }
            catch { }

            try
            {
                var f2 = await ApplicationData.Current.LocalFolder.GetFileAsync("yt_subs_cache.json");
                await f2.DeleteAsync();
                count++;
            }
            catch { }

            return count;
        }

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
                    if (historyTracks.Count >= 20) break; // Match PlayTrack cap — protect 512MB RAM
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
                // [OPT] Build existing-ID set once → O(1) lookup instead of O(n) .Any()
                var existingIds = new HashSet<string>();
                foreach (var t in downloadedTracks) existingIds.Add(t.VideoId);

                foreach (var file in files)
                {
                    if (file.Name.EndsWith(".m4a") && !file.Name.StartsWith("temp_play_"))
                    {
                        currentFileNames.Add(file.Name);
                        string localId = "LOCAL:" + file.Name;
                        if (!existingIds.Contains(localId))
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
