using System;
using Windows.System;

namespace YTMusicWP.Services
{
    /// <summary>
    /// MemoryHelper provides low-memory hardware detection and proactive memory management
    /// for 512MB RAM Windows Phone 8.1 devices (e.g. Lumia 520, 630).
    /// </summary>
    public static class MemoryHelper
    {
        private static bool _initialized = false;
        private static bool? _isLowMemoryCached = null;

        /// <summary>
        /// True if the current device is a 512MB RAM device.
        /// Windows Phone 8.1 assigns a 185MB limit to foreground apps on 512MB devices.
        /// (AppMemoryUsageLimit is approximately 185MB - 200MB).
        /// </summary>
        public static bool IsLowMemoryDevice
        {
            get
            {
                if (_isLowMemoryCached.HasValue) return _isLowMemoryCached.Value;
                try
                {
                    // 512MB phones have AppMemoryUsageLimit around 185MB (<= 220MB threshold)
                    ulong limit = MemoryManager.AppMemoryUsageLimit;
                    _isLowMemoryCached = (limit <= 220UL * 1024UL * 1024UL);
                }
                catch
                {
                    _isLowMemoryCached = false;
                }
                return _isLowMemoryCached.Value;
            }
        }

        /// <summary>
        /// Registers listeners for OS memory pressure notifications.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                MemoryManager.AppMemoryUsageIncreased += MemoryManager_AppMemoryUsageIncreased;
            }
            catch { }
            StartUsageSampler();
        }

        // ==========================================
        // DEBUG MEMORY PROFILING ("[Memory]" lines in the Output window)
        // Samples AppMemoryUsage every 2s, tracks the peak and logs whenever usage moves by >= 3 MB, tagged with
        // the last screen passed to Mark(). Compiled out of Release builds.
        // ==========================================
        private const ulong SampleLogStepBytes = 3UL * 1024 * 1024;
        private static System.Threading.Timer _sampler;
        private static ulong _peakUsage;
        private static ulong _lastLoggedUsage;
        private static string _currentScreen = "Startup";

        [System.Diagnostics.Conditional("DEBUG")]
        private static void StartUsageSampler()
        {
            _sampler = new System.Threading.Timer(_ =>
            {
                try
                {
                    ulong usage = MemoryManager.AppMemoryUsage;
                    if (usage > _peakUsage) _peakUsage = usage;
                    ulong diff = usage > _lastLoggedUsage ? usage - _lastLoggedUsage : _lastLoggedUsage - usage;
                    if (diff >= SampleLogStepBytes) LogUsage(_currentScreen);
                }
                catch { }
            }, null, 2000, 2000);
        }

        /// <summary>Tags the following memory samples with <paramref name="screen"/> and logs the current usage.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void Mark(string screen)
        {
            _currentScreen = screen;
            LogUsage(screen);
        }

        private static void LogUsage(string screen)
        {
            try
            {
                ulong usage = MemoryManager.AppMemoryUsage;
                ulong limit = MemoryManager.AppMemoryUsageLimit;
                if (usage > _peakUsage) _peakUsage = usage;
                _lastLoggedUsage = usage;
                System.Diagnostics.Debug.WriteLine(string.Format("[Memory] {0}: {1:0.0} MB / {2:0} MB ({3:0}%), peak {4:0.0} MB, level {5}",
                    screen, usage / 1048576.0, limit / 1048576.0, limit > 0 ? usage * 100.0 / limit : 0, _peakUsage / 1048576.0, MemoryManager.AppMemoryUsageLevel));
            }
            catch { }
        }

        /// <summary>
        /// Raised on the UI thread when the OS reports high memory usage, after the caches were trimmed: the page drops
        /// what it can rebuild later (animated artwork video).
        /// </summary>
        public static event EventHandler HighMemoryPressure;

        private static void MemoryManager_AppMemoryUsageIncreased(object sender, object e)
        {
            try
            {
                var level = MemoryManager.AppMemoryUsageLevel;
                if (level != AppMemoryUsageLevel.High) return;
                System.Diagnostics.Debug.WriteLine("[MemoryHelper] High memory pressure detected. Trimming memory caches.");

                // Raised on a thread-pool thread, while the caches (lyrics dictionary, bitmaps) belong to the UI thread
                var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
                var ignored = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, () =>
                {
                    TrimMemory();
                    var handler = HighMemoryPressure;
                    if (handler != null)
                    {
                        try { handler(null, EventArgs.Empty); } catch { }
                    }
                });
            }
            catch { }
        }

        /// <summary>
        /// Proactively trims in-memory bitmap and data caches, then triggers garbage collection.
        /// </summary>
        public static void TrimMemory()
        {
            try
            {
                // 1. Purge LumiaBlurHelper artwork and blur caches
                LumiaBlurHelper.ClearCache();

                // 2. Purge lyrics cache
                MainPage.ClearLyricsCache();

                // 3. Force garbage collection on generation 2
                GC.Collect(2, GCCollectionMode.Forced);
            }
            catch { }
        }
    }
}
