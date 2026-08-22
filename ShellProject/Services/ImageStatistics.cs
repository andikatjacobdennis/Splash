using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaintClone.Models;

namespace PaintClone.Services
{
    /// <summary>Everything the Attributes window reports about the picture itself, as opposed to
    /// the canvas it sits on. Measured from the flattened composite of the *visible* layers - what
    /// you would get if you exported right now - rather than from the active layer, because these
    /// are statements about the image and a hidden layer is not part of it.</summary>
    public sealed class ImageStats
    {
        public int Width, Height;
        public long PixelCount => (long)Width * Height;

        public int LayerCount, VisibleLayerCount, TextLayerCount;
        public long MemoryBytes;

        /// <summary>Fully transparent, partly transparent, and fully opaque pixel counts. They sum
        /// to PixelCount.</summary>
        public long TransparentPixels, PartialPixels, OpaquePixels;

        /// <summary>Distinct RGB triples among pixels that aren't fully transparent. Exact, not
        /// estimated - counted through a 2^24 bitset rather than a hash set, which costs a flat
        /// 2 MB instead of growing with the picture.</summary>
        public int UniqueColors;

        /// <summary>256-bin counts over the non-transparent pixels.</summary>
        public int[] HistR = new int[256], HistG = new int[256], HistB = new int[256], HistLum = new int[256];

        public double MeanR, MeanG, MeanB, MeanLum, StdDevLum;
        public int MedianLum, MinLum, MaxLum;

        /// <summary>The most-used colours, each with the share of coloured pixels it accounts for.
        /// Grouped rather than exact - see ImageStatistics.Compute.</summary>
        public List<(Color Color, double Percent)> TopColors = new();

        /// <summary>Pixels that carried any colour at all - the denominator for every average and
        /// percentage above. Zero for a completely empty picture, which every consumer has to
        /// handle rather than dividing by it.</summary>
        public long ColoredPixels => PartialPixels + OpaquePixels;
    }

    public static class ImageStatistics
    {
        /// <summary>Rec. 709 luma weights - the same ones used for HD video and for "luminosity"
        /// in most image editors. Not a plain (R+G+B)/3, which would call pure blue and pure green
        /// equally bright when the eye finds green far brighter.</summary>
        private const double LumR = 0.2126, LumG = 0.7152, LumB = 0.0722;

        /// <summary>Bits per channel kept when grouping colours for the "most used" list. Five bits
        /// gives 32 levels a channel, so shades within about 8/255 of each other count as the same
        /// colour. That is deliberate: on any photographic or anti-aliased image the exact top
        /// colours are thousands of near-identical neighbours, and a list of them says nothing.
        /// The exact figure is reported separately as UniqueColors.</summary>
        private const int GroupBits = 5;

