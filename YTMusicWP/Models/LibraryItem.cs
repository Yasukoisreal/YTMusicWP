namespace YTMusicWP
{
    public class LibraryItem
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ThumbnailUrl { get; set; }
        public string IconGlyph { get; set; } // fallback icon if no thumbnail
        public bool IsCircle { get; set; }     // true for artists
        public string ItemType { get; set; }   // "favorites", "playlist", "ytplaylist", "artist", "downloads", "recent"
        public object Tag { get; set; }        // original object reference
    }
}
