using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaintClone.Models
{
    /// <summary>
    /// Wraps a WriteableBitmap (Pbgra32) and provides direct, unsafe pixel access plus
    /// the small set of raster primitives the whole application draws with:
    /// SetPixel / GetPixel / line / rect / ellipse / flood-fill / blit.
    ///
    /// Everything the app draws (pencil, brush, shapes, text, pasted selections)
    /// ultimately funnels through this class so the document is a *real* bitmap,
    /// not a collection of vector shapes.
    /// </summary>
    public sealed unsafe class RasterSurface
    {
        public WriteableBitmap Bitmap { get; private set; }
        public int Width => Bitmap.PixelWidth;
        public int Height => Bitmap.PixelHeight;

        private int _stride;
        private byte* _buffer;
        private bool _locked;

        public RasterSurface(int width, int height, Color background)
        {
            Bitmap = new WriteableBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32, null);
            Clear(background);
        }

        public RasterSurface(WriteableBitmap existing)
        {
            Bitmap = existing;
        }

        public void Lock()
        {
            if (_locked) return;
            Bitmap.Lock();
            _stride = Bitmap.BackBufferStride;
            _buffer = (byte*)Bitmap.BackBuffer.ToPointer();
            _locked = true;
        }

        public void Unlock(Int32Rect? dirty = null)
        {
            if (!_locked) return;
            var rect = dirty ?? new Int32Rect(0, 0, Width, Height);
            if (rect.Width > 0 && rect.Height > 0)
                Bitmap.AddDirtyRect(rect);
            Bitmap.Unlock();
            _locked = false;
        }

        // Pbgra32 stores premultiplied BGRA; we treat colors as fully opaque (A=255) almost everywhere,
        // which matches classic Paint (no alpha compositing concept).
        private static uint PremultiplyStore(Color c)
        {
            byte a = c.A;
            // Rounded, not truncated. A semi-transparent pixel goes through several
            // premultiply/unpremultiply round trips on its way from being drawn to being committed
            // (render -> blit into the preview layer -> blend into the document), and truncating at
            // each step let the error accumulate in one direction - which is why a translucent
            // gradient drifted slightly in colour at each stage instead of staying put.
            byte r = (byte)((c.R * a + 127) / 255);
            byte g = (byte)((c.G * a + 127) / 255);
            byte b = (byte)((c.B * a + 127) / 255);
            return (uint)((a << 24) | (r << 16) | (g << 8) | b);
        }

        public void SetPixel(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || !_locked) return;
            uint* p = (uint*)(_buffer + y * _stride + x * 4);
            *p = PremultiplyStore(c);
        }

        public Color GetPixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return Colors.Transparent;
            bool wasLocked = _locked;
            if (!wasLocked) Lock();
            uint v = *(uint*)(_buffer + y * _stride + x * 4);
            byte a = (byte)((v >> 24) & 0xFF);
            byte r = (byte)((v >> 16) & 0xFF);
            byte g = (byte)((v >> 8) & 0xFF);
            byte b = (byte)(v & 0xFF);
            if (a > 0 && a < 255)
            {
                // Rounded to match PremultiplyStore, so a store/read round trip is as close to
                // lossless as 8-bit premultiplied storage allows.
                r = (byte)Math.Min(255, (r * 255 + a / 2) / a);
                g = (byte)Math.Min(255, (g * 255 + a / 2) / a);
                b = (byte)Math.Min(255, (b * 255 + a / 2) / a);
            }
            if (!wasLocked) Unlock(new Int32Rect(0, 0, 0, 0));
            return Color.FromArgb(a, r, g, b);
        }

        /// <summary>Snaps every pixel's alpha to strictly 0 or 255 (no in-between values) - used
        /// after rendering text, to guarantee anti-aliasing edge pixels can never reach the
        /// document as semi-transparent "holes" once blitted (Blit does a hard overwrite, not
        /// alpha blending, so a stray semi-transparent pixel would otherwise punch a visible gap
        /// wherever it landed). cutoff is the alpha value at/above which a pixel counts as opaque.</summary>
        public void ThresholdAlpha(byte cutoff = 96)
        {
            Lock();
            try
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        var c = ReadRawUnpremultiplied(x, y);
                        if (c.A == 0 || c.A == 255) continue; // already binary, nothing to do
                        if (c.A >= cutoff)
                            SetPixel(x, y, Color.FromArgb(255, c.R, c.G, c.B));
                        else
                            SetPixel(x, y, Colors.Transparent);
                    }
                }
            }
            finally
            {
                Unlock();
            }
        }

        public void Clear(Color c)
        {
            Lock();
            uint packed = PremultiplyStore(c);

            if (packed == 0)
            {
                // Fully-transparent clear (the hot path: called every mouse-move to reset the
                // tool preview layer) - a raw memory clear instead of a per-pixel managed loop.
                System.Runtime.InteropServices.NativeMemory.Clear((void*)_buffer, (nuint)(_stride * Height));
            }
            else
            {
                // Build one row once, then bulk-copy it down - O(Height) memcpy calls instead of
                // O(Width*Height) individual pointer writes.
                var rowArray = new uint[Width];
                for (int i = 0; i < Width; i++) rowArray[i] = packed;
                fixed (uint* rowPtr = rowArray)
                {
                    for (int y = 0; y < Height; y++)
                        Buffer.MemoryCopy(rowPtr, _buffer + y * _stride, _stride, Width * 4);
                }
            }
            Unlock();
        }

        /// <summary>Filled square stamp used by pencil / eraser / brush "square" shape.</summary>
        public void StampSquare(int cx, int cy, int size, Color c)
        {
            int half = Math.Max(1, size) / 2;
            int x0 = cx - half, y0 = cy - half;
            int x1 = x0 + Math.Max(1, size), y1 = y0 + Math.Max(1, size);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    SetPixel(x, y, c);
        }

        /// <summary>Filled circular stamp used by brush "round" shape and airbrush spray radius.</summary>
        public void StampCircle(int cx, int cy, int radius, Color c)
        {
            int r2 = radius * radius;
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                    if (x * x + y * y <= r2)
                        SetPixel(cx + x, cy + y, c);
        }

        /// <summary>Thin "/" calligraphy-nib stamp (classic Paint's two diagonal brush shapes).</summary>
        public void StampDiagonalRight(int cx, int cy, int size, Color c)
        {
            int half = Math.Max(1, size);
            int nib = Math.Max(1, size / 3);
            DrawLine(cx - half, cy + half, cx + half, cy - half, c, nib);
        }

        /// <summary>Thin "\" calligraphy-nib stamp.</summary>
        public void StampDiagonalLeft(int cx, int cy, int size, Color c)
        {
            int half = Math.Max(1, size);
            int nib = Math.Max(1, size / 3);
            DrawLine(cx - half, cy - half, cx + half, cy + half, c, nib);
        }

        /// <summary>Sparse star-shaped stamp for a rougher, more textured brush stroke.</summary>
        public void StampSplatter(int cx, int cy, int radius, Color c)
        {
            SetPixel(cx, cy, c);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                for (int r = 1; r <= radius; r += 2)
                {
                    int x = cx + (int)Math.Round(Math.Cos(a) * r);
                    int y = cy + (int)Math.Round(Math.Sin(a) * r);
                    SetPixel(x, y, c);
                }
            }
        }

        /// <summary>Plus/cross-shaped stamp.</summary>
        public void StampCross(int cx, int cy, int size, Color c)
        {
            int half = Math.Max(1, size);
            int thickness = Math.Max(1, size / 3);
            DrawLine(cx - half, cy, cx + half, cy, c, thickness);
            DrawLine(cx, cy - half, cx, cy + half, c, thickness);
        }

        /// <summary>Fuzzy-edged round stamp - pixels near the center always deposit, pixels near
        /// the edge deposit with decreasing probability, giving a soft airbrush-like falloff
        /// rather than a hard circular edge.</summary>
        /// <summary>Filled equilateral-ish triangle, point upward.</summary>
        public void StampTriangle(int cx, int cy, int size, Color c)
        {
            int h = Math.Max(1, size);
            int half = h / 2;
            for (int dy = 0; dy < h; dy++)
            {
                // Width grows linearly from the apex down to the base.
                int rowHalf = (dy * half) / Math.Max(1, h - 1);
                for (int dx = -rowHalf; dx <= rowHalf; dx++)
                    SetPixel(cx + dx, cy - half + dy, c);
            }
        }

        /// <summary>Filled diamond / rhombus (a square rotated 45 degrees).</summary>
        public void StampDiamond(int cx, int cy, int size, Color c)
        {
            int r = Math.Max(1, size / 2);
            for (int dy = -r; dy <= r; dy++)
            {
                int rowHalf = r - Math.Abs(dy);
                for (int dx = -rowHalf; dx <= rowHalf; dx++)
                    SetPixel(cx + dx, cy + dy, c);
            }
        }

        /// <summary>Five-pointed star, drawn by filling from the centre out to the star's radius
        /// at each angle (radius alternates between outer and inner points).</summary>
        public void StampStar(int cx, int cy, int size, Color c)
        {
            double outer = Math.Max(1, size / 2.0);
            double inner = outer * 0.42;
            for (int dy = -(int)outer; dy <= (int)outer; dy++)
            {
                for (int dx = -(int)outer; dx <= (int)outer; dx++)
                {
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > outer) continue;
                    double ang = Math.Atan2(dy, dx) + Math.PI / 2; // point straight up
                    // Sawtooth between inner and outer radius, five times around the circle.
                    double phase = (ang % (2 * Math.PI / 5) + 2 * Math.PI / 5) % (2 * Math.PI / 5);
                    double t = Math.Abs(phase - Math.PI / 5) / (Math.PI / 5);
                    double limit = inner + (outer - inner) * t;
                    if (dist <= limit) SetPixel(cx + dx, cy + dy, c);
                }
            }
        }

        /// <summary>Hollow circle (ring) - an outline rather than a filled dab.</summary>
        public void StampRing(int cx, int cy, int size, Color c)
        {
            double r = Math.Max(1, size / 2.0);
            double inner = Math.Max(0, r - Math.Max(1, size / 4.0));
            for (int dy = -(int)r; dy <= (int)r; dy++)
                for (int dx = -(int)r; dx <= (int)r; dx++)
                {
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= r && d >= inner) SetPixel(cx + dx, cy + dy, c);
                }
        }

        /// <summary>Hollow square outline.</summary>
        public void StampHollowSquare(int cx, int cy, int size, Color c)
        {
            int half = Math.Max(1, size) / 2;
            int thick = Math.Max(1, size / 4);
            for (int dy = -half; dy <= half; dy++)
                for (int dx = -half; dx <= half; dx++)
                    if (Math.Abs(dx) > half - thick || Math.Abs(dy) > half - thick)
                        SetPixel(cx + dx, cy + dy, c);
        }

        /// <summary>Wide flat horizontal nib - broad strokes sideways, thin vertically.</summary>
        public void StampHorizontalBar(int cx, int cy, int size, Color c)
        {
            int half = Math.Max(1, size);
            int thick = Math.Max(1, size / 4);
            for (int dy = -thick / 2; dy <= thick / 2; dy++)
                for (int dx = -half; dx <= half; dx++)
                    SetPixel(cx + dx, cy + dy, c);
        }

        /// <summary>Tall narrow vertical nib - the transpose of the horizontal bar.</summary>
        public void StampVerticalBar(int cx, int cy, int size, Color c)
        {
            int half = Math.Max(1, size);
            int thick = Math.Max(1, size / 4);
            for (int dx = -thick / 2; dx <= thick / 2; dx++)
                for (int dy = -half; dy <= half; dy++)
                    SetPixel(cx + dx, cy + dy, c);
        }

        /// <summary>Angled calligraphy nib - a broad, steeply-slanted flat edge, which naturally
        /// produces thick-to-thin stroke variation depending on the direction of travel.</summary>
        public void StampCalligraphy(int cx, int cy, int size, Color c)
        {
            int len = Math.Max(2, size);
            int thick = Math.Max(1, size / 3);
            for (int i = -len / 2; i <= len / 2; i++)
                for (int t = 0; t < thick; t++)
                {
                    // Slope of roughly 30 degrees, the classic broad-nib angle.
                    SetPixel(cx + i, cy - i / 2 + t, c);
                }
        }

        /// <summary>Chalk / charcoal - a filled dab with randomly dropped pixels, so strokes have
        /// a dry, grainy texture rather than a solid edge.</summary>
        public void StampChalk(int cx, int cy, int size, Color c, Random rng)
        {
            double r = Math.Max(1, size / 2.0);
            for (int dy = -(int)r; dy <= (int)r; dy++)
                for (int dx = -(int)r; dx <= (int)r; dx++)
                {
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > r) continue;
                    // Denser in the middle, sparser at the rim, for a soft grainy falloff.
                    double keep = 0.85 - 0.6 * (d / r);
                    if (rng.NextDouble() < keep) SetPixel(cx + dx, cy + dy, c);
                }
        }

        /// <summary>Stipple - sparse scattered dots across the dab area, for dotted/textured work.</summary>
        public void StampStipple(int cx, int cy, int size, Color c, Random rng)
        {
            double r = Math.Max(1, size / 2.0);
            int dots = Math.Max(2, size / 2);
            for (int i = 0; i < dots; i++)
            {
                double ang = rng.NextDouble() * Math.PI * 2;
                double dist = rng.NextDouble() * r;
                SetPixel(cx + (int)(Math.Cos(ang) * dist), cy + (int)(Math.Sin(ang) * dist), c);
            }
        }

        public void StampSoft(int cx, int cy, int radius, Color c, Random rng)
        {
            if (radius < 1) radius = 1;
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    double dist = Math.Sqrt(x * x + y * y);
                    if (dist > radius) continue;
                    double falloff = 1.0 - dist / radius; // 1 at center, 0 at edge
                    if (rng.NextDouble() < falloff * falloff)
                        SetPixel(cx + x, cy + y, c);
                }
            }
        }

        /// <summary>Bresenham line, stamped with the given pen size.</summary>
        /// <summary>When true, line and ellipse outlines are drawn with anti-aliased (partially
        /// covered) edge pixels instead of hard on/off ones. Tools set this from their own
        /// anti-aliasing option immediately before drawing, so it always reflects whatever the
        /// current tool is configured for rather than being global state anyone has to remember
        /// to reset.</summary>
        public bool AntiAlias { get; set; }

        /// <summary>Writes a colour over the existing pixel with the given coverage (0..1). Unlike
        /// SetPixel, which hard-overwrites, this blends - which is what makes a partially covered
        /// edge pixel look like a smooth edge rather than a lighter dot on a hard one.</summary>
        public void BlendPixel(int x, int y, Color c, double coverage)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || !_locked) return;
            if (coverage <= 0) return;
            if (coverage > 1) coverage = 1;

            var dst = ReadRawUnpremultiplied(x, y);
            double srcA = (c.A / 255.0) * coverage;
            if (srcA <= 0) return;
            double dstA = dst.A / 255.0;
            double outA = srcA + dstA * (1 - srcA);
            if (outA <= 0) { SetPixel(x, y, Colors.Transparent); return; }

            byte Mix(byte sc, byte dc) =>
                (byte)Math.Round((sc * srcA + dc * dstA * (1 - srcA)) / outA);

            SetPixel(x, y, Color.FromArgb((byte)Math.Round(outA * 255),
                Mix(c.R, dst.R), Mix(c.G, dst.G), Mix(c.B, dst.B)));
        }

        /// <summary>Anti-aliased line, thickness-aware. Rather than Wu's classic single-pixel
        /// algorithm (which only handles hairlines), this walks the line's bounding box and shades
        /// each pixel by how far its centre lies from the line - giving a smooth edge at any
        /// thickness, which is what the drawing tools actually need.</summary>
        public void DrawLineAA(int x0, int y0, int x1, int y1, Color c, int size)
        {
            double half = Math.Max(1, size) / 2.0;
            double dx = x1 - x0, dy = y1 - y0;
            double lenSq = dx * dx + dy * dy;

            int minX = (int)Math.Floor(Math.Min(x0, x1) - half - 1);
            int maxX = (int)Math.Ceiling(Math.Max(x0, x1) + half + 1);
            int minY = (int)Math.Floor(Math.Min(y0, y1) - half - 1);
            int maxY = (int)Math.Ceiling(Math.Max(y0, y1) + half + 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Distance from this pixel's centre to the nearest point on the segment.
                    double px = x + 0.5 - x0, py = y + 0.5 - y0;
                    double t = lenSq > 0 ? Math.Max(0, Math.Min(1, (px * dx + py * dy) / lenSq)) : 0;
                    double ox = px - t * dx, oy = py - t * dy;
                    double dist = Math.Sqrt(ox * ox + oy * oy);

                    // Full coverage inside the stroke, fading to none across the final pixel -
                    // a one-pixel ramp is what reads as a smooth edge without looking blurry.
                    double coverage = half - dist + 0.5;
                    if (coverage > 0) BlendPixel(x, y, c, coverage);
                }
            }
        }

        public void DrawLine(int x0, int y0, int x1, int y1, Color c, int size)
        {
            if (AntiAlias) { DrawLineAA(x0, y0, x1, y1, c, size); return; }
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (size <= 1) SetPixel(x0, y0, c); else StampSquare(x0, y0, size, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public void DrawRect(Int32Rect r, Color outline, int thickness, bool fill, Color fillColor)
        {
            if (fill)
            {
                for (int y = r.Y; y < r.Y + r.Height; y++)
                    for (int x = r.X; x < r.X + r.Width; x++)
                        SetPixel(x, y, fillColor);
            }
            for (int t = 0; t < thickness; t++)
            {
                DrawLine(r.X, r.Y + t, r.X + r.Width - 1, r.Y + t, outline, 1);
                DrawLine(r.X, r.Y + r.Height - 1 - t, r.X + r.Width - 1, r.Y + r.Height - 1 - t, outline, 1);
                DrawLine(r.X + t, r.Y, r.X + t, r.Y + r.Height - 1, outline, 1);
                DrawLine(r.X + r.Width - 1 - t, r.Y, r.X + r.Width - 1 - t, r.Y + r.Height - 1, outline, 1);
            }
        }

        /// <summary>Anti-aliased ellipse outline: shades each pixel by how far its centre sits
        /// from the true ellipse edge. An ellipse is diagonal almost everywhere along its
        /// perimeter, so it's the shape where hard pixel edges show up worst.</summary>
        private void DrawEllipseOutlineAA(double cx, double cy, double rx, double ry, Color c, int thickness)
        {
            double half = Math.Max(1, thickness) / 2.0;
            int x0 = (int)Math.Floor(cx - rx - half - 1), x1 = (int)Math.Ceiling(cx + rx + half + 1);
            int y0 = (int)Math.Floor(cy - ry - half - 1), y1 = (int)Math.Ceiling(cy + ry + half + 1);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    double nx = (x + 0.5 - cx) / rx, ny = (y + 0.5 - cy) / ry;
                    double r = Math.Sqrt(nx * nx + ny * ny);
                    if (r <= 0.0001) continue;

                    // Convert the normalised radial error back into approximate pixel distance,
                    // so the edge ramp is a consistent width regardless of how elongated the
                    // ellipse is.
                    double scale = Math.Min(rx, ry);
                    double dist = Math.Abs(r - 1.0) * scale;
                    double coverage = half - dist + 0.5;
                    if (coverage > 0) BlendPixel(x, y, c, coverage);
                }
            }
        }

        public void DrawEllipse(Int32Rect bounds, Color outline, int thickness, bool fill, Color fillColor)
        {
            double rx = bounds.Width / 2.0, ry = bounds.Height / 2.0;
            double cx = bounds.X + rx, cy = bounds.Y + ry;
            if (rx < 0.5 || ry < 0.5) return;

            if (fill)
            {
                for (int y = bounds.Y; y < bounds.Y + bounds.Height; y++)
                {
                    double dy = (y + 0.5 - cy) / ry;
                    double t = 1 - dy * dy;
                    if (t < 0) continue;
                    double dx = rx * Math.Sqrt(t);
                    int xs = (int)Math.Round(cx - dx), xe = (int)Math.Round(cx + dx);
                    for (int x = xs; x <= xe; x++) SetPixel(x, y, fillColor);
                }
            }

            if (AntiAlias)
            {
                DrawEllipseOutlineAA(cx, cy, rx, ry, outline, thickness);
                return;
            }

            // Outline via parametric sampling - simple and robust for arbitrary aspect ratios.
            int steps = (int)(4 * (bounds.Width + bounds.Height));
            steps = Math.Max(steps, 64);
            for (int i = 0; i <= steps; i++)
            {
                double a = 2 * Math.PI * i / steps;
                int x = (int)Math.Round(cx + rx * Math.Cos(a));
                int y = (int)Math.Round(cy + ry * Math.Sin(a));
                if (thickness <= 1) SetPixel(x, y, outline); else StampSquare(x, y, thickness, outline);
            }
        }

        public void DrawRoundedRect(Int32Rect r, int cornerRadius, Color outline, int thickness, bool fill, Color fillColor)
        {
            int rad = Math.Max(0, Math.Min(cornerRadius, Math.Min(r.Width, r.Height) / 2));
            if (fill)
            {
                for (int y = r.Y; y < r.Y + r.Height; y++)
                {
                    for (int x = r.X; x < r.X + r.Width; x++)
                    {
                        if (IsInsideRoundedRect(x, y, r, rad)) SetPixel(x, y, fillColor);
                    }
                }
            }
            int steps = Math.Max(64, 4 * (r.Width + r.Height));
            // Approximate outline: walk the boundary of the rounded-rect region.
            for (int y = r.Y; y < r.Y + r.Height; y++)
            {
                for (int x = r.X; x < r.X + r.Width; x++)
                {
                    if (!IsInsideRoundedRect(x, y, r, rad)) continue;
                    bool edge =
                        !IsInsideRoundedRect(x - 1, y, r, rad) || !IsInsideRoundedRect(x + 1, y, r, rad) ||
                        !IsInsideRoundedRect(x, y - 1, r, rad) || !IsInsideRoundedRect(x, y + 1, r, rad);
                    if (edge)
                    {
                        if (thickness <= 1) SetPixel(x, y, outline); else StampSquare(x, y, thickness, outline);
                    }
                }
            }
        }

        private static bool IsInsideRoundedRect(int x, int y, Int32Rect r, int rad)
        {
            if (x < r.X || y < r.Y || x >= r.X + r.Width || y >= r.Y + r.Height) return false;
            int lx = x - r.X, ly = y - r.Y;
            int w = r.Width, h = r.Height;
            if (rad <= 0) return true;
            // corners
            if (lx < rad && ly < rad) return InCircle(lx, ly, rad, rad, rad);
            if (lx >= w - rad && ly < rad) return InCircle(lx, ly, w - rad - 1, rad, rad);
            if (lx < rad && ly >= h - rad) return InCircle(lx, ly, rad, h - rad - 1, rad);
            if (lx >= w - rad && ly >= h - rad) return InCircle(lx, ly, w - rad - 1, h - rad - 1, rad);
            return true;
        }

        private static bool InCircle(int x, int y, int cx, int cy, int r)
        {
            int dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>Classic 4-connected flood fill with exact color matching (no tolerance/anti-alias awareness),
        /// matching legacy Paint semantics. Runs on a plain managed scanline stack so huge fills don't blow the call stack.</summary>
        public void FloodFill(int startX, int startY, Color newColor)
        {
            if (startX < 0 || startY < 0 || startX >= Width || startY >= Height) return;
            Color target = GetPixel(startX, startY);
            if (ColorsEqual(target, newColor)) return;

            Lock();
            try
            {
                var stack = new System.Collections.Generic.Stack<(int x, int y)>();
                stack.Push((startX, startY));
                uint newPacked = PremultiplyStore(newColor);

                while (stack.Count > 0)
                {
                    var (x, y) = stack.Pop();
                    if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                    if (!ColorsEqual(ReadRaw(x, y), target)) continue;

                    // scan left
                    int xl = x;
                    while (xl - 1 >= 0 && ColorsEqual(ReadRaw(xl - 1, y), target)) xl--;
                    int xr = x;
                    while (xr + 1 < Width && ColorsEqual(ReadRaw(xr + 1, y), target)) xr++;

                    for (int xi = xl; xi <= xr; xi++)
                        *(uint*)(_buffer + y * _stride + xi * 4) = newPacked;

                    if (y - 1 >= 0)
                        for (int xi = xl; xi <= xr; xi++)
                            if (ColorsEqual(ReadRaw(xi, y - 1), target)) stack.Push((xi, y - 1));
                    if (y + 1 < Height)
                        for (int xi = xl; xi <= xr; xi++)
                            if (ColorsEqual(ReadRaw(xi, y + 1), target)) stack.Push((xi, y + 1));
                }
            }
            finally
            {
                Unlock();
            }
        }

        private Color ReadRaw(int x, int y) => ReadRawUnpremultiplied(x, y);

        /// <summary>Same 4-connected, exact-color-matching scanline algorithm as FloodFill, but
        /// builds a selection mask instead of writing pixels - used by the Magic Wand tool.
        /// Returns the bounding box of the matched region and a mask sized to that box.</summary>
        public (Int32Rect Bounds, bool[,] Mask) MagicWandSelect(int startX, int startY, int tolerance = 0, bool contiguous = true)
        {
            if (startX < 0 || startY < 0 || startX >= Width || startY >= Height)
                return (new Int32Rect(0, 0, 1, 1), new bool[1, 1]);

            Color target = GetPixel(startX, startY);
            var visited = new bool[Width, Height];

            // Tolerance compares each channel (including alpha) against the clicked colour. Squared
            // Euclidean distance would spread unevenly across hues; a per-channel limit is what
            // people actually expect from a "tolerance" number - raise it and visibly more of the
            // neighbouring shades come in.
            bool Matches(Color c)
            {
                if (tolerance <= 0) return ColorsEqual(c, target);
                return Math.Abs(c.R - target.R) <= tolerance
                    && Math.Abs(c.G - target.G) <= tolerance
                    && Math.Abs(c.B - target.B) <= tolerance
                    && Math.Abs(c.A - target.A) <= tolerance;
            }

            Lock();
            try
            {
                if (!contiguous)
                {
                    // Global mode: select every matching pixel anywhere in the layer, not just the
                    // region connected to the click. Useful for grabbing one colour that appears in
                    // several separate places at once.
                    for (int y = 0; y < Height; y++)
                        for (int x = 0; x < Width; x++)
                            if (Matches(ReadRaw(x, y))) visited[x, y] = true;
                }
                else
                {

                var stack = new System.Collections.Generic.Stack<(int x, int y)>();
                stack.Push((startX, startY));

                while (stack.Count > 0)
                {
                    var (x, y) = stack.Pop();
                    if (x < 0 || y < 0 || x >= Width || y >= Height || visited[x, y]) continue;
                    if (!Matches(ReadRaw(x, y))) continue;

                    int xl = x;
                    while (xl - 1 >= 0 && !visited[xl - 1, y] && Matches(ReadRaw(xl - 1, y))) xl--;
                    int xr = x;
                    while (xr + 1 < Width && !visited[xr + 1, y] && Matches(ReadRaw(xr + 1, y))) xr++;

                    for (int xi = xl; xi <= xr; xi++) visited[xi, y] = true;

                    if (y - 1 >= 0)
                        for (int xi = xl; xi <= xr; xi++)
                            if (!visited[xi, y - 1] && Matches(ReadRaw(xi, y - 1))) stack.Push((xi, y - 1));
                    if (y + 1 < Height)
                        for (int xi = xl; xi <= xr; xi++)
                            if (!visited[xi, y + 1] && Matches(ReadRaw(xi, y + 1))) stack.Push((xi, y + 1));
                }
                }
            }
            finally
            {
                Unlock();
            }

            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (visited[x, y]) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }

            if (maxX < 0) return (new Int32Rect(startX, startY, 1, 1), new bool[1, 1] { { true } });

            int w = maxX - minX + 1, h = maxY - minY + 1;
            var mask = new bool[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mask[x, y] = visited[minX + x, minY + y];

            return (new Int32Rect(minX, minY, w, h), mask);
        }

        /// <summary>Fast pixel read that assumes the caller already called Lock() - used when one
        /// RasterSurface needs to read pixels from another already-locked surface without paying
        /// the Lock()/Unlock() cost on every single pixel (that cost was the main performance
        /// bottleneck in Blit before this was added).</summary>
        internal Color ReadRawUnpremultiplied(int x, int y)
        {
            uint v = *(uint*)(_buffer + y * _stride + x * 4);
            byte a = (byte)((v >> 24) & 0xFF);
            byte r = (byte)((v >> 16) & 0xFF);
            byte g = (byte)((v >> 8) & 0xFF);
            byte b = (byte)(v & 0xFF);
            if (a > 0 && a < 255)
            {
                // Rounded to match PremultiplyStore, so a store/read round trip is as close to
                // lossless as 8-bit premultiplied storage allows.
                r = (byte)Math.Min(255, (r * 255 + a / 2) / a);
                g = (byte)Math.Min(255, (g * 255 + a / 2) / a);
                b = (byte)Math.Min(255, (b * 255 + a / 2) / a);
            }
            return Color.FromArgb(a, r, g, b);
        }

        private static bool ColorsEqual(Color a, Color b) => a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;

        /// <summary>Copies a rectangular region out as a standalone WriteableBitmap (used by selection/copy/paste).</summary>
        public WriteableBitmap CopyRegion(Int32Rect r)
        {
            r.X = Math.Max(0, r.X); r.Y = Math.Max(0, r.Y);
            r.Width = Math.Min(r.Width, Width - r.X);
            r.Height = Math.Min(r.Height, Height - r.Y);
            if (r.Width <= 0 || r.Height <= 0) return new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);

            var dest = new WriteableBitmap(r.Width, r.Height, 96, 96, PixelFormats.Pbgra32, null);
            int destStride = dest.BackBufferStride;
            byte[] scan = new byte[r.Width * 4];
            dest.Lock();
            Lock();
            try
            {
                for (int y = 0; y < r.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        (IntPtr)(_buffer + (r.Y + y) * _stride + r.X * 4), scan, 0, r.Width * 4);
                    System.Runtime.InteropServices.Marshal.Copy(
                        scan, 0, dest.BackBuffer + y * destStride, r.Width * 4);
                }
            }
            finally
            {
                Unlock(new Int32Rect(0, 0, 0, 0));
                dest.AddDirtyRect(new Int32Rect(0, 0, r.Width, r.Height));
                dest.Unlock();
            }
            return dest;
        }

        /// <summary>Blits a source bitmap onto this surface at (x,y). transparentColor, if given, is skipped (legacy "transparent paste").</summary>
        public void Blit(WriteableBitmap src, int x, int y, Color? transparentColor = null)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            var srcSurface = new RasterSurface(src);
            srcSurface.Lock();  // lock ONCE for the whole blit, not once per pixel (was the #1 perf killer)
            Lock();
            try
            {
                for (int sy = 0; sy < h; sy++)
                {
                    int dy = y + sy;
                    if (dy < 0 || dy >= Height) continue;
                    for (int sx = 0; sx < w; sx++)
                    {
                        int dx = x + sx;
                        if (dx < 0 || dx >= Width) continue;
                        Color c = srcSurface.ReadRawUnpremultiplied(sx, sy);
                        // Any fully-transparent source pixel is skipped whenever the caller asked
                        // to key out a fully-transparent colour. Comparing all four channels isn't
                        // enough here: an untouched pixel reads back as (0,0,0,0), which does NOT
                        // equal Colors.Transparent (#00FFFFFF), so it used to fail the test and get
                        // written - stamping transparent-black over whatever was already on the
                        // canvas. That's what erased the background behind committed text.
                        if (transparentColor.HasValue &&
                            (ColorsEqual(c, transparentColor.Value) ||
                             (transparentColor.Value.A == 0 && c.A == 0))) continue;
                        *(uint*)(_buffer + dy * _stride + dx * 4) = PremultiplyStore(c);
                    }
                }
            }
            finally
            {
                Unlock();
                srcSurface.Unlock(new Int32Rect(0, 0, 0, 0));
            }
        }

        public byte[] SnapshotBytes()
        {
            Lock();
            var arr = new byte[Bitmap.BackBufferStride * Height];
            System.Runtime.InteropServices.Marshal.Copy((IntPtr)_buffer, arr, 0, arr.Length);
            Unlock(new Int32Rect(0, 0, 0, 0));
            return arr;
        }

        public void RestoreBytes(byte[] data)
        {
            Lock();
            System.Runtime.InteropServices.Marshal.Copy(data, 0, (IntPtr)_buffer, data.Length);
            Unlock();
        }
    }
}
