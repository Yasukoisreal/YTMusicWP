using System;
using System.Threading.Tasks;
using Windows.Phone.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace YTMusicWP
{
    public sealed partial class MainPage
    {
        // ── Bottom Navigation ──
        private int _currentTab = 0; // 0=Home, 1=Search, 2=Library
        private static readonly SolidColorBrush _navActiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        private static readonly SolidColorBrush _navInactiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 179, 179, 179));

        // ── Display layers (Canvas.ZIndex in MainPage.xaml) ──
        // Back closes whatever is on top, so the checks below follow this order, top first:
        //   300 Login  ·  280 Create playlist / Add to playlist  ·  270 Live debug  ·  260 Song credits
        //   250 Artist picker / Now Playing menu  ·  200 Track actions sheet
        //   120 Fullscreen lyrics  ·  100 Now Playing  ·  96 nav bar  ·  90 Create sheet
        //    75 Listen Together  ·  55 Samples  ·  50/51 Playlist + Artist (last opened on top)  ·  50 Mood, Settings
        //     0 tab content
        // Same ZIndex: the element later in MainPage.xaml is on top (Create playlist over Add to playlist,
        // Artist picker over the menu, Playlist/Artist over Mood over Settings).
        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            if (LoginWebContainer.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseLoginWeb_Click(null, null);
            }
            else if (CreatePlaylistDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CancelCreatePlaylist_Click(null, null);
            }
            else if (AddToPlaylistDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CancelAddToPlaylist_Click(null, null);
            }
            else if (LiveDebugDialog.IsOpen)
            {
                e.Handled = true;
                LiveDebugDialog.Close();
            }
            else if (SongCreditsDialog.IsOpen)
            {
                e.Handled = true;
                SongCreditsDialog.Close();
            }
            else if (ArtistPickerBottomSheet.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseArtistPicker_Click(null, null);
            }
            else if (NowPlayingMenuDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseNowPlayingMenu_Click(null, null);
            }
            else if (CustomBottomSheet.IsOpen)
            {
                e.Handled = true;
                CloseBottomSheet_Click(null, null);
            }
            else if (FullscreenLyricsView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseFullscreenLyrics_Tapped(null, null);
            }
            else if (NowPlayingView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                if (NowPlayingPivot != null && NowPlayingPivot.SelectedIndex != 0)
                {
                    NowPlayingPivot.SelectedIndex = 0;
                }
                else
                {
                    CloseNowPlaying_Click(null, null);
                }
            }
            else if (CreateBottomSheet.IsOpen)
            {
                e.Handled = true;
                CloseCreateSheet();
            }
            else if (ListenTogetherView != null && ListenTogetherView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                if (ListenTogetherView.LtSettingsPanel != null && ListenTogetherView.LtSettingsPanel.Visibility == Visibility.Visible)
                {
                    LtCloseSettings_Click(null, null);
                }
                else
                {
                    CloseListenTogetherView_Click(null, null);
                }
            }
            else if (_currentTab == SamplesTab)
            {
                // Samples is a tab: back goes Home (SwitchTab closes the Samples view)
                e.Handled = true;
                SwitchTab(0);
            }
            else if (PlaylistDetailsView.Visibility == Visibility.Visible || ArtistProfileView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                bool isArtistTop = ArtistProfileView.Visibility == Visibility.Visible &&
                    (PlaylistDetailsView.Visibility != Visibility.Visible || Canvas.GetZIndex(ArtistProfileView) >= Canvas.GetZIndex(PlaylistDetailsView));
                if (isArtistTop)
                {
                    CloseArtistProfile_Click(null, null);
                }
                else
                {
                    ClosePlaylistDetails_Click(null, null);
                }
            }
            else if (MoodCategoryView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseMoodCategory_Click(null, null);
            }
            else if (SettingsPanel.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseSettings_Click(null, null);
            }
            else if (SuggestionPopup.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                SuggestionPopup.Visibility = Visibility.Collapsed;
            }
            else if (_currentTab != 0)
            {
                e.Handled = true;
                SwitchTab(0);
            }
        }

        // Playlist and Artist are single reusable pages that can open each other; the one opened last is on top.
        // Fixed values: the old "other + 1" rule climbed by one on every Playlist <-> Artist hop and, after enough
        // hops, lifted the pages above Now Playing, the Create sheet and the dialogs.
        private const int PageZIndex = 50;

        private void BringPageToFront(FrameworkElement page)
        {
            var other = page == PlaylistDetailsView ? (FrameworkElement)ArtistProfileView : PlaylistDetailsView;
            Canvas.SetZIndex(other, PageZIndex);
            Canvas.SetZIndex(page, PageZIndex + 1);
        }

        /// <summary>
        /// Playlist and Artist pages sit in the content layer (row 0, below Samples and Now Playing). Opening one
        /// from those surfaces (the Samples "more" sheet, the Now Playing menu) first leaves them, as YT Music does,
        /// so the page never opens hidden behind them.
        /// </summary>
        private void LeaveImmersiveViewsForPage()
        {
            if (_currentTab == SamplesTab) SwitchTab(0);
            if (NowPlayingView.Visibility == Visibility.Visible)
            {
                _isClosingNowPlaying = false;
                NowPlayingView.Visibility = Visibility.Collapsed;
                StopTitleMarquee();
                UpdateStatusBarColor(false, animate: false);
                UpdateWordTimerState();
            }
        }

        private void NavHome_Click(object sender, RoutedEventArgs e) { SwitchTab(0); }

        private void NavSearch_Click(object sender, RoutedEventArgs e) { SwitchTab(1); EnsureMoodsAndGenresLoaded(); }

        private void NavLibrary_Click(object sender, RoutedEventArgs e)
        {
            SwitchTab(2);
            // Bind synced data when switching to Library
            if (LibraryPanel.YouTubePlaylistsListView.ItemsSource == null)
                LibraryPanel.YouTubePlaylistsListView.ItemsSource = _youtubeUserPlaylists;
            if (LibraryPanel.SubscriptionsListView.ItemsSource == null)
                LibraryPanel.SubscriptionsListView.ItemsSource = _youtubeSubscriptions;
            RefreshLibraryList();
        }

        private void SwitchTab(int tab)
        {
            if (_currentTab == tab) return;
            _currentTab = tab;

            // [OPT] Stop background timers that animate offscreen elements
            StopTitleMarquee();
            StopShortsLoop();

            // Close overlay views when switching tabs
            if (ArtistProfileView.Visibility == Visibility.Visible)
            {
                ArtistProfileView.Visibility = Visibility.Collapsed;
                ArtistProfileCover.Source = null;
                ArtistSongsList.ItemsSource = null;
                ArtistSectionsControl.ItemsSource = null;
                ArtistAboutSection.Visibility = Visibility.Collapsed;
                ArtistAboutImage.ImageSource = null;
            }
            if (PlaylistDetailsView.Visibility == Visibility.Visible)
            {
                PlaylistDetailsView.Visibility = Visibility.Collapsed;
                PlaylistDetailsCoverBrush.ImageSource = null;
                PlaylistDetailsCoverRect.Visibility = Visibility.Collapsed;
            }
            if (MoodCategoryView.Visibility == Visibility.Visible)
            {
                Services.MotionHelper.HideNow(MoodCategoryView);
                MoodCategorySectionList.ItemsSource = null;
            }
            // Leaving the Samples tab stops its video (and resumes the main player if Samples paused it)
            if (_shortsIsOpen) CloseShortsView();
            // Also close Settings if open
            Services.MotionHelper.HideNow(SettingsPanel);
            SuggestionPopup.Visibility = Visibility.Collapsed;
            // Fade-in animation for active panel
            var panels = new FrameworkElement[] { HomePanel, SearchPanel, LibraryPanel };
            for (int i = 0; i < panels.Length; i++)
            {
                if (i == tab)
                {
                    Services.MotionHelper.FadeIn(panels[i], Services.MotionHelper.ShortMs, Services.MotionHelper.TabRise);
                    if (tab == 0)
                    {
                        EnsureHomePullTimer();
                    }
                }
                else
                {
                    panels[i].Visibility = Visibility.Collapsed;
                }
            }

            NavHomeIcon.Fill = (tab == 0) ? _navActiveBrush : _navInactiveBrush;
            NavHomeText.Foreground = (tab == 0) ? _navActiveBrush : _navInactiveBrush;

            NavSearchIcon.Fill = (tab == 1) ? _navActiveBrush : _navInactiveBrush;
            NavSearchText.Foreground = (tab == 1) ? _navActiveBrush : _navInactiveBrush;

            NavLibraryIcon.Fill = (tab == 2) ? _navActiveBrush : _navInactiveBrush;
            NavLibraryText.Foreground = (tab == 2) ? _navActiveBrush : _navInactiveBrush;

            NavSamplesIcon.Fill = (tab == SamplesTab) ? _navActiveBrush : _navInactiveBrush;
            NavSamplesText.Foreground = (tab == SamplesTab) ? _navActiveBrush : _navInactiveBrush;

            if (tab == SamplesTab) OpenSamplesTab();
            Services.MemoryHelper.Mark(tab == 0 ? "Home" : tab == 1 ? "Search" : tab == 2 ? "Library" : "Samples");

            if (Services.MemoryHelper.IsLowMemoryDevice)
            {
                Task.Run(() =>
                {
                    try { GC.Collect(1, GCCollectionMode.Optimized); } catch { }
                });
            }
        }

        private async void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            Services.MotionHelper.ShowPage(SettingsPanel);
            await UpdateStorageDisplayAsync();
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Services.MotionHelper.HidePage(SettingsPanel);
        }



        private bool _createSheetOpen = false;

        private void NavCreate_Click(object sender, RoutedEventArgs e)
        {
            if (_createSheetOpen)
            {
                CloseCreateSheet();
            }
            else
            {
                OpenCreateSheet();
            }
        }

        private readonly Services.MotionGroup _createMotion = new Services.MotionGroup();
        private const double CreateIconOpenScale = 18.0 / 14.0;

        private void OpenCreateSheet()
        {
            _createSheetOpen = true;
            var circleT = Services.MotionHelper.EnsureTransform(NavCreateCircle);
            if (CreateBottomSheet.Visibility != Visibility.Visible)
            {
                CreateBottomSheet.Opacity = 0;
                CreateBottomSheet.SheetTransform.Y = 200;
            }
            if (NavCreateCircle.Visibility != Visibility.Visible)
            {
                NavCreateCircle.Opacity = 0;
                circleT.ScaleX = 0.4;
                circleT.ScaleY = 0.4;
            }
            CreateBottomSheet.Visibility = Visibility.Visible;
            NavCreateCircle.Visibility = Visibility.Visible;

            // Hide text, icon -> black on the white disc
            NavCreateText.Visibility = Visibility.Collapsed;
            NavCreateIcon.Fill = _libChipActiveTextBrush; // cached Black brush

            // + turns into x and grows while the disc blooms behind it and the menu card rises
            _createMotion.Animate(Services.MotionHelper.EnterMs, Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut, null,
                Services.MotionHelper.To(NavCreateTransform, "Rotation", 45),
                Services.MotionHelper.To(NavCreateTransform, "ScaleX", CreateIconOpenScale),
                Services.MotionHelper.To(NavCreateTransform, "ScaleY", CreateIconOpenScale),
                Services.MotionHelper.To(NavCreateCircle, "Opacity", 1),
                Services.MotionHelper.To(circleT, "ScaleX", 1),
                Services.MotionHelper.To(circleT, "ScaleY", 1),
                Services.MotionHelper.To(CreateBottomSheet.SheetTransform, "Y", 0),
                Services.MotionHelper.To(CreateBottomSheet, "Opacity", 1));
        }

        private void CloseCreateSheet()
        {
            _createSheetOpen = false;
            var circleT = Services.MotionHelper.EnsureTransform(NavCreateCircle);

            // Show text, icon -> gray
            NavCreateText.Visibility = Visibility.Visible;
            NavCreateIcon.Fill = _navInactiveBrush;

            _createMotion.Animate(Services.MotionHelper.ExitMs, Windows.UI.Xaml.Media.Animation.EasingMode.EaseIn, () =>
                {
                    CreateBottomSheet.Visibility = Visibility.Collapsed;
                    NavCreateCircle.Visibility = Visibility.Collapsed;
                },
                Services.MotionHelper.To(NavCreateTransform, "Rotation", 0),
                Services.MotionHelper.To(NavCreateTransform, "ScaleX", 1),
                Services.MotionHelper.To(NavCreateTransform, "ScaleY", 1),
                Services.MotionHelper.To(NavCreateCircle, "Opacity", 0),
                Services.MotionHelper.To(circleT, "ScaleX", 0.4),
                Services.MotionHelper.To(circleT, "ScaleY", 0.4),
                Services.MotionHelper.To(CreateBottomSheet.SheetTransform, "Y", 200),
                Services.MotionHelper.To(CreateBottomSheet, "Opacity", 0));
        }

        private void CloseCreateSheet_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (e.OriginalSource is Windows.UI.Xaml.Shapes.Rectangle)
                CloseCreateSheet();
        }

        private void CreateSheet_Playlist_Click(object sender, RoutedEventArgs e)
        {
            CloseCreateSheet();
            OpenCreatePlaylistDialog_Click(sender, e);
        }

        private async void CreateSheet_Collab_Click(object sender, RoutedEventArgs e)
        {
            CloseCreateSheet();
            var dialog = new Windows.UI.Popups.MessageDialog("Collaborative playlists are not yet supported on this platform.", "Coming Soon");
            await dialog.ShowAsync();
        }

        private async void CreateSheet_Blend_Click(object sender, RoutedEventArgs e)
        {
            CloseCreateSheet();
            var dialog = new Windows.UI.Popups.MessageDialog("Blend is not yet supported on this platform.", "Coming Soon");
            await dialog.ShowAsync();
        }

    }
}
