using System;
using System.IO;
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
                using (var renderer = new WriteableBitmapRenderer(filterEffect, bitmap))
                {
                    await renderer.RenderAsync();
                }
            }
            bitmap.Invalidate();
            return bitmap;
        }
    }
}
