using System.Windows;
using System.Windows.Media.Imaging;

namespace SamplePlugins
{
    /// <summary>
    /// Simplest possible example plugin: read every pixel out, transform it, write it back.
    /// This is the whole shape every plugin follows - Name, optionally Description, and Apply.
    /// </summary>
    public class GrayscalePlugin
    {
        public string Name => "Grayscale";
        public string Description => "Converts the picture to grayscale.";

        public void Apply(WriteableBitmap bitmap)
        {
            int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
            int stride = bitmap.BackBufferStride;
            var pixels = new byte[stride * h];
            bitmap.CopyPixels(pixels, stride, 0);

            // Pbgra32 byte order per pixel: Blue, Green, Red, Alpha.
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                byte gray = (byte)(0.11 * b + 0.59 * g + 0.30 * r);
                pixels[i] = gray;
                pixels[i + 1] = gray;
                pixels[i + 2] = gray;
                // alpha (pixels[i+3]) is left untouched
            }

            bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        }
    }
}
