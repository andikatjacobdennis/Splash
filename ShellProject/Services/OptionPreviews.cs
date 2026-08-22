using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaintClone.Models;
using PaintClone.Tools;

namespace PaintClone.Services
{
    /// <summary>
    /// Little bitmaps shown beside each entry in the tool-options dropdowns, so a choice can be
    /// recognised by what it actually looks like rather than only by its name.
    ///
    /// Wherever it's practical these are rendered with the *same* drawing code the tool itself uses
    /// - BrushTool.StampShape for brush shapes, RasterSurface.DrawRect/DrawLine for fill modes and
    /// edges, GradientTool's own position maths for blends. That's deliberate: a preview drawn by a
    /// separate hand-rolled approximation is one that can quietly stop matching what the tool
    /// really does, and a wrong preview is worse than none.
    ///
    /// Everything returns an ImageSource rather than a live control. A WPF element can only have
    /// one parent, and a ComboBox renders its selected entry a second time inside the closed box -
    /// so returning controls here would throw the moment an entry with a preview was selected. An
    /// ImageSource is shareable, so the dropdown row and the closed box can each draw their own.
    /// </summary>
    public static class OptionPreviews
    {
        // Previews are drawn in the glyph colour of whichever theme is active, looked up per call
        // so a Dark/Light switch is picked up the next time the options bar is rebuilt.
        private static Color GlyphColor()
        {
            if (Application.Current?.TryFindResource("PsIconGlyph") is SolidColorBrush b) return b.Color;
            return Colors.Black;
        }

        private static ImageSource Finish(RasterSurface s)
        {
            s.Bitmap.Freeze(); // shareable across the dropdown row and the closed selection box
            return s.Bitmap;
        }

        private static RasterSurface Blank(int w, int h) => new(w, h, Colors.Transparent);

        /// <summary>A single stamp of the given brush shape, drawn by the brush's own code.</summary>
        public static ImageSource BrushShapePreview(BrushShape shape)
        {
            const int size = 22;
            var s = Blank(size, size);
            s.Lock();
            try { BrushTool.StampShape(s, size / 2, size / 2, 14, GlyphColor(), shape); }
            finally { s.Unlock(); }
            return Finish(s);
        }

        /// <summary>A diagonal line drawn with anti-aliasing off vs on - the actual difference the
        /// Edges option makes, rather than a symbol standing in for it.</summary>
        public static ImageSource EdgesPreview(bool antiAlias)
        {
            var s = Blank(26, 16);
            s.Lock();
            try
            {
                s.AntiAlias = antiAlias;
                s.DrawLine(2, 13, 23, 2, GlyphColor(), 2);
                s.AntiAlias = false;
            }
            finally { s.Unlock(); }
            return Finish(s);
        }

        /// <summary>Outline / filled / both, drawn with the same DrawRect the shape tools use.</summary>
        public static ImageSource FillModePreview(ShapeFillMode mode)
        {
            var s = Blank(26, 18);
            var c = GlyphColor();
            var faint = Color.FromArgb(110, c.R, c.G, c.B);
            s.Lock();
            try
            {
                var r = new Int32Rect(2, 2, 21, 13);
                switch (mode)
                {
                    case ShapeFillMode.OutlineOnly: s.DrawRect(r, c, 2, false, Colors.Transparent); break;
                    case ShapeFillMode.FillOnly: s.DrawRect(r, faint, 1, true, faint); break;
                    default: s.DrawRect(r, c, 2, true, faint); break;
                }
            }
            finally { s.Unlock(); }
            return Finish(s);
        }

