using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace YTMusicWP.Controls
{
    /// <summary>
    /// Bottom sheet showing the livestream debug log stored in LocalSettings["LiveDebugLog"].
    /// Sharing goes through MainPage's DataRequested handler, which reads <see cref="LogText"/> while the dialog is open.
    /// </summary>
    public sealed partial class LiveDebugDialog : UserControl
    {
        private const string EmptyLogText = "No livestream logs recorded yet.";

        /// <summary>Raised with a short message MainPage should show as a toast.</summary>
        public event EventHandler<string> ToastRequested;

        public LiveDebugDialog()
        {
            this.InitializeComponent();
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        public string LogText
        {
            get { return LiveDebugTextBox.Text ?? ""; }
        }

        public void Open()
        {
            RefreshLogs();
            Services.MotionHelper.ShowSheet(this, Sheet);
        }

        public void Close()
        {
            LiveDebugTextBox.IsReadOnly = true;
            Services.MotionHelper.HideSheet(this, Sheet);
        }

        private void RefreshLogs()
        {
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                string log = ls.ContainsKey("LiveDebugLog") ? ls["LiveDebugLog"]?.ToString() : null;
                LiveDebugTextBox.Text = !string.IsNullOrEmpty(log) ? log : EmptyLogText;
            }
            catch (Exception ex)
            {
                LiveDebugTextBox.Text = "Error reading logs: " + ex.Message;
            }
        }

        private void Toast(string message)
        {
            if (ToastRequested != null) ToastRequested(this, message);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshLogs();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ls = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                ls["LiveDebugLog"] = "";
                LiveDebugTextBox.Text = EmptyLogText;
                Toast("Cleared livestream logs.");
            }
            catch { }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LiveDebugTextBox.IsReadOnly = false;
                LiveDebugTextBox.Focus(FocusState.Programmatic);
                LiveDebugTextBox.SelectAll();
                Toast("All logs selected. Tap Copy on keyboard!");
            }
            catch { }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = LiveDebugTextBox.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    var file = await Windows.Storage.KnownFolders.MusicLibrary.CreateFileAsync("LiveStream_Debug.txt", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await Windows.Storage.FileIO.WriteTextAsync(file, text);
                    Toast("Saved LiveStream_Debug.txt to Music folder!");
                }
            }
            catch (Exception ex)
            {
                Toast("Error saving file: " + ex.Message);
            }
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Windows.ApplicationModel.DataTransfer.DataTransferManager.ShowShareUI();
            }
            catch (Exception ex)
            {
                Toast("Lỗi Share: " + ex.Message);
            }
        }
    }
}
