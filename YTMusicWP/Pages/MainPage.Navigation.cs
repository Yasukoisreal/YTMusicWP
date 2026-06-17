using System;
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

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            if (CustomBottomSheet.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseBottomSheet_Click(null, null);
            }
            else if (SettingsPanel.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseSettings_Click(null, null);
            }
            else if (ArtistProfileView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseArtistProfile_Click(null, null);
            }
            else if (LoginWebContainer.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseLoginWeb_Click(null, null);
            }
            else if (CreatePlaylistDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CreatePlaylistDialog.Visibility = Visibility.Collapsed;
            }
            else if (AddToPlaylistDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                AddToPlaylistDialog.Visibility = Visibility.Collapsed;
            }
            else if (NowPlayingMenuDialog.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseNowPlayingMenu_Click(null, null);
            }
            else if (PlaylistDetailsView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                ClosePlaylistDetails_Click(null, null);
            }
            else if (NowPlayingView.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CloseNowPlaying_Click(null, null);
            }
            else if (SuggestionPopup.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                SuggestionPopup.Visibility = Visibility.Collapsed;
            }
        }

        private void NavHome_Click(object sender, RoutedEventArgs e) { SwitchTab(0); }

        private void NavSearch_Click(object sender, RoutedEventArgs e) { SwitchTab(1); LoadDiscoverSection(); }

        private void NavLibrary_Click(object sender, RoutedEventArgs e)
        {
            SwitchTab(2);
            // Bind synced data when switching to Library
            if (YouTubePlaylistsListView.ItemsSource == null)
                YouTubePlaylistsListView.ItemsSource = _youtubeUserPlaylists;
            if (SubscriptionsListView.ItemsSource == null)
                SubscriptionsListView.ItemsSource = _youtubeSubscriptions;
        }

        private void SwitchTab(int tab)
        {
            if (_currentTab == tab) return;
            _currentTab = tab;

            // Fade-in animation for active panel
            var panels = new[] { HomePanel, SearchPanel, LibraryPanel };
            for (int i = 0; i < panels.Length; i++)
            {
                if (i == tab)
                {
                    panels[i].Opacity = 0;
                    panels[i].Visibility = Visibility.Visible;
                    var fadeIn = new Windows.UI.Xaml.Media.Animation.Storyboard();
                    var anim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = new Duration(TimeSpan.FromMilliseconds(150))
                    };
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, panels[i]);
                    Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
                    fadeIn.Children.Add(anim);
                    fadeIn.Begin();
                }
                else
                {
                    panels[i].Visibility = Visibility.Collapsed;
                }
            }

            // Active tab = White, inactive = #B3B3B3
            var activeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            var inactiveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 179, 179, 179));

            NavHomeIcon.Fill = (tab == 0) ? activeBrush : inactiveBrush;
            NavHomeText.Foreground = (tab == 0) ? activeBrush : inactiveBrush;
            NavHomeText.FontWeight = (tab == 0) ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.Normal;

            // NavSearchIcon is a Canvas with Path children
            var searchBrush = (tab == 1) ? activeBrush : inactiveBrush;
            foreach (var child in NavSearchIcon.Children)
            {
                var p = child as Windows.UI.Xaml.Shapes.Path;
                if (p != null) p.Fill = searchBrush;
            }
            NavSearchText.Foreground = (tab == 1) ? activeBrush : inactiveBrush;
            NavSearchText.FontWeight = (tab == 1) ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.Normal;

            NavLibraryIcon.Fill = (tab == 2) ? activeBrush : inactiveBrush;
            NavLibraryText.Foreground = (tab == 2) ? activeBrush : inactiveBrush;
            NavLibraryText.FontWeight = (tab == 2) ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.Normal;
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Visible;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
        }

    }
}
