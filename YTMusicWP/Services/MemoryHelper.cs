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
        }

        private static void MemoryManager_AppMemoryUsageIncreased(object sender, object e)
        {
            try
            {
                var level = MemoryManager.AppMemoryUsageLevel;
                if (level == AppMemoryUsageLevel.High)
                {
                    System.Diagnostics.Debug.WriteLine("[MemoryHelper] High memory pressure detected. Trimming memory caches.");
                    TrimMemory();
                }
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