        /// <summary>The blend itself, using GradientTool's own position maths so each entry shows
        /// the shape that mode really produces.</summary>
        public static ImageSource GradientPreview(GradientType type)
        {
            const int w = 30, h = 18;
            var s = Blank(w, h);
            var c = GlyphColor();
            s.Lock();
            try
            {
                // Start at the left edge / centre height, end at the right edge - the same drag a
                // user would make to see this blend across the swatch.
                var start = new Point(0, h / 2.0);
                var end = new Point(w - 1, h / 2.0);
                double dx = end.X - start.X, dy = end.Y - start.Y;
                double lenSq = dx * dx + dy * dy;
                double len = Math.Sqrt(lenSq);

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        double t = GradientTool.PositionAlong(type, x, y, start, end, dx, dy, lenSq, len);
                        t = Math.Max(0, Math.Min(1, t));
                        byte a = (byte)Math.Round(255 * (1 - t)); // opaque at the start, fading out
                        s.SetPixel(x, y, Color.FromArgb(a, c.R, c.G, c.B));
                    }
                }
            }
            finally { s.Unlock(); }
            return Finish(s);
        }

        /// <summary>A star with exactly the chosen number of points, using the same vertex maths
        /// StarTool draws with.</summary>
        public static ImageSource StarPointsPreview(int points)
        {
            const int w = 22, h = 22;
            var s = Blank(w, h);
            var c = GlyphColor();
            points = Math.Max(3, Math.Min(24, points));
            double cx = w / 2.0, cy = h / 2.0, rx = w / 2.0 - 1.5, ry = h / 2.0 - 1.5;

            var pts = new List<Point>();
            for (int i = 0; i < points * 2; i++)
            {
                double frac = i / (double)(points * 2);
                double ang = -Math.PI / 2 + frac * Math.PI * 2;
                double f = (i % 2 == 0) ? 1.0 : 0.45; // outer / inner vertices alternate
                pts.Add(new Point(cx + Math.Cos(ang) * rx * f, cy + Math.Sin(ang) * ry * f));
            }

            s.Lock();
            try
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    s.DrawLine((int)Math.Round(a.X), (int)Math.Round(a.Y),
                               (int)Math.Round(b.X), (int)Math.Round(b.Y), c, 1);
                }
            }
            finally { s.Unlock(); }
            return Finish(s);
        }

        /// <summary>A short line carrying the chosen head at its right-hand end, drawn with
        /// ArrowTool's own code so the dropdown shows the head you'll actually get. An earlier
        /// version drew a barbed head for every entry, which meant diamond, dot and bar all
        /// previewed as something they aren't.</summary>
        public static ImageSource ArrowHeadPreview(ArrowHead head)
            => ArrowPreview(ArrowHead.None, head, width: HeadPreviewWidth);

        /// <summary>The same, mirrored: the head sits at the left-hand end, matching where a start
        /// head lands on a left-to-right arrow. Aggregation and composition are only distinguishable
        /// from a plain line by which *end* their diamond is on, so the start dropdown has to show
        /// that rather than reusing the end-head picture.</summary>
        public static ImageSource ArrowStartHeadPreview(ArrowHead head)
            => ArrowPreview(head, ArrowHead.None, width: HeadPreviewWidth);

        /// <summary>A whole named relationship - both heads and the line style together. UML
        /// realization is a hollow triangle on a *dashed* shaft; showing the head alone would make
        /// it indistinguishable from generalization in the list.</summary>
        public static ImageSource ArrowPresetPreview(ArrowPreset preset)
            => ArrowPreview(preset.Start, preset.End, preset.Line);

        /// <summary>Narrower than a preset's, because a head dropdown has to leave room for names
        /// as long as "Crow's foot + circle" before the row starts truncating.</summary>
        private const int HeadPreviewWidth = 46;

        private static ImageSource ArrowPreview(ArrowHead start, ArrowHead end,
                                                LineStyle line = LineStyle.Solid, int width = 62)
        {
            // Wide enough that the composite ER heads - a crow's foot with a circle behind it
            // reaches back about two head-lengths - still leave a recognisable stretch of shaft.
            int w = width; const int h = 20;
            var s = Blank(w, h);
            var c = GlyphColor();
            s.Lock();
            try
            {
                int y = h / 2;
                s.DashPattern = LineStyles.PatternFor(line, 1);
                s.DashPhase = 0;
                ArrowTool.DrawArrow(s, start, end, new Point(4, y), new Point(w - 5, y), 8, 1, c);
            }
            finally { s.DashPattern = null; s.Unlock(); }
            return Finish(s);
        }

        /// <summary>Contiguous vs global: one connected blob, or scattered matching patches.</summary>
        public static ImageSource AreaPreview(bool contiguous)
        {
            const int w = 26, h = 18;
            var s = Blank(w, h);
            var c = GlyphColor();
            var faint = Color.FromArgb(110, c.R, c.G, c.B);
            s.Lock();
            try
            {
                if (contiguous)
                {
                    s.DrawRect(new Int32Rect(2, 2, 12, 13), c, 1, true, faint);
                    s.DrawRect(new Int32Rect(16, 2, 7, 13), c, 1, false, Colors.Transparent);
                }
                else
                {
                    s.DrawRect(new Int32Rect(2, 2, 8, 6), c, 1, true, faint);
                    s.DrawRect(new Int32Rect(15, 3, 8, 6), c, 1, true, faint);
                    s.DrawRect(new Int32Rect(6, 10, 8, 6), c, 1, true, faint);
                }
            }
            finally { s.Unlock(); }
            return Finish(s);
        }
    }
}
