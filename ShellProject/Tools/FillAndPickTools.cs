using System;
using System.Windows;
using System.Windows.Input;
using PaintClone.Controls;

namespace PaintClone.Tools
{
    /// <summary>4-connected flood fill (spec section 16), with the two options a paint bucket is
    /// normally expected to have: a tolerance, so near-but-not-identical shades can be swallowed by
    /// one fill, and a contiguous/global switch, so a colour can be replaced everywhere at once
    /// rather than only in the region that was clicked.</summary>
    public class FillTool : ITool
    {
        public string Name => "Fill With Color";
        public string StatusHint => "Click inside an area to fill it. Tolerance and Area are in the tool options.";

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            ctx.History.PushUndoState(ctx.Document, "Fill With Color");
            var c = e.Button == MouseButton.Right ? ctx.Colors.Background : ctx.Colors.Foreground;
            ctx.Document.Surface.FloodFill((int)e.DocPointInt.X, (int)e.DocPointInt.Y, c,
                                           ctx.FillTolerance, ctx.FillContiguous);
            ctx.Document.MarkDirty();
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e) { }
        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e) { }
        public void Cancel(ToolContext ctx) { }
    }

    /// <summary>Eyedropper - samples the EXACT pixel color, no averaging (spec section 17).
    /// After sampling, classic Paint returns to the previously active tool; MainWindow handles that switch.</summary>
    public class ColorPickerTool : ITool
    {
        public string Name => "Pick Color";
        public string StatusHint => "Click a color on the drawing to use it for future drawing.";

        public System.Action<System.Windows.Media.Color, MouseButton> OnSampled;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            var c = ctx.Document.Surface.GetPixel((int)e.DocPointInt.X, (int)e.DocPointInt.Y);
            if (e.Button == MouseButton.Right) ctx.Colors.SetBackground(c);
            else ctx.Colors.SetForeground(c);
            OnSampled?.Invoke(c, e.Button);
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e) { }
        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e) { }
        public void Cancel(ToolContext ctx) { }
    }

    /// <summary>Simplified magnifier: click cycles to the next classic zoom level, centered where possible.
    /// (Zoom affects display scale only, never the underlying bitmap - spec section 18.)</summary>
    public class MagnifierTool : ITool
    {
        public string Name => "Magnifier";
        public string StatusHint => "Click to zoom in and out, or drag a box to zoom to that area.";
        /// <summary>Every whole zoom from 1x to 10x. Was a sparse 1/2/4/6/8 set chosen to fit a row
        /// of buttons; now that zoom is picked from a dropdown there's no reason to skip steps.</summary>
        public static readonly double[] Levels = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        public Action<MouseButton> OnClick;
        public Action<Int32Rect> OnAreaSelected;

        private Point _start;
        private bool _dragging;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            _start = e.DocPointInt;
            _dragging = true;
            ctx.Canvas.ShowSelection(null);
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_dragging) return;
            ctx.Canvas.ShowSelection(NormalizedRect(_start, e.DocPointInt));
        }

        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            var r = NormalizedRect(_start, e.DocPointInt);
            ctx.Canvas.ShowSelection(ctx.Selection.Bounds); // restore whatever was showing before this drag's rubber-band preview

            // A tiny drag is really just a click that wobbled a pixel or two - fall back to the
            // classic cycle-zoom behavior rather than "zooming to" a near-empty rectangle.
            if (r.Width <= 3 && r.Height <= 3)
                OnClick?.Invoke(e.Button);
            else
                OnAreaSelected?.Invoke(r);
        }

        public void Cancel(ToolContext ctx) => _dragging = false;

        private static Int32Rect NormalizedRect(Point a, Point b)
        {
            int x = (int)Math.Min(a.X, b.X);
            int y = (int)Math.Min(a.Y, b.Y);
            int w = (int)Math.Abs(b.X - a.X);
            int h = (int)Math.Abs(b.Y - a.Y);
            return new Int32Rect(x, y, Math.Max(1, w), Math.Max(1, h));
        }
    }
}
