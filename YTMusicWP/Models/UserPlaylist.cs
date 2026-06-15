using System.Collections.ObjectModel;

namespace YTMusicWP
{
    public class UserPlaylist
    {
        public string Name { get; set; }
        public ObservableCollection<YouTubeTrack> Tracks { get; set; }
        public UserPlaylist() { Tracks = new ObservableCollection<YouTubeTrack>(); }
    }
}
