using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;
using Lumia.Imaging;
using Lumia.Imaging.Adjustments;

namespace YTMusicWP.Services
{
    internal static class LumiaBlurHelper
    {
        private static int MaxCacheSize => MemoryHelper.IsLowMemoryDevice ? 2 : 6;

        // LRU cache: max 6 entries keyed by canonical URL
        private static readonly string[] _cacheKeys = new string[6];
        private static readonly WriteableBitmap[] _cacheValues = new WriteableBitmap[6];

        // Faded artwork cache: max 6 entries
        private static readonly string[] _fadedCacheKeys = new string[6];
        private static readonly WriteableBitmap[] _fadedCacheValues = new WriteableBitmap[6];

        public static void ClearCache()
        {
            Array.Clear(_cacheKeys, 0, _cacheKeys.Length);
            Array.Clear(_cacheValues, 0, _cacheValues.Length);
            Array.Clear(_fadedCacheKeys, 0, _fadedCacheKeys.Length);
            Array.Clear(_fadedCacheValues, 0, _fadedCacheValues.Length);
        }

        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            try
            {
                return MainPage.GetAppleMusicThumbnail(key);
            }
            catch
            {
                return key;
            }
        }

        public static WriteableBitmap GetCached(string key)
        {
            string normKey = NormalizeKey(key);
            int max = MaxCacheSize;
            for (int i = 0; i < max; i++)
            {
                if (_cacheKeys[i] == normKey && _cacheValues[i] != null)
                {
                    var val = _cacheValues[i];
                    if (i > 0)
                    {
                        for (int j = i; j > 0; j--)
                        {
                            _cacheKeys[j] = _cacheKeys[j - 1];
                            _cacheValues[j] = _cacheValues[j - 1];
                        }
                        _cacheKeys[0] = normKey;
                        _cacheValues[0] = val;
                    }
                    return val;
                }
            }
            return null;
        }

        public static void PutCache(string key, WriteableBitmap bitmap)
        {
            if (string.IsNullOrEmpty(key) || bitmap == null) return;
            string normKey = NormalizeKey(key);
            int max = MaxCacheSize;

            int existingIdx = -1;
            for (int i = 0; i < max; i++)
            {
                if (_cacheKeys[i] == normKey)
                {
                    existingIdx = i;
                    break;
                }
            }

            int end = (existingIdx != -1) ? existingIdx : Math.Min(max - 1, _cacheKeys.Length - 1);
            for (int j = end; j > 0; j--)
            {
                _cacheKeys[j] = _cacheKeys[j - 1];
                _cacheValues[j] = _cacheValues[j - 1];
            }
            _cacheKeys[0] = normKey;
            _cacheValues[0] = bitmap;
        }

        public static WriteableBitmap GetCachedFaded(string key)
        {
            string normKey = NormalizeKey(key);
            int max = MaxCacheSize;
            for (int i = 0; i < max; i++)
            {
                if (_fadedCacheKeys[i] == normKey && _fadedCacheValues[i] != null)
                {
                    var val = _fadedCacheValues[i];
                    if (i > 0)
                    {
                        for (int j = i; j > 0; j--)
                        {
                            _fadedCacheKeys[j] = _fadedCacheKeys[j - 1];
                            _fadedCacheValues[j] = _fadedCacheValues[j - 1];
                        }
                        _fadedCacheKeys[0] = normKey;
                        _fadedCacheValues[0] = val;
                    }
                    return val;
                }
            }
            return null;
        }

        public static void PutCachedFaded(string key, WriteableBitmap bitmap)
        {
            if (string.IsNullOrEmpty(key) || bitmap == null) return;
            string normKey = NormalizeKey(key);
            int max = MaxCacheSize;

            int existingIdx = -1;
            for (int i = 0; i < max; i++)
            {
                if (_fadedCacheKeys[i] == normKey)
                {
                    existingIdx = i;
                    break;
                }
            }

            int end = (existingIdx != -1) ? existingIdx : Math.Min(max - 1, _fadedCacheKeys.Length - 1);
            for (int j = end; j > 0; j--)
            {
                _fadedCacheKeys[j] = _fadedCacheKeys[j - 1];
                _fadedCacheValues[j] = _fadedCacheValues[j - 1];
            }
            _fadedCacheKeys[0] = normKey;
            _fadedCacheValues[0] = bitmap;
        }

