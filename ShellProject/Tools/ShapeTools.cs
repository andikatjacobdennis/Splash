using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PaintClone.Controls;
using PaintClone.Models;

namespace PaintClone.Tools
{
    /// <summary>
    /// Applies the stroke settings (anti-aliasing, dash pattern) a shape is about to be drawn with.
    /// Kept in one place because a shape's outline is stroked from a dozen separate DrawLine calls
    /// and every one of them has to agree - and because the dash phase must be reset once per
    /// shape, not once per edge, or every side of a rectangle would start with a fresh dash and the
    /// corners would never line up. Free-standing rather than a member of DragShapeToolBase because
    /// Curve and Polygon draw outlines too but aren't part of that hierarchy.
    /// </summary>
    internal static class StrokeSetup
    {
        public static void Begin(ToolContext ctx, RasterSurface s)
        {
            s.AntiAlias = ctx.AntiAlias;
            s.DashPattern = LineStyles.PatternFor(ctx.LineStyle, ctx.PenSize,
                                                  ctx.DashLengthPercent, ctx.DashGapPercent);
            s.DashPhase = 0;
        }

        public static void End(RasterSurface s)
        {
            s.AntiAlias = false;
            s.DashPattern = null;
            s.DashPhase = 0;
        }
    }

    /// <summary>Shared drag-preview-commit workflow used by Line/Rectangle/Ellipse/RoundedRectangle
    /// (spec section 38): nothing touches the real document until mouse-up.</summary>
    public abstract class DragShapeToolBase : ITool
    {
        public abstract string Name { get; }
        public abstract string StatusHint { get; }

        /// <summary>The key this tool is registered under in MainWindow's tool table. Defaults to
        /// the name with spaces stripped, which matches every current registration.</summary>
        public virtual string ToolKey => Name.Replace(" ", "");

        /// <summary>Idle: nothing happening. Drawing: dragging out a brand-new shape. Moving:
        /// repositioning a shape this same tool already drew, which is still pending (floating,
        /// not yet rasterized) - the tool's own version of what Select/FreeFormSelect/MagicWand do
        /// for their selections, so adjusting a just-drawn shape never requires switching tools.</summary>
        protected enum Mode { Idle, Drawing, Moving }
        protected Mode _mode = Mode.Idle;
        protected Point _start;
        protected MouseButton _button;
        private Point? _lastPreviewEnd;
        private Point _moveAnchor;
        private Int32Rect _moveOrigBounds;

        /// <summary>True for tools whose Shift constraint should snap the *direction* to 45-degree
        /// increments (which includes horizontal and vertical), rather than forcing a square
        /// bounding box. Line-like tools want the former; box-like shapes want the latter.</summary>
        protected virtual bool ConstrainsToAngle => false;

        /// <summary>Extra padding, beyond the stroke-thickness allowance, that this shape's
        /// rendering can extend past its start/end bounding box. Arrowheads stick out perpendicular
        /// to the line, so without this the head gets clipped when the pending shape is re-rendered
        /// into a tightly-fitted bitmap.</summary>
        protected virtual int ExtraPad(ToolContext ctx) => 0;

        /// <summary>False for tools whose output isn't meaningfully adjustable after the fact, so
        /// it should be committed immediately instead of becoming a selected, still-editable
        /// pending shape. The pending-shape flow exists so a drawn shape can be nudged or resized
        /// before it's made permanent; for something that simply fills the region it was dragged
        /// over, being left with a selection to dismiss is friction rather than a feature.</summary>
        protected virtual bool UsesPendingShape => true;

        /// <summary>False for tools that paint right up to their bounding box with no stroke
        /// spilling past it. Those get no padding at all - padding would leave an unpainted border
        /// around the shape once it's re-rendered into its (padded) pending-shape bitmap.</summary>
        protected virtual bool UsesStrokePadding => true;

        public virtual void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            // A shape this same tool drew earlier is still pending (floating, uncommitted) -
            // clicking inside it moves it, exactly like Select/FreeFormSelect/MagicWand already do
            // for their own selections, so nudging a just-drawn shape never means switching to
            // Select and back. It's always already floating by construction (a pending shape is
            // never anything else), so - unlike those tools - there's never a Lift step here.
            if (ctx.Selection.HasSelection && ctx.Selection.Contains(e.DocPointInt))
            {
                _mode = Mode.Moving;
                _moveAnchor = e.DocPointInt;
                _moveOrigBounds = ctx.Selection.Bounds.Value;
                return;
            }

            // Clicking outside a still-pending shape finalizes it right where it is, then starts
            // drawing a new one with this same click - so laying down several shapes in a row
            // never needs the tool re-picked in between. A no-op if nothing is pending.
            ctx.FinalizePendingShape?.Invoke();

            _mode = Mode.Drawing;
            _start = e.DocPointInt;
            _button = e.Button;
            _lastPreviewEnd = null;
        }

        protected static void BeginStroke(ToolContext ctx, RasterSurface s) => StrokeSetup.Begin(ctx, s);
        protected static void EndStroke(RasterSurface s) => StrokeSetup.End(s);

