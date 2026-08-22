using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaintClone.Models;

namespace PaintClone.Services
{
    /// <summary>
    /// Renders a TextLayerData's current content into its layer's (already document-sized,
    /// initially-transparent) RasterSurface. This is the one place that turns "live text
    /// properties" into pixels, and it's called on every edit, move, resize, and document resize -
    /// never once-and-done - which is what keeps a text layer re-editable indefinitely instead of
    /// being a one-time commit.
    ///
    /// Deliberately mirrors the rasterization technique the Text tool used before text layers
    /// existed (DrawingVisual -> RenderTargetBitmap -> blit, with an alpha threshold for aliased
    /// text) rather than a different implementation, so a re-rendered text layer looks pixel-for-
    /// pixel identical to what the original one-shot commit used to produce.
    /// </summary>
    public static class TextLayerRenderer
    {
        public static void Render(RasterSurface layerSurface, TextLayerData text)
        {
            layerSurface.Clear(Colors.Transparent);
            if (string.IsNullOrEmpty(text.Content)) return;

            var typeface = new Typeface(new FontFamily(text.FontFamily),
                text.Italic ? FontStyles.Italic : FontStyles.Normal,
                text.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);

            var formatted = new FormattedText(
                text.Content, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, text.FontSize, new SolidColorBrush(text.Color), 96.0)
            {
                // Explicitly untrimmed: left to the default, this could come out as a single line
                // ending in "..." instead of the multiple wrapped lines that were visible while
                // editing.
                Trimming = TextTrimming.None
            };
            // Point text (AutoWidth) never wraps - leaving MaxTextWidth at its default (0, meaning
            // unconstrained) is what makes each line render at its own natural width, matching the
            // live NoWrap TextBox it was typed into. Paragraph text wraps within its stored Width.
            if (!text.AutoWidth) formatted.MaxTextWidth = Math.Max(1, text.Width);
            if (text.Underline) formatted.SetTextDecorations(TextDecorations.Underline);

            var visual = new DrawingVisual();
            // Aliased text keeps every pixel fully opaque or fully clear, which is what lets the
            // hard alpha-threshold below produce a clean result; anti-aliased text has soft edge
            // pixels that are the point, so they're kept as-is and blended in at blit time instead.
            TextOptions.SetTextRenderingMode(visual, text.AntiAlias ? TextRenderingMode.ClearType : TextRenderingMode.Aliased);
            TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
            using (var dc = visual.RenderOpen())
                dc.DrawText(formatted, new Point(0, 0));

            int w = Math.Max(1, (int)text.Width);
            int h = Math.Max(1, (int)Math.Max(text.Height, formatted.Height));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var wb = new WriteableBitmap(rtb);
            if (!text.AntiAlias) new RasterSurface(wb).ThresholdAlpha();

            layerSurface.Blit(wb, (int)Math.Round(text.X), (int)Math.Round(text.Y), transparentColor: Colors.Transparent);
        }
    }
}
