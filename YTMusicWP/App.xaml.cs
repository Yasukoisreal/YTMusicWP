using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace YTMusicWP
{
    public sealed partial class App : Application
    {
        private TransitionCollection transitions;

        public App()
        {
            this.InitializeComponent();
            Services.MemoryHelper.Initialize();
            this.Suspending += this.OnSuspending;
            this.UnhandledException += App_UnhandledException;
        }

        private async void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Prevent app crash — especially important for OOM on 512MB WP8.1 devices
            e.Handled = true;
            string msg = e.Exception != null ? e.Exception.Message : "Unknown error";
            string stack = e.Exception != null ? e.Exception.StackTrace : "No stack trace";
            string err = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}\r\nStack: {2}\r\n\r\n", DateTime.Now, msg, stack);
            System.Diagnostics.Debug.WriteLine("[CRASH PREVENTED] " + err);
            try
            {
                var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync("crash.log", Windows.Storage.CreationCollisionOption.OpenIfExists);
                await Windows.Storage.FileIO.AppendTextAsync(file, err);
            }
            catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                this.DebugSettings.EnableFrameRateCounter = false;
            }
#endif

            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.CacheSize = 1;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    // Restore state if needed
                }

                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                if (rootFrame.ContentTransitions != null)
                {
                    this.transitions = new TransitionCollection();
                    foreach (var c in rootFrame.ContentTransitions)
                    {
                        this.transitions.Add(c);
                    }
                }

                rootFrame.ContentTransitions = null;
                rootFrame.Navigated += this.RootFrame_FirstNavigated;

                if (!rootFrame.Navigate(typeof(MainPage), e.Arguments))
                {
                    throw new Exception("Failed to create initial page");
                }
            }
            else if (!string.IsNullOrEmpty(e.Arguments))
            {
                var mainPage = rootFrame.Content as MainPage;
                if (mainPage != null)
                {
                    mainPage.HandleTileDeepLink(e.Arguments);
                }
            }

            Window.Current.Activate();
        }

        private void RootFrame_FirstNavigated(object sender, NavigationEventArgs e)
        {
            var rootFrame = sender as Frame;
            if (rootFrame != null)
            {
                rootFrame.ContentTransitions = this.transitions ?? new TransitionCollection() { new NavigationThemeTransition() };
                rootFrame.Navigated -= this.RootFrame_FirstNavigated;
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            // TODO: Save application state and stop any background activity if needed
            deferral.Complete();
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);

            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null) return;

            var page = rootFrame.Content as MainPage;
            if (page == null) return;

            // FileSavePicker continuation
            if (args.Kind == ActivationKind.PickSaveFileContinuation)
            {
                var saveArgs = args as FileSavePickerContinuationEventArgs;
                if (saveArgs != null && saveArgs.File != null)
                {
                    page.HandleFileSaveContinuation(saveArgs.File);
                }
            }
            // FileOpenPicker continuation
            else if (args.Kind == ActivationKind.PickFileContinuation)
            {
                var openArgs = args as FileOpenPickerContinuationEventArgs;
                if (openArgs != null && openArgs.Files != null && openArgs.Files.Count > 0)
                {
                    page.HandleFileOpenContinuation(openArgs.Files[0]);
                }
            }

            Window.Current.Activate();
        }
    }
}