        public static ImageStats Compute(PaintDocument doc)
        {
            var s = new ImageStats
            {
                Width = doc.Width,
                Height = doc.Height,
                LayerCount = doc.Layers.Count,
            };

            foreach (var layer in doc.Layers)
            {
                if (layer.Visible) s.VisibleLayerCount++;
                if (layer.Text != null) s.TextLayerCount++;
                s.MemoryBytes += (long)layer.Surface.Width * layer.Surface.Height * 4;
            }

            var flat = doc.GetFlattenedBitmap();
            int w = flat.PixelWidth, h = flat.PixelHeight;
            int stride = w * 4;
            var px = new byte[stride * h];
            flat.CopyPixels(px, stride, 0);

            // 2^24 bits, one per RGB triple. A HashSet<int> would work but allocates per distinct
            // colour, and a photographic image can hold millions of them.
            var seen = new ulong[1 << 24 >> 6];

            int groupShift = 8 - GroupBits;
            int groupSize = 1 << (GroupBits * 3);
            var groupCount = new int[groupSize];
            // Summed channels per group, so the swatch shown is the group's average rather than
            // whichever member happened to be found first.
            var groupR = new long[groupSize];
            var groupG = new long[groupSize];
            var groupB = new long[groupSize];

            double sumR = 0, sumG = 0, sumB = 0, sumLum = 0, sumLumSq = 0;
            s.MinLum = 255;
            s.MaxLum = 0;

            for (int i = 0; i < px.Length; i += 4)
            {
                byte a = px[i + 3];
                if (a == 0) { s.TransparentPixels++; continue; }
                if (a == 255) s.OpaquePixels++; else s.PartialPixels++;

                // The buffer is Pbgra32 - premultiplied - so the stored channels are scaled by
                // alpha. Undo that before measuring, or every semi-transparent pixel would be
                // recorded as darker than it actually is and drag the averages down with it.
                int b = px[i], g = px[i + 1], r = px[i + 2];
                if (a < 255)
                {
                    r = Math.Min(255, r * 255 / a);
                    g = Math.Min(255, g * 255 / a);
                    b = Math.Min(255, b * 255 / a);
                }

                s.HistR[r]++; s.HistG[g]++; s.HistB[b]++;
                sumR += r; sumG += g; sumB += b;

                int lum = (int)Math.Round(LumR * r + LumG * g + LumB * b);
                if (lum > 255) lum = 255;
                s.HistLum[lum]++;
                sumLum += lum;
                sumLumSq += (double)lum * lum;
                if (lum < s.MinLum) s.MinLum = lum;
                if (lum > s.MaxLum) s.MaxLum = lum;

                int key = (r << 16) | (g << 8) | b;
                int word = key >> 6, bit = key & 63;
                if ((seen[word] & (1UL << bit)) == 0)
                {
                    seen[word] |= 1UL << bit;
                    s.UniqueColors++;
                }

                int gi = ((r >> groupShift) << (GroupBits * 2)) | ((g >> groupShift) << GroupBits) | (b >> groupShift);
                groupCount[gi]++;
                groupR[gi] += r; groupG[gi] += g; groupB[gi] += b;
            }

            long n = s.ColoredPixels;
            if (n == 0)
            {
                // Nothing visible at all. Leave the averages at zero and say so rather than
                // dividing by it - MinLum would otherwise still read 255 from its seed value.
                s.MinLum = 0;
                return s;
            }

            s.MeanR = sumR / n;
            s.MeanG = sumG / n;
            s.MeanB = sumB / n;
            s.MeanLum = sumLum / n;
            s.StdDevLum = Math.Sqrt(Math.Max(0, sumLumSq / n - s.MeanLum * s.MeanLum));
            s.MedianLum = MedianOf(s.HistLum, n);
            s.TopColors = TopGroups(groupCount, groupR, groupG, groupB, n, 8);
            return s;
        }

        /// <summary>The bin the middle pixel falls in, walking the histogram until half the
        /// population has been passed.</summary>
        private static int MedianOf(int[] hist, long total)
        {
            long half = total / 2, running = 0;
            for (int i = 0; i < hist.Length; i++)
            {
                running += hist[i];
                if (running > half) return i;
            }
            return 0;
        }

        private static List<(Color, double)> TopGroups(int[] count, long[] sr, long[] sg, long[] sb,
                                                       long total, int take)
        {
            var best = new List<int>();
            for (int i = 0; i < count.Length; i++)
            {
                if (count[i] == 0) continue;
                best.Add(i);
            }
            best.Sort((x, y) => count[y].CompareTo(count[x]));

            var result = new List<(Color, double)>();
            for (int i = 0; i < Math.Min(take, best.Count); i++)
            {
                int gi = best[i];
                int c = count[gi];
                result.Add((Color.FromRgb((byte)(sr[gi] / c), (byte)(sg[gi] / c), (byte)(sb[gi] / c)),
                            100.0 * c / total));
            }
            return result;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.##} GB";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.##} MB";
            if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):0.##} KB";
            return $"{bytes} bytes";
        }
    }
}
