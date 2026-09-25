using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using YTMusicWP.Models;

namespace YTMusicWP.Services
{
    public static class DatabaseHelper
    {
        private static SQLiteAsyncConnection _db;
        private static bool _sqliteDisabled = false;

        public static async Task InitializeAsync()
        {
            if (_sqliteDisabled) return;

            try
            {
                SQLitePCL.Batteries_V2.Init();
            }
            catch (Exception pex)
            {
                Debug.WriteLine("[SQLite] Native SQLite not available: " + pex.Message + ". Using JSON storage fallback.");
                _sqliteDisabled = true;
                _db = null;
                return;
            }

            string dbPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "YTMusicWP.db3");
            try
            {
                _db = new SQLiteAsyncConnection(dbPath);

                // Create tables
                await _db.CreateTableAsync<HistoryEntity>();
                await _db.CreateTableAsync<FavoriteEntity>();
                await _db.CreateTableAsync<DownloadedEntity>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Database Initialize Error: " + ex.Message + ". Attempting repair...");
                try
                {
                    _db = null;
                    try
                    {
                        var file = await ApplicationData.Current.LocalFolder.GetFileAsync("YTMusicWP.db3");
                        if (file != null)
                        {
                            await file.DeleteAsync();
                        }
                    }
                    catch { }

                    _db = new SQLiteAsyncConnection(dbPath);
                    await _db.CreateTableAsync<HistoryEntity>();
                    await _db.CreateTableAsync<FavoriteEntity>();
                    await _db.CreateTableAsync<DownloadedEntity>();
                }
                catch (Exception retryEx)
                {
                    Debug.WriteLine("Database Repair Error: " + retryEx.Message + ". Using JSON storage fallback.");
                    _sqliteDisabled = true;
                    _db = null;
                }
            }
        }