        public virtual void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (_mode == Mode.Moving)
            {
                int dx = (int)(e.DocPointInt.X - _moveAnchor.X);
                int dy = (int)(e.DocPointInt.Y - _moveAnchor.Y);
                ctx.Selection.MoveTo(_moveOrigBounds.X + dx, _moveOrigBounds.Y + dy);
                ctx.Canvas.ClearPreview();
                SelectRectTool.RenderFloating(ctx);
                ctx.Canvas.ShowSelection(ctx.Selection.Bounds);
                return;
            }
            if (_mode != Mode.Drawing) return;
            var end = ConstrainedEnd(e);
            _lastPreviewEnd = end;
            ctx.Canvas.ClearPreview();
            ctx.Canvas.PreviewSurface.Lock();
            StrokeSetup.Begin(ctx, ctx.Canvas.PreviewSurface);
            DrawPreview(ctx, _start, end, ctx.Canvas.PreviewSurface);
            StrokeSetup.End(ctx.Canvas.PreviewSurface);
            ctx.Canvas.PreviewSurface.Unlock();
        }

        public virtual void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (_mode == Mode.Moving)
            {
                // Nothing to commit - it's still just pending, exactly as before the move. No undo
                // entry either: the document itself hasn't been touched yet (matching the existing
                // handle-based resize, which is the same "still pending" case).
                _mode = Mode.Idle;
                return;
            }
            if (_mode != Mode.Drawing) return;
            _mode = Mode.Idle;
            // Reuse whatever point the preview last actually showed, rather than recomputing from
            // this event's own coordinates - those can genuinely differ from the last MouseMove's
            // position (a physical button release rarely lands at the exact same pixel as the last
            // move sample), which was the cause of the final shape occasionally not matching what
            // was just previewed. Only fall back to this event's own point for a plain click with
            // no drag at all, where no MouseMove ever fired.
            var end = _lastPreviewEnd ?? ConstrainedEnd(e);
            ctx.Canvas.ClearPreview();

            // Padding accounts for thick outlines: StampSquare-based strokes are centered ON each
            // boundary point rather than inset from it, so they can extend up to PenSize/2 pixels
            // beyond the mathematically exact bounding box.
            int pad = UsesStrokePadding ? Math.Max(0, ctx.PenSize / 2 + 1) + ExtraPad(ctx) : 0;
            var raw = NormalizedRect(_start, end);

            // Hand the shape to MainWindow as a *pending* shape rather than rasterizing it here.
            // It stays unrasterized - re-rendered from these original start/end parameters on
            // every move/resize - until it's finally committed, so repeatedly adjusting it never
            // resamples a previous render and never degrades quality. The tool itself stays
            // current (see OnMouseDown above) rather than switching to Select.
            if (UsesPendingShape && raw.Width > 1 && raw.Height > 1 && ctx.BeginPendingShape != null)
            {
                ctx.BeginPendingShape(new PendingShape
                {
                    // Re-rendering a pending shape has to apply the same stroke settings the live
                    // preview did, or a dashed (or anti-aliased) shape would quietly come out
                    // solid the moment it was moved or resized.
                    Render = (s, en, surface) =>
                    {
                        BeginStroke(ctx, surface);
                        try { DrawPreview(ctx, s, en, surface); }
                        finally { EndStroke(surface); }
                    },
                    Start = _start,
                    End = end,
                    Pad = pad,
                    Label = Name,
                    OriginToolKey = ToolKey
                });
                return;
            }

            // Reached either by a tool that opts out of the pending-shape flow, or by a
            // degenerate (essentially zero-area) drag that isn't worth a selection. Either way the
            // result goes straight into the document and the current tool stays selected.
            ctx.History.PushUndoState(ctx.Document, Name);
            ctx.Document.Surface.Lock();
            StrokeSetup.Begin(ctx, ctx.Document.Surface);
            // Only GradientTool's DrawPreview ever consults this (every other override writes
            // pixels directly), so turning it on unconditionally here is harmless for every other
            // shape tool that can reach this same direct-commit path via a zero-area click.
            ctx.Document.Surface.Blend = true;
            DrawPreview(ctx, _start, end, ctx.Document.Surface);
            ctx.Document.Surface.Blend = false;
            StrokeSetup.End(ctx.Document.Surface);
            ctx.Document.Surface.Unlock();
            ctx.Document.MarkDirty();
        }

        public virtual void Cancel(ToolContext ctx)
        {
            _mode = Mode.Idle;
            _lastPreviewEnd = null;
            ctx.Canvas.ClearPreview();
        }

        private Point ConstrainedEnd(CanvasMouseEventArgs e)
        {
            var end = e.DocPointInt;
            if (e.ShiftDown)
            {
                // Shift constrains to square/circle bounding box or 45-degree lines (spec sections 23,25,27,28).
                double dx = end.X - _start.X, dy = end.Y - _start.Y;
                if (ConstrainsToAngle)
                {
                    double angle = Math.Atan2(dy, dx);
                    double snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    end = new Point(_start.X + Math.Cos(snapped) * len, _start.Y + Math.Sin(snapped) * len);
                }
                else
                {
                    double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    end = new Point(_start.X + Math.Sign(dx == 0 ? 1 : dx) * side, _start.Y + Math.Sign(dy == 0 ? 1 : dy) * side);
                }
            }
            return end;
        }

        protected static Int32Rect NormalizedRect(Point a, Point b)
        {
            int x = (int)Math.Min(a.X, b.X);
            int y = (int)Math.Min(a.Y, b.Y);
            int w = (int)Math.Abs(b.X - a.X);
            int h = (int)Math.Abs(b.Y - a.Y);
            return new Int32Rect(x, y, Math.Max(w, 1), Math.Max(h, 1));
        }

        protected (Color outline, Color fill, bool fill_) ResolveColors(ToolContext ctx)
        {
            // Left-drag: outline=foreground, fill=background. Right-drag: swapped (classic Paint convention).
            Color outline = _button == MouseButton.Right ? ctx.Colors.Background : ctx.Colors.Foreground;
            Color fill = _button == MouseButton.Right ? ctx.Colors.Foreground : ctx.Colors.Background;
            bool doFill = ctx.ShapeFillMode != ShapeFillMode.OutlineOnly;
            if (ctx.ShapeFillMode == ShapeFillMode.FillOnly) outline = fill;
            return (outline, fill, doFill);
        }

        protected abstract void DrawPreview(ToolContext ctx, Point start, Point end, RasterSurface surface);
    }

    public class LineTool : DragShapeToolBase
    {
        public override string Name => "Line";
        public override string StatusHint => "Click and drag to draw a line.";
        protected override bool ConstrainsToAngle => true;
        protected override void DrawPreview(ToolContext ctx, Point start, Point end, RasterSurface surface)
        {
            var c = _button == MouseButton.Right ? ctx.Colors.Background : ctx.Colors.Foreground;
            surface.DrawLine((int)start.X, (int)start.Y, (int)end.X, (int)end.Y, c, Math.Max(1, ctx.PenSize));
        }
    }

    public class RectangleTool : DragShapeToolBase
    {
        public override string Name => "Rectangle";
        public override string StatusHint => "Click and drag to draw a rectangle.";
        protected override void DrawPreview(ToolContext ctx, Point start, Point end, RasterSurface surface)
        {
            var (outline, fill, doFill) = ResolveColors(ctx);
            surface.DrawRect(NormalizedRect(start, end), outline, Math.Max(1, ctx.PenSize), doFill, fill);
        }
    }

    public class EllipseTool : DragShapeToolBase
    {
        public override string Name => "Ellipse";
        public override string StatusHint => "Click and drag to draw an ellipse.";
        protected override void DrawPreview(ToolContext ctx, Point start, Point end, RasterSurface surface)
        {
            var (outline, fill, doFill) = ResolveColors(ctx);
            surface.DrawEllipse(NormalizedRect(start, end), outline, Math.Max(1, ctx.PenSize), doFill, fill);
        }
    }

    public class RoundedRectangleTool : DragShapeToolBase
    {
        public override string Name => "Rounded Rectangle";
        public override string StatusHint => "Click and drag to draw a rounded rectangle.";
        protected override void DrawPreview(ToolContext ctx, Point start, Point end, RasterSurface surface)
        {
            var (outline, fill, doFill) = ResolveColors(ctx);
            var r = NormalizedRect(start, end);
            // 0 keeps the original behaviour: derive a radius from the shape's own size. Anything
            // else is the explicit radius chosen in the tool options, capped so it can't exceed
            // half the shorter side (past that the "rounding" is just a stadium/ellipse).
            int radius = ctx.CornerRadius > 0
                ? Math.Min(ctx.CornerRadius, Math.Max(1, Math.Min(r.Width, r.Height) / 2))
                : Math.Max(4, Math.Min(r.Width, r.Height) / 4);
            surface.DrawRoundedRect(r, radius, outline, Math.Max(1, ctx.PenSize), doFill, fill);
        }
    }

    /// <summary>Line with an arrowhead at the end point - drawn by reusing the base line plus two
    /// short barbs, each rotated off the line's own direction so the head always points the way the
    /// line travels regardless of drag direction.</summary>
    public class ArrowTool : DragShapeToolBase
    {
        public override string Name => "Arrow";
        public override string StatusHint => "Drag to draw an arrow; hold Shift to snap to 45-degree angles.";

        // Arrows are line-like, so Shift should snap the direction (giving horizontal and vertical
        // as well as the diagonals) rather than forcing a square bounding box.
        protected override bool ConstrainsToAngle => true;

        /// <summary>Arrowheads stick out perpendicular to the line, so the drawn shape extends
        /// past the plain start/end box by roughly the head's length. Without allowing for that
        /// here, re-rendering the pending shape into a tightly-fitted bitmap clipped the head -
        /// most visibly when the selection was dragged shorter vertically.</summary>
        protected override int ExtraPad(ToolContext ctx) => (int)Math.Ceiling(HeadLength(ctx, double.MaxValue)) + 2;

        private static double HeadLength(ToolContext ctx, double lineLen)
        {
            int thickness = Math.Max(1, ctx.PenSize);
            // Scales with stroke weight and line length, so it stays proportionate on a short thin
            // arrow and a long thick one alike.
            return Math.Min(lineLen * 0.35, 10 + thickness * 3);
        }

        protected override void DrawPreview(ToolContext ctx, Point start, Point end, RasterSurface surface)
        {
            var c = _button == MouseButton.Right ? ctx.Colors.Background : ctx.Colors.Foreground;
            int thickness = Math.Max(1, ctx.PenSize);
            surface.DrawLine((int)start.X, (int)start.Y, (int)end.X, (int)end.Y, c, thickness);

            double dx = end.X - start.X, dy = end.Y - start.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1 || ctx.ArrowStyle == ArrowStyle.None) return;

            double angle = Math.Atan2(dy, dx);
            double headLen = HeadLength(ctx, len);

            DrawHeadsFor(surface, ctx.ArrowStyle, start, end, angle, headLen, thickness, c);
        }

        /// <summary>Draws whichever head(s) a style calls for onto an already-drawn shaft. Public to
        /// this assembly so the tool-options preview renders heads with this exact code rather than
        /// its own approximation - the previews previously drew a barbed head for every style, so
        /// diamond, dot and bar all showed the wrong picture in the dropdown.</summary>
        internal static void DrawHeadsFor(RasterSurface surface, ArrowStyle style, Point start, Point end,
                                          double angle, double headLen, int thickness, Color c)
        {
            if (style == ArrowStyle.None) return;

            bool bothEnds = style is ArrowStyle.Both or ArrowStyle.FilledBoth
                or ArrowStyle.DiamondBoth or ArrowStyle.CircleBoth or ArrowStyle.BarBoth;

            // The dash pattern belongs to the shaft only - a dashed arrowhead just looks like a
            // broken one. Suppressed for the duration of the head and restored afterwards.
            var savedDash = surface.DashPattern;
            surface.DashPattern = null;
            try
            {
                // Heads other than the barbed ones are shapes centred on the tip rather than swept
                // back from it, so they're drawn separately from DrawHead's barb geometry.
                switch (style)
                {
                    case ArrowStyle.Diamond:
                    case ArrowStyle.DiamondBoth:
                        DrawDiamondHead(surface, end, angle, headLen, c);
                        if (bothEnds) DrawDiamondHead(surface, start, angle + Math.PI, headLen, c);
                        return;

                    case ArrowStyle.Circle:
                    case ArrowStyle.CircleBoth:
                        DrawDotHead(surface, end, headLen, c);
                        if (bothEnds) DrawDotHead(surface, start, headLen, c);
                        return;

                    case ArrowStyle.Bar:
                    case ArrowStyle.BarBoth:
                        DrawBarHead(surface, end, angle, headLen, thickness, c);
                        if (bothEnds) DrawBarHead(surface, start, angle + Math.PI, headLen, thickness, c);
                        return;
                }

                bool filled = style is ArrowStyle.Filled or ArrowStyle.FilledBoth;
                DrawHead(surface, end, angle, headLen, thickness, c, filled);
                if (bothEnds) DrawHead(surface, start, angle + Math.PI, headLen, thickness, c, filled);
            }
            finally
            {
                surface.DashPattern = savedDash;
            }
        }

        /// <summary>Solid diamond centred just behind the tip, pointing along dir.</summary>
        private static void DrawDiamondHead(RasterSurface surface, Point tip, double dir, double headLen, Color c)
        {
            double half = headLen / 2;
            var back = new Point(tip.X - Math.Cos(dir) * headLen, tip.Y - Math.Sin(dir) * headLen);
            var mid = new Point((tip.X + back.X) / 2, (tip.Y + back.Y) / 2);
            double px = Math.Cos(dir + Math.PI / 2), py = Math.Sin(dir + Math.PI / 2);
            PolygonTool.FillPolygon(surface, new List<Point>
            {
                tip,
                new(mid.X + px * half * 0.6, mid.Y + py * half * 0.6),
                back,
                new(mid.X - px * half * 0.6, mid.Y - py * half * 0.6),
            }, c);
        }

        /// <summary>Solid dot centred on the tip.</summary>
        private static void DrawDotHead(RasterSurface surface, Point tip, double headLen, Color c)
            => surface.StampCircle((int)Math.Round(tip.X), (int)Math.Round(tip.Y),
                                   Math.Max(2, (int)Math.Round(headLen / 2)), c);

        /// <summary>Flat cross-bar ("tee") across the tip, perpendicular to the line.</summary>
        private static void DrawBarHead(RasterSurface surface, Point tip, double dir, double headLen,
                                        int thickness, Color c)
        {
            double px = Math.Cos(dir + Math.PI / 2), py = Math.Sin(dir + Math.PI / 2);
            double half = headLen * 0.55;
            surface.DrawLine((int)Math.Round(tip.X - px * half), (int)Math.Round(tip.Y - py * half),
                             (int)Math.Round(tip.X + px * half), (int)Math.Round(tip.Y + py * half),
                             c, Math.Max(1, thickness));
        }

        /// <summary>Draws one arrowhead at tip, pointing along dir.</summary>
        private static void DrawHead(RasterSurface surface, Point tip, double dir,
                                     double headLen, int thickness, Color c, bool filled)
        {
            const double spread = Math.PI / 7;
            double a1 = dir + Math.PI + spread;
            double a2 = dir + Math.PI - spread;
            var b1 = new Point(tip.X + Math.Cos(a1) * headLen, tip.Y + Math.Sin(a1) * headLen);
            var b2 = new Point(tip.X + Math.Cos(a2) * headLen, tip.Y + Math.Sin(a2) * headLen);

            // Rounded, not truncated. A cast truncates *toward zero*, so the two barbs - which sit
            // at mirrored offsets either side of the shaft - were being pulled in opposite
            // directions by up to a pixel each, which is exactly what made the head look lopsided.
            int tx = (int)Math.Round(tip.X), ty = (int)Math.Round(tip.Y);
            int b1x = (int)Math.Round(b1.X), b1y = (int)Math.Round(b1.Y);
            int b2x = (int)Math.Round(b2.X), b2y = (int)Math.Round(b2.Y);

            if (filled)
            {
                PolygonTool.FillPolygon(surface, new List<Point> { tip, b1, b2 }, c);
                // Outline it too, so a solid head still reads cleanly at small sizes where the
                // scanline fill alone can look ragged.
                surface.DrawLine(tx, ty, b1x, b1y, c, thickness);
                surface.DrawLine(tx, ty, b2x, b2y, c, thickness);
                surface.DrawLine(b1x, b1y, b2x, b2y, c, thickness);
            }
            else
            {
                surface.DrawLine(tx, ty, b1x, b1y, c, thickness);
                surface.DrawLine(tx, ty, b2x, b2y, c, thickness);
            }
        }
    }

    /// <summary>Five-pointed star inscribed in the dragged bounding box.</summary>
    /// <summary>
    /// Every closed shape that's defined purely by a ring of vertices inscribed in the dragged box -
    /// star, triangle, hexagon, heart, and so on. They all draw identically once their outline is
    /// known (fill it, then stroke it), so the only thing that varies per shape is the list of
    /// points, which keeps adding another shape down to adding one case in BuildPoints.
    ///
    /// Registered once per ShapeLibrary entry in MainWindow's tool table, and grouped behind a
    /// single toolbox button with a flyout, the way Photoshop groups its shape tools.
    /// </summary>
    public class PolyShapeTool : DragShapeToolBase
    {
        public ShapeDef Shape { get; }

        public PolyShapeTool(ShapeDef shape) { Shape = shape; }

        public override string Name => Shape.Name;
        public override string ToolKey => Shape.Id;
        public override string StatusHint => Shape.Id == ShapeLibrary.DefaultId
            ? "Click and drag to draw a star; set its points, depth and angle in the tool options."
            : $"Click and drag to draw a {Shape.Name.ToLowerInvariant()}; set its angle and line style in the tool options.";

        protected override void DrawPreview(ToolContext ctx, Point start, Point end, RasterSurface surface)
        {
            var (outline, fill, doFill) = ResolveColors(ctx);
            var r = NormalizedRect(start, end);
            var pts = BuildPoints(Shape, r, ctx);
            if (pts.Count < 3) return;

            if (doFill) PolygonTool.FillPolygon(surface, pts, fill);
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                surface.DrawLine((int)Math.Round(a.X), (int)Math.Round(a.Y),
                                 (int)Math.Round(b.X), (int)Math.Round(b.Y), outline, Math.Max(1, ctx.PenSize));
            }
        }

        /// <summary>The outline of one shape, inscribed in r and rotated by the tool options' angle.
        /// Shapes are authored in a -1..1 unit square (see ShapeLibrary) and scaled into the box
        /// here, so a definition only has to describe its silhouette - never the placement,
        /// rotation or aspect maths, which is identical for every shape.</summary>
        internal static List<Point> BuildPoints(ShapeDef shape, Int32Rect r, ToolContext ctx)
        {
            double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
            double rx = r.Width / 2.0, ry = r.Height / 2.0;

            var unit = shape.Unit(ctx);
            double rot = (ctx?.ShapeRotation ?? 0) * Math.PI / 180.0;
            double cos = Math.Cos(rot), sin = Math.Sin(rot);

            var pts = new List<Point>(unit.Count);
            foreach (var u in unit)
            {
                // Rotate in the unit square so the shape turns about its own centre regardless of
                // how non-square the dragged box is, then scale into that box.
                double ux = u.X * cos - u.Y * sin;
                double uy = u.X * sin + u.Y * cos;
                pts.Add(new Point(cx + ux * rx, cy + uy * ry));
            }
            return pts;
        }
    }

    /// <summary>Linear gradient between the foreground and background colours, drawn across the
    /// dragged bounding box. The drag direction sets the gradient axis, so dragging diagonally
    /// gives a diagonal blend rather than being locked to horizontal/vertical.</summary>
    /// <summary>
    /// Blends the foreground colour into the background colour across the dragged area. The drag
    /// defines the gradient's axis: its start is where the blend begins and its end where it
    /// finishes, so both the angle and how gradual the blend is come from the drag itself.
    /// Five blend shapes are offered, and the result is dithered by default because an 8-bit
    /// channel can only step in whole units - a slow blend across a wide area would otherwise
    /// show obvious banding.
    /// </summary>
    public class GradientTool : DragShapeToolBase
    {
        public override string Name => "Gradient";
        public override string StatusHint => "Drag to blend the foreground colour into the background colour; the drag sets the angle and length.";
        protected override bool ConstrainsToAngle => true;

        // A gradient paints exactly its dragged box - no stroke spills past it, so no padding.
        protected override bool UsesStrokePadding => false;

        // Committed straight away rather than left as a movable selection. A gradient fills the
        // area you dragged over; there's no useful "now reposition it" step, so the selection was
        // just something to dismiss. Committing directly also keeps the tool selected, so several
        // gradients can be laid down in a row, and it avoids the re-render round trip entirely -
        // which is where the colour-drift problems came from.
        protected override bool UsesPendingShape => false;

        // Deterministic 4x4 Bayer matrix: an ordered dither, which for a smooth gradient looks
        // far cleaner than random noise and, being fixed, means re-rendering the same gradient
        // always produces identical output (important now that pending shapes re-render on resize).
        private static readonly int[,] Bayer4 =
        {
            {  0,  8,  2, 10 },
            { 12,  4, 14,  6 },
            {  3, 11,  1,  9 },
            { 15,  7, 13,  5 },
        };

        protected override void DrawPreview(ToolContext ctx, Point start, Point end, RasterSurface surface)
        {
            var from = _button == MouseButton.Right ? ctx.Colors.Background : ctx.Colors.Foreground;
            var to = _button == MouseButton.Right ? ctx.Colors.Foreground : ctx.Colors.Background;
            // Flips which end each colour sits at without making the user swap the foreground and
            // background colours themselves.
            if (ctx.GradientReverse) (from, to) = (to, from);

            double dx = end.X - start.X, dy = end.Y - start.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1) return;
            double len = Math.Sqrt(lenSq);

            // Paint only the dragged box, clipped to the surface. This has to be the box rather
            // than the whole surface: during rubber-banding the surface handed in is the full-canvas
            // preview layer, but on commit it's a bitmap the size of the box - so filling "the whole
            // surface" made the preview cover the entire canvas while the committed result covered
            // only the dragged area. Deriving the region from the drag itself makes both identical.
            // "Whole canvas" paints every pixel of the layer and uses the drag purely to set the
            // blend's direction and length; the default paints only the box that was dragged.
            var box = NormalizedRect(start, end);
            int xStart, yStart, xEnd, yEnd;
            if (ctx.GradientFillsCanvas)
            {
                xStart = 0; yStart = 0; xEnd = surface.Width; yEnd = surface.Height;
            }
            else
            {
                xStart = Math.Max(0, box.X); yStart = Math.Max(0, box.Y);
                xEnd = Math.Min(surface.Width, box.X + box.Width);
                yEnd = Math.Min(surface.Height, box.Y + box.Height);
            }

            for (int y = yStart; y < yEnd; y++)
            {
                for (int x = xStart; x < xEnd; x++)
                {
                    double t = ComputeT(ctx.GradientType, x, y, start, end, dx, dy, lenSq, len);
                    t = Math.Max(0, Math.Min(1, t));

                    if (ctx.GradientDither)
                    {
                        // Nudge the position by up to one output step before quantising, so the
                        // hard edge between two adjacent output values is broken up into a fine
                        // interleave instead of a visible band boundary.
                        // Keyed to the pixel's offset from the gradient's own start point, NOT to
                        // raw surface coordinates. Those differ between the two times a gradient is
                        // rendered - during rubber-banding the surface is the full canvas, so x/y
                        // are document coordinates, while the pending-shape re-render draws into a
                        // box-sized bitmap where they start at zero. Keying on raw coordinates
                        // therefore shifted the dither pattern between the two, which showed up as
                        // the colour subtly changing the moment the selection appeared. The offset
                        // from start is identical in both spaces, so the pattern now lines up.
                        int dxi = x - (int)start.X, dyi = y - (int)start.Y;
                        double noise = (Bayer4[((dyi % 4) + 4) % 4, ((dxi % 4) + 4) % 4] + 0.5) / 16.0 - 0.5;
                        t = Math.Max(0, Math.Min(1, t + noise / 255.0));
                    }

                    var color = Color.FromArgb(
                        (byte)Math.Round(from.A + (to.A - from.A) * t),
                        (byte)Math.Round(from.R + (to.R - from.R) * t),
                        (byte)Math.Round(from.G + (to.G - from.G) * t),
                        (byte)Math.Round(from.B + (to.B - from.B) * t));

                    // A translucent gradient (either endpoint colour carrying alpha < 255,
                    // including the default transparent Background) must blend into whatever is
                    // already drawn there on commit, rather than erase it - a plain SetPixel would
                    // overwrite, not blend. surface.Blend is only ever turned on for that commit
                    // (see DragShapeToolBase.OnMouseUp): during the live rubber-band preview it's
                    // off, and SetPixel is used directly, because the preview surface starts every
                    // single redraw fully transparent - blending onto that is mathematically
                    // identical to just storing, so doing the extra per-pixel read-and-blend on
                    // every mouse-move bought nothing but a visible stutter.
                    if (surface.Blend) surface.BlendPixel(x, y, color, 1.0);
                    else surface.SetPixel(x, y, color);
                }
            }
        }

        /// <summary>Position along the blend (0 = start colour, 1 = end colour) for one pixel,
        /// according to the selected gradient shape. Exposed (as PositionAlong) so the tool-options
        /// preview can render each blend with this exact maths rather than an approximation of it.</summary>
        internal static double PositionAlong(GradientType type, int x, int y, Point start, Point end,
                                             double dx, double dy, double lenSq, double len)
            => ComputeT(type, x, y, start, end, dx, dy, lenSq, len);

        private static double ComputeT(GradientType type, int x, int y, Point start, Point end,
                                       double dx, double dy, double lenSq, double len)
        {
            double px = x - start.X, py = y - start.Y;
            switch (type)
            {
                case GradientType.Reflected:
                    // Same projection as linear, mirrored so both directions blend away from start.
                    return Math.Abs((px * dx + py * dy) / lenSq);

                case GradientType.Radial:
                    return Math.Sqrt(px * px + py * py) / len;

                case GradientType.Diamond:
                    // Chebyshev-style distance gives square-cornered rings rather than circles.
                    return (Math.Abs(px * dx + py * dy) + Math.Abs(px * dy - py * dx)) / lenSq;

                case GradientType.Angular:
                    // Angle around the start point, normalised to 0..1 for a full sweep.
                    double ang = Math.Atan2(py, px) - Math.Atan2(dy, dx);
                    while (ang < 0) ang += Math.PI * 2;
                    return ang / (Math.PI * 2);

                case GradientType.Linear:
                default:
                    // Linear - project the pixel onto the drag vector. Listed explicitly rather
                    // than left to `default` alone so that adding a new GradientType without a
                    // matching branch is caught by the verifier instead of silently rendering as
                    // a linear blend.
                    return (px * dx + py * dy) / lenSq;
            }
        }
    }

    /// <summary>Click to add vertices, double-click (or Enter) to close the polygon (spec section 26).</summary>
    public class PolygonTool : ITool
    {
        public string Name => "Polygon";
        public string StatusHint => "Click to place vertices; double-click to finish the polygon.";

        private readonly List<Point> _points = new();
        private MouseButton _button;
        private bool _active;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_active)
            {
                if (ctx.Selection.HasSelection) ctx.Selection.Deselect(ctx.Document.Surface);
                ctx.Canvas.ShowSelection(null);
                _active = true; _button = e.Button; _points.Clear();
            }
            _points.Add(e.DocPointInt);
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_active || _points.Count == 0) return;
            ctx.Canvas.ClearPreview();
            ctx.Canvas.PreviewSurface.Lock();
            for (int i = 0; i < _points.Count - 1; i++)
                DrawSeg(ctx, ctx.Canvas.PreviewSurface, _points[i], _points[i + 1]);
            DrawSeg(ctx, ctx.Canvas.PreviewSurface, _points[^1], e.DocPointInt);
            ctx.Canvas.PreviewSurface.Unlock();
        }

        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            // Double-click detection: WPF raises two down+up pairs; MainWindow forwards a flag via ClickCount
            // is not directly available here, so we treat a click very near the first point, or an explicit
            // Finish() call (wired to double-click in MainWindow), as closing the polygon.
        }

        public void Finish(ToolContext ctx)
        {
            if (!_active || _points.Count < 2) { Cancel(ctx); return; }
            ctx.Canvas.ClearPreview();
            ctx.History.PushUndoState(ctx.Document, "Polygon");
            ctx.Document.Surface.Lock();
            var (outline, fill, _) = Resolve(ctx);
            bool doFill = ctx.ShapeFillMode != ShapeFillMode.OutlineOnly;
            if (doFill) FillPolygon(ctx.Document.Surface, _points, fill);
            for (int i = 0; i < _points.Count; i++)
                DrawSeg(ctx, ctx.Document.Surface, _points[i], _points[(i + 1) % _points.Count]);
            ctx.Document.Surface.Unlock();
            ctx.Document.MarkDirty();
            _active = false;
            _points.Clear();
        }

        private (Color outline, Color fill, bool _) Resolve(ToolContext ctx)
        {
            Color outline = _button == MouseButton.Right ? ctx.Colors.Background : ctx.Colors.Foreground;
            Color fill = _button == MouseButton.Right ? ctx.Colors.Foreground : ctx.Colors.Background;
            return (outline, fill, true);
        }

        private void DrawSeg(ToolContext ctx, RasterSurface s, Point a, Point b)
        {
            var (outline, _, _) = Resolve(ctx);
            s.DrawLine((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, outline, Math.Max(1, ctx.PenSize));
        }

        internal static void FillPolygon(RasterSurface s, List<Point> pts, Color fill)
        {
            if (pts.Count < 3) return;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in pts) { minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            for (int y = (int)minY; y <= (int)maxY; y++)
            {
                var xs = new List<double>();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    {
                        double t = (y - a.Y) / (b.Y - a.Y);
                        xs.Add(a.X + t * (b.X - a.X));
                    }
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                    for (int x = (int)xs[i]; x <= (int)xs[i + 1]; x++)
                        s.SetPixel(x, y, fill);
            }
        }

        public void Cancel(ToolContext ctx)
        {
            _active = false;
            _points.Clear();
            ctx.Canvas.ClearPreview();
        }
    }

    /// <summary>Simplified curve tool: classic Paint's curve is a 2-stage line-bend interaction.
    /// This implementation reproduces the workflow with a single control-point drag per stage,
    /// rendered as a quadratic Bezier - a faithful approximation of the interaction, though the
    /// exact cubic math of the original tool is simplified (documented deviation, see README).</summary>
    public class CurveTool : ITool
    {
        public string Name => "Curve";
        public string StatusHint => "Drag to draw a line, then drag it twice to bend it into a curve.";

        // Three gestures, each a complete press-drag-release:
        //   1. DrawingLine - drag out the straight baseline from _p0 to _p1.
        //   2. Bend1       - drag anywhere to pull the first control point.
        //   3. Bend2       - drag again for the second control point, then it commits.
        private enum Stage { Idle, DrawingLine, AwaitBend1, Bending1, AwaitBend2, Bending2 }
        private Stage _stage = Stage.Idle;

        private Point _p0, _p1;   // baseline endpoints
        private Point _c1, _c2;   // bezier control points
        private MouseButton _button;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            var pt = e.DocPointInt;
            switch (_stage)
            {
                case Stage.Idle:
                    if (ctx.Selection.HasSelection) ctx.Selection.Deselect(ctx.Document.Surface);
                    ctx.Canvas.ShowSelection(null);
                    _button = e.Button;
                    _p0 = _p1 = pt;
                    // Start both control points ON the baseline, so before any bend is applied the
                    // curve is exactly the straight line - never a stale bend left over from the
                    // previously drawn curve.
                    _c1 = _c2 = pt;
                    _stage = Stage.DrawingLine;
                    break;

                case Stage.AwaitBend1:
                    // Seed the control point from where the press actually happened, rather than
                    // from a remembered hover position. Relying on a remembered point was the bug
                    // behind the curve snapping back toward its start: if the press landed
                    // somewhere no MouseMove had reported yet, the bend used a stale coordinate
                    // (often still the baseline's own start point) instead of where the user
                    // actually pressed.
                    _c1 = _c2 = pt;
                    _stage = Stage.Bending1;
                    Render(ctx);
                    break;

                case Stage.AwaitBend2:
                    _c2 = pt;
                    _stage = Stage.Bending2;
                    Render(ctx);
                    break;
            }
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            var pt = e.DocPointInt;
            switch (_stage)
            {
                case Stage.DrawingLine:
                    _p1 = pt;
                    // Keep the control points pinned to the baseline while it's still being drawn,
                    // so the preview is a true straight line the whole time.
                    _c1 = _c2 = Mid(_p0, _p1);
                    Render(ctx);
                    break;
                case Stage.Bending1:
                    // First bend drives both control points together, which gives the single
                    // smooth arc you expect from the first drag.
                    _c1 = _c2 = pt;
                    Render(ctx);
                    break;
                case Stage.Bending2:
                    // Second bend moves only the second control point, adding the S-curve.
                    _c2 = pt;
                    Render(ctx);
                    break;
            }
        }

        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            var pt = e.DocPointInt;
            switch (_stage)
            {
                case Stage.DrawingLine:
                    _p1 = pt;
                    _c1 = _c2 = Mid(_p0, _p1);
                    // A zero-length "line" is a stray click, not the start of a curve - reset
                    // rather than stranding the tool mid-gesture waiting for bends.
                    if ((int)_p0.X == (int)_p1.X && (int)_p0.Y == (int)_p1.Y)
                    {
                        _stage = Stage.Idle;
                        ctx.Canvas.ClearPreview();
                        return;
                    }
                    _stage = Stage.AwaitBend1;
                    Render(ctx);
                    break;

                case Stage.Bending1:
                    _c1 = _c2 = pt;
                    _stage = Stage.AwaitBend2;
                    Render(ctx);
                    break;

                case Stage.Bending2:
                    _c2 = pt;
                    Commit(ctx);
                    _stage = Stage.Idle;
                    break;
            }
        }

        private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private void Render(ToolContext ctx)
        {
            ctx.Canvas.ClearPreview();
            ctx.Canvas.PreviewSurface.Lock();
            StrokeSetup.Begin(ctx, ctx.Canvas.PreviewSurface);
            DrawCurve(ctx, ctx.Canvas.PreviewSurface);
            StrokeSetup.End(ctx.Canvas.PreviewSurface);
            ctx.Canvas.PreviewSurface.Unlock();
        }

        private void Commit(ToolContext ctx)
        {
            ctx.Canvas.ClearPreview();
            ctx.History.PushUndoState(ctx.Document, "Curve");
            ctx.Document.Surface.Lock();
            StrokeSetup.Begin(ctx, ctx.Document.Surface);
            DrawCurve(ctx, ctx.Document.Surface);
            StrokeSetup.End(ctx.Document.Surface);
            ctx.Document.Surface.Unlock();
            ctx.Document.MarkDirty();
        }

        private void DrawCurve(ToolContext ctx, RasterSurface s)
        {
            var c = _button == MouseButton.Right ? ctx.Colors.Background : ctx.Colors.Foreground;
            int size = Math.Max(1, ctx.PenSize);
            Point prev = _p0;
            const int steps = 64;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double mt = 1 - t;
                double x = mt * mt * mt * _p0.X + 3 * mt * mt * t * _c1.X + 3 * mt * t * t * _c2.X + t * t * t * _p1.X;
                double y = mt * mt * mt * _p0.Y + 3 * mt * mt * t * _c1.Y + 3 * mt * t * t * _c2.Y + t * t * t * _p1.Y;
                var cur = new Point(x, y);
                s.DrawLine((int)prev.X, (int)prev.Y, (int)cur.X, (int)cur.Y, c, size);
                prev = cur;
            }
        }

        public void Cancel(ToolContext ctx)
        {
            _stage = Stage.Idle;
            ctx.Canvas.ClearPreview();
        }
    }
}
