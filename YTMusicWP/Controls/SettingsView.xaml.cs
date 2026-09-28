using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Settings screen. The settings logic still lives in MainPage (Auth/Storage partials), which reads the
    /// named elements directly (they are x:FieldModifier="internal") and handles the button events below.
    /// </summary>
    public sealed partial class SettingsView : UserControl
    {
        public event RoutedEventHandler CloseClick;
        public event RoutedEventHandler LoginGoogleClick;
        public event RoutedEventHandler LoginCookieClick;
        public event RoutedEventHandler LogoutGoogleClick;
        public event RoutedEventHandler AddAccountClick;
        public event RoutedEventHandler SyncNowClick;
        public event RoutedEventHandler ClearCacheClick;
        public event RoutedEventHandler CleanAllCacheClick;
        public event RoutedEventHandler ClearRecentHistoryClick;
        public event RoutedEventHandler ClearTempStreamsClick;
        public event RoutedEventHandler ExportAllToMusicFolderClick;
        public event RoutedEventHandler RefreshStorageStatsClick;
        public event RoutedEventHandler ListenTogetherClick;

        public SettingsView()
        {
            this.InitializeComponent();
        }

        private static void Raise(RoutedEventHandler handler, object sender, RoutedEventArgs e)
        {
            if (handler != null) handler(sender, e);
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Raise(CloseClick, sender, e); }
        private void LoginGoogle_Click(object sender, RoutedEventArgs e) { Raise(LoginGoogleClick, sender, e); }
        private void LoginCookie_Click(object sender, RoutedEventArgs e) { Raise(LoginCookieClick, sender, e); }
        private void LogoutGoogle_Click(object sender, RoutedEventArgs e) { Raise(LogoutGoogleClick, sender, e); }
        private void AddAccount_Click(object sender, RoutedEventArgs e) { Raise(AddAccountClick, sender, e); }
        private void SyncNow_Click(object sender, RoutedEventArgs e) { Raise(SyncNowClick, sender, e); }
        private void ClearCache_Click(object sender, RoutedEventArgs e) { Raise(ClearCacheClick, sender, e); }
        private void CleanAllCache_Click(object sender, RoutedEventArgs e) { Raise(CleanAllCacheClick, sender, e); }
        private void ClearRecentHistory_Click(object sender, RoutedEventArgs e) { Raise(ClearRecentHistoryClick, sender, e); }
        private void ClearTempStreams_Click(object sender, RoutedEventArgs e) { Raise(ClearTempStreamsClick, sender, e); }
        private void ExportAllToMusicFolder_Click(object sender, RoutedEventArgs e) { Raise(ExportAllToMusicFolderClick, sender, e); }
        private void RefreshStorageStats_Click(object sender, RoutedEventArgs e) { Raise(RefreshStorageStatsClick, sender, e); }
        private void ListenTogether_Click(object sender, RoutedEventArgs e) { Raise(ListenTogetherClick, sender, e); }
    }
}
