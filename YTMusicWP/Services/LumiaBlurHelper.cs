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
        // LRU cache: max 3 entries keyed by URL
        private static readonly string[] _cacheKeys = new string[3];
        private static readonly WriteableBitmap[] _cacheValues = new WriteableBitmap[3];
        private static int _cacheIndex;

        // Faded artwork cache: max 3 entries
        private static readonly string[] _fadedCacheKeys = new string[3];
        private static readonly WriteableBitmap[] _fadedCacheValues = new WriteableBitmap[3];
        private static int _fadedCacheIndex;

        public static WriteableBitmap GetCached(string key)
        {
            for (int i = 0; i < _cacheKeys.Length; i++)
            {
                if (_cacheKeys[i] == key && _cacheValues[i] != null)
                    return _cacheValues[i];
            }
            return null;
        }

        public static void PutCache(string key, WriteableBitmap bitmap)
        {
            _cacheKeys[_cacheIndex] = key;
            _cacheValues[_cacheIndex] = bitmap;
            _cacheIndex = (_cacheIndex + 1) % _cacheKeys.Length;
        }

        public static WriteableBitmap GetCachedFaded(string key)
        {
            for (int i = 0; i < _fadedCacheKeys.Length; i++)
            {
                if (_fadedCacheKeys[i] == key && _fadedCacheValues[i] != null)
                    return _fadedCacheValues[i];
            }
            return null;
        }

        public static void PutCachedFaded(string key, WriteableBitmap bitmap)
        {
            _fadedCacheKeys[_fadedCacheIndex] = key;
            _fadedCacheValues[_fadedCacheIndex] = bitmap;
            _fadedCacheIndex = (_fadedCacheIndex + 1) % _fadedCacheKeys.Length;
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
                filterEffect.Filters = new IFilter[] { new BlurFilter(kernelSize) };
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

            using (var pixelStream = bitmap.PixelBuffer.AsStream())
            {
                byte[] pixels = new byte[targetWidth * targetHeight * 4];
                await pixelStream.ReadAsync(pixels, 0, pixels.Length);

                int fadeStartY = Math.Max(0, targetHeight - fadeHeight);
                for (int y = fadeStartY; y < targetHeight; y++)
                {
                    double progress = (double)(y - fadeStartY) / fadeHeight;
                    // Cosine ease: starts flat at 1.0, ends flat at 0.0 with 0 derivative at edges
                    double alpha = 0.5 * (1.0 + Math.Cos(progress * Math.PI));

                    int rowStart = y * targetWidth * 4;
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int idx = rowStart + (x * 4);
                        // BGRA premultiplied alpha
                        pixels[idx]     = (byte)(pixels[idx] * alpha);     // B
                        pixels[idx + 1] = (byte)(pixels[idx + 1] * alpha); // G
                        pixels[idx + 2] = (byte)(pixels[idx + 2] * alpha); // R
                        pixels[idx + 3] = (byte)(pixels[idx + 3] * alpha); // A
                    }
                }

                pixelStream.Position = 0;
                await pixelStream.WriteAsync(pixels, 0, pixels.Length);
            }
            bitmap.Invalidate();
            return bitmap;
        }
    }
}