        // ==========================================
        // HISTORY
        // ==========================================
        public static async Task AddOrUpdateHistoryAsync(YouTubeTrack track)
        {
            if (_db == null || track == null || string.IsNullOrEmpty(track.VideoId)) return;

            try
            {
                var existing = await _db.Table<HistoryEntity>().Where(x => x.VideoId == track.VideoId).FirstOrDefaultAsync();
                if (existing != null)
                {
                    existing.LastPlayedAt = DateTime.Now;
                    existing.PlayCount++;
                    // Update Title/Channel/Thumb in case it changed or was empty
                    if (!string.IsNullOrEmpty(track.Title)) existing.Title = track.Title;
                    if (!string.IsNullOrEmpty(track.ChannelName)) existing.ChannelName = track.ChannelName;
                    if (!string.IsNullOrEmpty(track.ThumbnailUrl)) existing.ThumbnailUrl = track.ThumbnailUrl;
                    await _db.UpdateAsync(existing);
                }
                else
                {
                    var entity = HistoryEntity.FromYouTubeTrack(track);
                    await _db.InsertAsync(entity);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("AddOrUpdateHistoryAsync Error: " + ex.Message);
            }
        }

        public static async Task<List<YouTubeTrack>> GetHistoryAsync(int limit = 100)
        {
            var results = new List<YouTubeTrack>();
            if (_db == null) return results;

            try
            {
                var entities = await _db.Table<HistoryEntity>()
                    .OrderByDescending(x => x.LastPlayedAt)
                    .Take(limit)
                    .ToListAsync();

                foreach (var e in entities)
                {
                    results.Add(e.ToYouTubeTrack());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetHistoryAsync Error: " + ex.Message);
            }
            return results;
        }

        public static async Task<List<YouTubeTrack>> GetMostPlayedAsync(int limit = 10)
        {
            var results = new List<YouTubeTrack>();
            if (_db == null) return results;

            try
            {
                var entities = await _db.Table<HistoryEntity>()
                    .Where(x => x.PlayCount > 0)
                    .OrderByDescending(x => x.PlayCount)
                    .Take(limit)
                    .ToListAsync();

                foreach (var e in entities)
                {
                    results.Add(e.ToYouTubeTrack());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetMostPlayedAsync Error: " + ex.Message);
            }
            return results;
        }

        public static async Task ClearHistoryAsync()
        {
            if (_db == null) return;
            try
            {
                await _db.DeleteAllAsync<HistoryEntity>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ClearHistoryAsync Error: " + ex.Message);
            }
        }

        public static async Task RemoveHistoryAsync(string videoId)
        {
            if (_db == null || string.IsNullOrEmpty(videoId)) return;
            try
            {
                var existing = await _db.Table<HistoryEntity>().Where(x => x.VideoId == videoId).FirstOrDefaultAsync();
                if (existing != null)
                {
                    await _db.DeleteAsync(existing);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RemoveHistoryAsync Error: " + ex.Message);
            }
        }

        // ==========================================
        // FAVORITES
        // ==========================================
        public static async Task AddFavoriteAsync(YouTubeTrack track)
        {
            if (_db == null || track == null || string.IsNullOrEmpty(track.VideoId)) return;
            try
            {
                var existing = await _db.Table<FavoriteEntity>().Where(x => x.VideoId == track.VideoId).FirstOrDefaultAsync();
                if (existing == null)
                {
                    var entity = FavoriteEntity.FromYouTubeTrack(track);
                    await _db.InsertAsync(entity);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("AddFavoriteAsync Error: " + ex.Message);
            }
        }

        public static async Task RemoveFavoriteAsync(string videoId)
        {
            if (_db == null || string.IsNullOrEmpty(videoId)) return;
            try
            {
                var existing = await _db.Table<FavoriteEntity>().Where(x => x.VideoId == videoId).FirstOrDefaultAsync();
                if (existing != null)
                {
                    await _db.DeleteAsync(existing);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RemoveFavoriteAsync Error: " + ex.Message);
            }
        }

        public static async Task<List<YouTubeTrack>> GetFavoritesAsync()
        {
            var results = new List<YouTubeTrack>();
            if (_db == null) return results;

            try
            {
                var entities = await _db.Table<FavoriteEntity>()
                    .OrderByDescending(x => x.AddedAt)
                    .ToListAsync();

                foreach (var e in entities)
                {
                    results.Add(e.ToYouTubeTrack());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetFavoritesAsync Error: " + ex.Message);
            }
            return results;
        }

        public static async Task ClearFavoritesAsync()
        {
            if (_db == null) return;
            try
            {
                await _db.DeleteAllAsync<FavoriteEntity>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ClearFavoritesAsync Error: " + ex.Message);
            }
        }

        // ==========================================
        // DOWNLOADS
        // ==========================================
        public static async Task AddOrUpdateDownloadedAsync(string fileName, YouTubeTrack track, string localThumbPath)
        {
            if (_db == null || string.IsNullOrEmpty(fileName) || track == null) return;
            try
            {
                var existing = await _db.Table<DownloadedEntity>().Where(x => x.FileName == fileName).FirstOrDefaultAsync();
                if (existing != null)
                {
                    existing.Title = track.Title;
                    existing.ChannelName = track.ChannelName;
                    existing.ThumbnailUrl = localThumbPath ?? track.ThumbnailUrl;
                    existing.DownloadedAt = DateTime.Now;
                    await _db.UpdateAsync(existing);
                }
                else
                {
                    var entity = new DownloadedEntity
                    {
                        FileName = fileName,
                        VideoId = track.VideoId,
                        Title = track.Title,
                        ChannelName = track.ChannelName,
                        ThumbnailUrl = localThumbPath ?? track.ThumbnailUrl,
                        DownloadedAt = DateTime.Now
                    };
                    await _db.InsertAsync(entity);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("AddOrUpdateDownloadedAsync Error: " + ex.Message);
            }
        }

        public static async Task<Dictionary<string, DownloadedEntity>> GetDownloadedMapAsync()
        {
            var map = new Dictionary<string, DownloadedEntity>(StringComparer.OrdinalIgnoreCase);
            if (_db == null) return map;
            try
            {
                var list = await _db.Table<DownloadedEntity>().ToListAsync();
                foreach (var item in list)
                {
                    if (!string.IsNullOrEmpty(item.FileName))
                    {
                        map[item.FileName] = item;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetDownloadedMapAsync Error: " + ex.Message);
            }
            return map;
        }

        public static async Task RemoveDownloadedAsync(string fileName)
        {
            if (_db == null || string.IsNullOrEmpty(fileName)) return;
            try
            {
                var existing = await _db.Table<DownloadedEntity>().Where(x => x.FileName == fileName).FirstOrDefaultAsync();
                if (existing != null)
                {
                    await _db.DeleteAsync(existing);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RemoveDownloadedAsync Error: " + ex.Message);
            }
        }
    }
}