        /// <summary>
        /// Renders a blurred version of the source image at the specified dimensions.
        /// Uses Lumia Imaging SDK BlurFilter for ARM NEON-accelerated blur.
        /// Typical usage: 120×200 target with kernelSize=80 for frosted backdrop.
        /// </summary>
        public static async Task<WriteableBitmap> RenderBlurredAsync(
            Stream source, int targetWidth, int targetHeight, int kernelSize)
        {
            if (source != null && source.CanSeek) source.Position = 0;
            var bitmap = new WriteableBitmap(targetWidth, targetHeight);
            using (var imageSource = new StreamImageSource(source))
            using (var filterEffect = new FilterEffect(imageSource))
            {
                filterEffect.Filters = new IFilter[]
                {
                    new BlurFilter(kernelSize),
                    new BlurFilter(kernelSize),
                    new BlurFilter(kernelSize)
                };
                using (var renderer = new WriteableBitmapRenderer(filterEffect, bitmap, OutputOption.Stretch))
                {
                    await renderer.RenderAsync();
                }
            }
            bitmap.Invalidate();
            return bitmap;
        }

        /// <summary>
        /// Renders the source image with the bottom portion dissolved using a smooth cosine alpha fade.
        /// This allows the sharp album artwork to seamlessly blend into the blurred backdrop behind it.
        /// </summary>
        public static async Task<WriteableBitmap> RenderFadedArtworkAsync(
            Stream source, int targetWidth, int targetHeight, int fadeHeight)
        {
            if (source != null && source.CanSeek) source.Position = 0;
            var bitmap = new WriteableBitmap(targetWidth, targetHeight);
            using (var imageSource = new StreamImageSource(source))
            using (var renderer = new WriteableBitmapRenderer(imageSource, bitmap, OutputOption.Stretch))
            {
                await renderer.RenderAsync();
            }

            try
            {
                using (var pixelStream = bitmap.PixelBuffer.AsStream())
                {
                    int rowBytes = targetWidth * 4;
                    byte[] rowBuffer = new byte[rowBytes];
                    int fadeStartY = Math.Max(0, targetHeight - fadeHeight);

                    for (int y = fadeStartY; y < targetHeight; y++)
                    {
                        long rowOffset = (long)y * rowBytes;
                        pixelStream.Seek(rowOffset, SeekOrigin.Begin);
                        int read = 0;
                        while (read < rowBytes)
                        {
                            int r = pixelStream.Read(rowBuffer, read, rowBytes - read);
                            if (r <= 0) break;
                            read += r;
                        }

                        if (read == rowBytes)
                        {
                            double progress = (double)(y - fadeStartY) / fadeHeight;
                            // Cosine ease: starts flat at 1.0, ends flat at 0.0 with 0 derivative at edges
                            double alpha = 0.5 * (1.0 + Math.Cos(progress * Math.PI));
                            int alphaI = (int)(alpha * 256.0);

                            for (int x = 0; x < targetWidth; x++)
                            {
                                int idx = x * 4;
                                // BGRA premultiplied alpha with integer shift
                                rowBuffer[idx]     = (byte)((rowBuffer[idx] * alphaI) >> 8);     // B
                                rowBuffer[idx + 1] = (byte)((rowBuffer[idx + 1] * alphaI) >> 8); // G
                                rowBuffer[idx + 2] = (byte)((rowBuffer[idx + 2] * alphaI) >> 8); // R
                                rowBuffer[idx + 3] = (byte)((rowBuffer[idx + 3] * alphaI) >> 8); // A
                            }

                            pixelStream.Seek(rowOffset, SeekOrigin.Begin);
                            pixelStream.Write(rowBuffer, 0, rowBytes);
                        }
                    }
                    pixelStream.Flush();
                }
                bitmap.Invalidate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[RenderFadedArtworkAsync] Pixel manipulation error: " + ex.Message);
            }

            return bitmap;
        }
    }
}
