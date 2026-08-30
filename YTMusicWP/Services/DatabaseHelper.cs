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

        public static async Task InitializeAsync()
        {
            try
            {
                var dbPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "YTMusicWP.db3");
                _db = new SQLiteAsyncConnection(dbPath);

                // Create tables
                await _db.CreateTableAsync<HistoryEntity>();
                await _db.CreateTableAsync<FavoriteEntity>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Database Initialize Error: " + ex.Message);
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

        public static async Task ClearHistoryAsync()
        {
            if (_db != null) await _db.DropTableAsync<HistoryEntity>();
            if (_db != null) await _db.CreateTableAsync<HistoryEntity>();
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
            if (_db != null) await _db.DropTableAsync<FavoriteEntity>();
            if (_db != null) await _db.CreateTableAsync<FavoriteEntity>();
        }
    }
}
