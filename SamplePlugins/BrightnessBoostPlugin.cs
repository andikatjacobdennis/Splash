using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SamplePlugins
{
    public class BrightnessBoostPlugin
    {
        public string Name => "Brightness Boost";
        public string Description => "Brightens the picture by a fixed amount.";

        private const int Amount = 35;

        public void Apply(WriteableBitmap bitmap)
        {
            int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
            int stride = bitmap.BackBufferStride;
            var pixels = new byte[stride * h];
            bitmap.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] + Amount);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + Amount);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + Amount);
            }

            bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        }
    }
}
