using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Library tab. The library logic still lives in MainPage, which reads the named elements directly
    /// (x:FieldModifier="internal") and handles the events below; each event forwards the original sender,
    /// so handlers that inspect sender/DataContext (chips, sort items, song flyouts) keep working.
    /// </summary>
    public sealed partial class LibraryView : UserControl
    {
        public event RoutedEventHandler ExportAllPlaylistsM3uClick;
        public event RoutedEventHandler ExportPlaylistsClick;
        public event RoutedEventHandler ImportM3uPlaylistClick;
        public event RoutedEventHandler ImportPlaylistsClick;
        public event RoutedEventHandler LibChipClick;
        public event RoutedEventHandler LibSortOptionClick;
        public event RoutedEventHandler LibTileDownloadedClick;
        public event RoutedEventHandler LibTileFavoriteClick;
        public event RoutedEventHandler LibTileFollowedClick;
        public event RoutedEventHandler LibTileMostPlayedClick;
        public event RoutedEventHandler LibrarySyncClick;
        public event RoutedEventHandler MenuAddToPlaylistClick;
        public event RoutedEventHandler MenuDownloadClick;
        public event RoutedEventHandler MenuFavoriteClick;
        public event RoutedEventHandler MenuPlayClick;
        public event RoutedEventHandler NavCreateClick;
        public event RoutedEventHandler NavSearchClick;
        public event HoldingEventHandler SongItemHolding;
        public event ItemClickEventHandler LibraryUnifiedItemClick;
        public event ItemClickEventHandler SongListItemClick;

        public LibraryView()
        {
            this.InitializeComponent();
        }

        private void ExportAllPlaylistsM3u_Click(object sender, RoutedEventArgs e) { if (ExportAllPlaylistsM3uClick != null) ExportAllPlaylistsM3uClick(sender, e); }
        private void ExportPlaylists_Click(object sender, RoutedEventArgs e) { if (ExportPlaylistsClick != null) ExportPlaylistsClick(sender, e); }
        private void ImportM3uPlaylist_Click(object sender, RoutedEventArgs e) { if (ImportM3uPlaylistClick != null) ImportM3uPlaylistClick(sender, e); }
        private void ImportPlaylists_Click(object sender, RoutedEventArgs e) { if (ImportPlaylistsClick != null) ImportPlaylistsClick(sender, e); }
        private void LibChip_Click(object sender, RoutedEventArgs e) { if (LibChipClick != null) LibChipClick(sender, e); }
        private void LibSortOption_Click(object sender, RoutedEventArgs e) { if (LibSortOptionClick != null) LibSortOptionClick(sender, e); }
        private void LibTileDownloaded_Click(object sender, RoutedEventArgs e) { if (LibTileDownloadedClick != null) LibTileDownloadedClick(sender, e); }
        private void LibTileFavorite_Click(object sender, RoutedEventArgs e) { if (LibTileFavoriteClick != null) LibTileFavoriteClick(sender, e); }
        private void LibTileFollowed_Click(object sender, RoutedEventArgs e) { if (LibTileFollowedClick != null) LibTileFollowedClick(sender, e); }
        private void LibTileMostPlayed_Click(object sender, RoutedEventArgs e) { if (LibTileMostPlayedClick != null) LibTileMostPlayedClick(sender, e); }
        private void LibrarySync_Click(object sender, RoutedEventArgs e) { if (LibrarySyncClick != null) LibrarySyncClick(sender, e); }
        private void MenuAddToPlaylist_Click(object sender, RoutedEventArgs e) { if (MenuAddToPlaylistClick != null) MenuAddToPlaylistClick(sender, e); }
        private void MenuDownload_Click(object sender, RoutedEventArgs e) { if (MenuDownloadClick != null) MenuDownloadClick(sender, e); }
        private void MenuFavorite_Click(object sender, RoutedEventArgs e) { if (MenuFavoriteClick != null) MenuFavoriteClick(sender, e); }
        private void MenuPlay_Click(object sender, RoutedEventArgs e) { if (MenuPlayClick != null) MenuPlayClick(sender, e); }
        private void NavCreate_Click(object sender, RoutedEventArgs e) { if (NavCreateClick != null) NavCreateClick(sender, e); }
        private void NavSearch_Click(object sender, RoutedEventArgs e) { if (NavSearchClick != null) NavSearchClick(sender, e); }
        private void SongItem_Holding(object sender, HoldingRoutedEventArgs e) { if (SongItemHolding != null) SongItemHolding(sender, e); }
        private void LibraryUnified_ItemClick(object sender, ItemClickEventArgs e) { if (LibraryUnifiedItemClick != null) LibraryUnifiedItemClick(sender, e); }
        private void SongList_ItemClick(object sender, ItemClickEventArgs e) { if (SongListItemClick != null) SongListItemClick(sender, e); }
    }
}
