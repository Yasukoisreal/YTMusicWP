using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Google sign-in screen. MainPage.Auth.cs drives it; the WebView navigation events are subscribed in the MainPage constructor.
    /// </summary>
    public sealed partial class LoginWebPanel : UserControl
    {
        public event RoutedEventHandler CloseLoginWebClick;

        public LoginWebPanel()
        {
            this.InitializeComponent();
        }

        private void CloseLoginWeb_Click(object sender, RoutedEventArgs e) { if (CloseLoginWebClick != null) CloseLoginWebClick(sender, e); }
    }
}
