using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace YTMusicWP
{
    public static partial class InnerTubeClient
    {
        // -----------------------------------------------------
        // PLAYLIST & LIKE MANAGEMENT (Using WEB_REMIX client)
        // -----------------------------------------------------

        public static async Task<string> CreateYouTubePlaylistAsync(string title, string accessToken)
        {
            // TVHTML5 OAuth tokens are blocked from creating playlists by YouTube API (Precondition failed).
            // WEB_REMIX/ANDROID clients are blocked from using TVHTML5 OAuth tokens (Invalid argument).
            // Data API v3 is disabled for the TVHTML5 OAuth project.
            // Therefore, creating a YouTube playlist is physically impossible with Device Code flow.
            // We must fallback to local playlists.
            await Task.Delay(100);
            return "LOCAL_" + System.Guid.NewGuid().ToString("N");
        }

        public static async Task<bool> DeleteYouTubePlaylistAsync(string playlistId, string accessToken)
        {
            if (playlistId.StartsWith("LOCAL_")) return true;

            var extra = new JObject { ["playlistId"] = playlistId };
            var json = await AuthInnerTubePostAsync("playlist/delete", extra, accessToken, "TVHTML5", "7.20241016.00.00");
            return json["_error"] == null;
        }

        public static async Task<bool> RenameYouTubePlaylistAsync(string playlistId, string newTitle, string accessToken)
        {
            if (playlistId.StartsWith("LOCAL_")) return true;
            if (playlistId.StartsWith("VL")) playlistId = playlistId.Substring(2);

            var extra = new JObject
            {
                ["playlistId"] = playlistId,
                ["actions"] = new JArray
                {
                    new JObject
                    {
                        ["action"] = "ACTION_SET_PLAYLIST_NAME",
                        ["playlistName"] = newTitle
                    }
                }
            };
            var json = await AuthInnerTubePostAsync("browse/edit_playlist", extra, accessToken, "TVHTML5", "7.20241016.00.00");
            return json["_error"] == null;
        }

        public static async Task<string> AddToYouTubePlaylistAsync(string playlistId, string videoId, string accessToken)
        {
            if (playlistId.StartsWith("LOCAL_")) return null;
            if (playlistId.StartsWith("VL")) playlistId = playlistId.Substring(2);

            var extra = new JObject
            {
                ["playlistId"] = playlistId,
                ["actions"] = new JArray
                {
                    new JObject
                    {
                        ["action"] = "ACTION_ADD_VIDEO",
                        ["addedVideoId"] = videoId
                    }
                }
            };
            var json = await AuthInnerTubePostAsync("browse/edit_playlist", extra, accessToken, "TVHTML5", "7.20241016.00.00");
            
            // Extract the setVideoId returned by YouTube
            if (json["_error"] == null)
            {
                var editResults = json["playlistEditResults"] as JArray;
                if (editResults != null && editResults.Count > 0)
                {
                    string setVideoId = editResults[0]?.SelectToken("playlistEditVideoAddedResultData.setVideoId")?.ToString();
                    return setVideoId ?? "SUCCESS";
                }
                return "SUCCESS";
            }
            return null;
        }

        public static async Task<bool> RemoveFromYouTubePlaylistAsync(string playlistId, string videoId, string setVideoId, string accessToken)
        {
            if (playlistId.StartsWith("LOCAL_")) return true;
            if (playlistId.StartsWith("VL")) playlistId = playlistId.Substring(2);

            var extra = new JObject
            {
                ["playlistId"] = playlistId,
                ["actions"] = new JArray
                {
                    new JObject
                    {
                        ["action"] = "ACTION_REMOVE_VIDEO_BY_SET_VIDEO_ID",
                        ["removedVideoId"] = videoId,
                        ["setVideoId"] = setVideoId
                    }
                }
            };
            var json = await AuthInnerTubePostAsync("browse/edit_playlist", extra, accessToken, "TVHTML5", "7.20241016.00.00");
            return json["_error"] == null;
        }

        public static async Task<bool> LikeVideoAsync(string videoId, string accessToken)
        {
            var extra = new JObject
            {
                ["target"] = new JObject { ["videoId"] = videoId }
            };
            var json = await AuthInnerTubePostAsync("like/like", extra, accessToken, "TVHTML5", "7.20241016.00.00");
            return json["_error"] == null;
        }

        public static async Task<bool> UnlikeVideoAsync(string videoId, string accessToken)
        {
            var extra = new JObject
            {
                ["target"] = new JObject { ["videoId"] = videoId }
            };
            var json = await AuthInnerTubePostAsync("like/removelike", extra, accessToken, "TVHTML5", "7.20241016.00.00");
            return json["_error"] == null;
        }
    }
}
