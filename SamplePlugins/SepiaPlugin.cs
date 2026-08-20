using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SamplePlugins
{
    public class SepiaPlugin
    {
        public string Name => "Sepia Tone";
        public string Description => "Applies a warm, old-photograph sepia tone.";

        public void Apply(WriteableBitmap bitmap)
        {
            int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
            int stride = bitmap.BackBufferStride;
            var pixels = new byte[stride * h];
            bitmap.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

                int newR = (int)(0.393 * r + 0.769 * g + 0.189 * b);
                int newG = (int)(0.349 * r + 0.686 * g + 0.168 * b);
                int newB = (int)(0.272 * r + 0.534 * g + 0.131 * b);

                pixels[i] = (byte)Math.Min(255, newB);
                pixels[i + 1] = (byte)Math.Min(255, newG);
                pixels[i + 2] = (byte)Math.Min(255, newR);
            }

            bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        }
    }
}
