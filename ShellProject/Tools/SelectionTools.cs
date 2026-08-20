using System;
using System.Collections.Generic;
using System.Windows;
using PaintClone.Controls;

namespace PaintClone.Tools
{
    public class SelectRectTool : ITool
    {
        public string Name => "Select";
        public string StatusHint => "Click and drag to select a rectangular area; drag a corner handle to resize it.";

        private enum Mode { None, Creating, Moving }
        private Mode _mode = Mode.None;
        private Point _dragAnchor;
        private Int32Rect _origBounds;
        private Point _createStart;
        private Int32Rect _lastMarquee;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (ctx.Selection.HasSelection && ctx.Selection.Contains(e.DocPointInt))
            {
                _mode = Mode.Moving;
                _dragAnchor = e.DocPointInt;
                _origBounds = ctx.Selection.Bounds.Value;
                if (!ctx.Selection.IsFloating)
                {
                    ctx.History.PushUndoState(ctx.Document, "Move Selection");
                    ctx.Selection.Lift(ctx.Document.Surface, ctx.Colors.Background);
                }
            }
            else
            {
                // Finalize whatever was selected before starting a brand-new marquee.
                ctx.Selection.Deselect(ctx.Document.Surface);
                ctx.Canvas.ShowSelection(null);
                _mode = Mode.Creating;
                _createStart = e.DocPointInt;
                _lastMarquee = Normalize(_createStart, _createStart);
            }
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (_mode == Mode.Creating)
            {
                _lastMarquee = Normalize(_createStart, e.DocPointInt);
                ctx.Canvas.ShowSelection(_lastMarquee);
            }
            else if (_mode == Mode.Moving)
            {
                int dx = (int)(e.DocPointInt.X - _dragAnchor.X);
                int dy = (int)(e.DocPointInt.Y - _dragAnchor.Y);
                ctx.Selection.MoveTo(_origBounds.X + dx, _origBounds.Y + dy);
                ctx.Canvas.ClearPreview();
                RenderFloating(ctx);
                ctx.Canvas.ShowSelection(ctx.Selection.Bounds);
            }
        }

        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (_mode == Mode.Creating)
            {
                // Reuse the last rect the marquee actually showed rather than recomputing from this
                // event's own coordinates, which can genuinely differ from the last MouseMove's
                // position - otherwise the finalized selection could end up a pixel or two off from
                // what was just visibly outlined.
                var r = _lastMarquee;
                if (r.Width > 0 && r.Height > 0)
                {
                    ctx.Selection.BeginSelection(r);
                    ctx.Canvas.ShowSelection(r);
                }
            }
            _mode = Mode.None;
        }

        public void Cancel(ToolContext ctx)
        {
            _mode = Mode.None;
            ctx.Selection.Deselect(ctx.Document.Surface);
            ctx.Canvas.ShowSelection(null);
            ctx.Canvas.ClearPreview();
        }

        internal static void RenderFloating(ToolContext ctx)
        {
            if (!ctx.Selection.IsFloating || !ctx.Selection.Bounds.HasValue) return;
            var b = ctx.Selection.Bounds.Value;
            // Matches SelectionManager.Commit: generated content keeps every pixel it drew.
            System.Windows.Media.Color? transparentColor =
                (ctx.Selection.DrawOpaque || ctx.Selection.ContentIsGenerated) ? null : ctx.Colors.Background;
            ctx.Canvas.PreviewSurface.Lock();
            ctx.Canvas.PreviewSurface.Blit(ctx.Selection.Floating, b.X, b.Y, transparentColor);
            ctx.Canvas.PreviewSurface.Unlock();
        }

        private static Int32Rect Normalize(Point a, Point b)
        {
            int x = (int)Math.Min(a.X, b.X), y = (int)Math.Min(a.Y, b.Y);
            int w = (int)Math.Abs(b.X - a.X), h = (int)Math.Abs(b.Y - a.Y);
            return new Int32Rect(x, y, w, h);
        }
    }

    /// <summary>Lasso selection: traces an arbitrary polygon and builds a boolean mask via scanline
    /// point-in-polygon fill (spec section 12).</summary>
    public class FreeFormSelectTool : ITool
    {
        public string Name => "Free-Form Select";
        public string StatusHint => "Drag around an area you want to select; drag a corner handle to resize it.";

        private readonly List<Point> _trace = new();
        private bool _tracing;
        private bool _moving;
        private Point _dragAnchor;
        private Int32Rect _origBounds;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (ctx.Selection.HasSelection && ctx.Selection.Contains(e.DocPointInt))
            {
                _moving = true;
                _dragAnchor = e.DocPointInt;
                _origBounds = ctx.Selection.Bounds.Value;
                if (!ctx.Selection.IsFloating)
                {
                    ctx.History.PushUndoState(ctx.Document, "Move Selection");
                    ctx.Selection.Lift(ctx.Document.Surface, ctx.Colors.Background);
                }
                return;
            }
            ctx.Selection.Deselect(ctx.Document.Surface);
            ctx.Canvas.ShowSelection(null);
            _tracing = true;
            _trace.Clear();
            _trace.Add(e.DocPointInt);
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (_tracing)
            {
                _trace.Add(e.DocPointInt);
                ctx.Canvas.ClearPreview();
                ctx.Canvas.PreviewSurface.Lock();
                for (int i = 0; i < _trace.Count - 1; i++)
                {
                    var a = _trace[i]; var b = _trace[i + 1];
                    ctx.Canvas.PreviewSurface.DrawLine((int)a.X, (int)a.Y, (int)b.X, (int)b.Y,
                        System.Windows.Media.Colors.Black, 1);
                }
                ctx.Canvas.PreviewSurface.Unlock();
            }
            else if (_moving)
            {
                int dx = (int)(e.DocPointInt.X - _dragAnchor.X);
                int dy = (int)(e.DocPointInt.Y - _dragAnchor.Y);
                ctx.Selection.MoveTo(_origBounds.X + dx, _origBounds.Y + dy);
                ctx.Canvas.ClearPreview();
                SelectRectTool.RenderFloating(ctx);
                ctx.Canvas.ShowSelection(ctx.Selection.Bounds);
            }
        }

        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (_tracing)
            {
                _tracing = false;
                ctx.Canvas.ClearPreview();
                if (_trace.Count < 3) { _trace.Clear(); return; }

                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var p in _trace)
                {
                    minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
                }
                var bounds = new Int32Rect((int)minX, (int)minY, Math.Max(1, (int)(maxX - minX)), Math.Max(1, (int)(maxY - minY)));
                var mask = BuildMask(_trace, bounds);
                ctx.Selection.BeginSelection(bounds, mask);
                ctx.Canvas.ShowSelection(bounds);
                _trace.Clear();
            }
            _moving = false;
        }

        private static bool[,] BuildMask(List<Point> pts, Int32Rect bounds)
        {
            var mask = new bool[bounds.Width, bounds.Height];
            for (int y = 0; y < bounds.Height; y++)
            {
                int docY = bounds.Y + y;
                var xs = new List<double>();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    if ((a.Y <= docY && b.Y > docY) || (b.Y <= docY && a.Y > docY))
                    {
                        double t = (docY - a.Y) / (b.Y - a.Y);
                        xs.Add(a.X + t * (b.X - a.X));
                    }
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    int xStart = Math.Max(bounds.X, (int)xs[i]);
                    int xEnd = Math.Min(bounds.X + bounds.Width - 1, (int)xs[i + 1]);
                    for (int x = xStart; x <= xEnd; x++) mask[x - bounds.X, y] = true;
                }
            }
            return mask;
        }

        public void Cancel(ToolContext ctx)
        {
            _tracing = false;
            _moving = false;
            _trace.Clear();
            ctx.Selection.Deselect(ctx.Document.Surface);
            ctx.Canvas.ShowSelection(null);
            ctx.Canvas.ClearPreview();
        }
    }

    /// <summary>Magic Wand: click a pixel to select every contiguous pixel of the same exact
    /// color (reuses the same scanline algorithm as Fill With Color, via
    /// RasterSurface.MagicWandSelect, but builds a selection mask instead of writing color).
    /// Supports the same drag-to-move behavior as the other selection tools.</summary>
    public class MagicWandSelectTool : ITool
    {
        public string Name => "Magic Wand";
        public string StatusHint => "Click to select by color - set tolerance and contiguous/global in the tool options.";

        private bool _moving;
        private Point _dragAnchor;
        private Int32Rect _origBounds;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (ctx.Selection.HasSelection && ctx.Selection.Contains(e.DocPointInt))
            {
                _moving = true;
                _dragAnchor = e.DocPointInt;
                _origBounds = ctx.Selection.Bounds.Value;
                if (!ctx.Selection.IsFloating)
                {
                    ctx.History.PushUndoState(ctx.Document, "Move Selection");
                    ctx.Selection.Lift(ctx.Document.Surface, ctx.Colors.Background);
                }
                return;
            }

            ctx.Selection.Deselect(ctx.Document.Surface);
            ctx.Canvas.ShowSelection(null);

            var (bounds, mask) = ctx.Document.Surface.MagicWandSelect(
                (int)e.DocPointInt.X, (int)e.DocPointInt.Y, ctx.WandTolerance, ctx.WandContiguous);
            ctx.Selection.BeginSelection(bounds, mask);
            ctx.Canvas.ShowSelection(bounds);
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_moving) return;
            int dx = (int)(e.DocPointInt.X - _dragAnchor.X);
            int dy = (int)(e.DocPointInt.Y - _dragAnchor.Y);
            ctx.Selection.MoveTo(_origBounds.X + dx, _origBounds.Y + dy);
            ctx.Canvas.ClearPreview();
            SelectRectTool.RenderFloating(ctx);
            ctx.Canvas.ShowSelection(ctx.Selection.Bounds);
        }

        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            _moving = false;
        }

        public void Cancel(ToolContext ctx)
        {
            _moving = false;
            ctx.Selection.Deselect(ctx.Document.Surface);
            ctx.Canvas.ShowSelection(null);
            ctx.Canvas.ClearPreview();
        }
    }
}
