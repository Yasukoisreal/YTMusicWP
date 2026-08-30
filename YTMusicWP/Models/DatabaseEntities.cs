using SQLite;
using System;

namespace YTMusicWP.Models
{
    [Table("History")]
    public class HistoryEntity
    {
        [PrimaryKey]
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string ChannelName { get; set; }
        public string ThumbnailUrl { get; set; }
        public DateTime LastPlayedAt { get; set; }
        public int PlayCount { get; set; }

        public YouTubeTrack ToYouTubeTrack()
        {
            return new YouTubeTrack
            {
                VideoId = this.VideoId,
                Title = this.Title,
                ChannelName = this.ChannelName,
                ThumbnailUrl = this.ThumbnailUrl
            };
        }

        public static HistoryEntity FromYouTubeTrack(YouTubeTrack track)
        {
            return new HistoryEntity
            {
                VideoId = track.VideoId,
                Title = track.Title,
                ChannelName = track.ChannelName,
                ThumbnailUrl = track.ThumbnailUrl,
                LastPlayedAt = DateTime.Now,
                PlayCount = 1
            };
        }
    }

    [Table("Favorites")]
    public class FavoriteEntity
    {
        [PrimaryKey]
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string ChannelName { get; set; }
        public string ThumbnailUrl { get; set; }
        public DateTime AddedAt { get; set; }

        public YouTubeTrack ToYouTubeTrack()
        {
            return new YouTubeTrack
            {
                VideoId = this.VideoId,
                Title = this.Title,
                ChannelName = this.ChannelName,
                ThumbnailUrl = this.ThumbnailUrl
            };
        }

        public static FavoriteEntity FromYouTubeTrack(YouTubeTrack track)
        {
            return new FavoriteEntity
            {
                VideoId = track.VideoId,
                Title = track.Title,
                ChannelName = track.ChannelName,
                ThumbnailUrl = track.ThumbnailUrl,
                AddedAt = DateTime.Now
            };
        }
    }
}
