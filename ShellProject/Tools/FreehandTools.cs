using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PaintClone.Controls;

namespace PaintClone.Tools
{
    public abstract class FreehandToolBase : ITool
    {
        public abstract string Name { get; }
        public abstract string StatusHint { get; }

        protected bool _drawing;
        protected Point _last;
        protected MouseButton _button;

        public virtual void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            ctx.History.PushUndoState(ctx.Document, Name);
            _drawing = true;
            _button = e.Button;
            _last = e.DocPointInt;
            ctx.Document.Surface.Lock();
            Deposit(ctx, e);
            ctx.Document.Surface.Unlock();
            ctx.Document.MarkDirty();
        }

        public virtual void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_drawing) return;
            ctx.Document.Surface.Lock();
            // Interpolate so fast mouse movement doesn't leave gaps (spec section 20).
            var p0 = _last;
            var p1 = e.DocPointInt;
            double dist = Math.Max(Math.Abs(p1.X - p0.X), Math.Abs(p1.Y - p0.Y));
            int steps = Math.Max(1, (int)dist);
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                var mid = new CanvasMouseEventArgs(
                    new Point(p0.X + (p1.X - p0.X) * t, p0.Y + (p1.Y - p0.Y) * t), _button, e.ShiftDown);
                Deposit(ctx, mid);
            }
            ctx.Document.Surface.Unlock();
            _last = p1;
            ctx.Document.MarkDirty();
        }

        public virtual void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            _drawing = false;
        }

        public virtual void Cancel(ToolContext ctx) => _drawing = false;

        protected Color ActiveColor(ToolContext ctx, MouseButton btn) =>
            btn == MouseButton.Right ? ctx.Colors.Background : ctx.Colors.Foreground;

        protected abstract void Deposit(ToolContext ctx, CanvasMouseEventArgs e);
    }

    /// <summary>Hard-edged 1-pixel tool - exact color, no anti-aliasing (spec section 19).</summary>
    public class PencilTool : FreehandToolBase
    {
        public override string Name => "Pencil";
        public override string StatusHint => "Click and drag to draw free-form lines.";

        protected override void Deposit(ToolContext ctx, CanvasMouseEventArgs e)
        {
            var c = ActiveColor(ctx, e.Button);
            int x = (int)e.DocPointInt.X, y = (int)e.DocPointInt.Y;
            int size = Math.Max(1, ctx.PenSize);
            if (size <= 1)
                ctx.Document.Surface.SetPixel(x, y, c);
            else
                ctx.Document.Surface.StampSquare(x, y, size, c);
        }
    }

    /// <summary>Round or square stamp brush of variable size (spec section 20).</summary>
    public class BrushTool : FreehandToolBase
    {
        public override string Name => "Brush";
        public override string StatusHint => "Click and drag to draw with the brush.";

        private static readonly Random SoftRng = new(54321); // fixed seed, same reasoning as the airbrush

        protected override void Deposit(ToolContext ctx, CanvasMouseEventArgs e)
        {
            var c = ActiveColor(ctx, e.Button);
            int x = (int)e.DocPointInt.X, y = (int)e.DocPointInt.Y;
            int size = Math.Max(1, ctx.PenSize);
            var surf = ctx.Document.Surface;
            switch (ctx.BrushShape)
            {
                case BrushShape.Round:
                    surf.StampCircle(x, y, Math.Max(1, size / 2), c);
                    break;
                case BrushShape.Square:
                    surf.StampSquare(x, y, size, c);
                    break;
                case BrushShape.DiagonalRight:
                    surf.StampDiagonalRight(x, y, size, c);
                    break;
                case BrushShape.DiagonalLeft:
                    surf.StampDiagonalLeft(x, y, size, c);
                    break;
                case BrushShape.Splatter:
                    surf.StampSplatter(x, y, Math.Max(1, size), c);
                    break;
                case BrushShape.Cross:
                    surf.StampCross(x, y, size, c);
                    break;
                case BrushShape.Soft:
                    surf.StampSoft(x, y, Math.Max(2, size), c, SoftRng);
                    break;
                case BrushShape.Triangle:
                    surf.StampTriangle(x, y, size, c);
                    break;
                case BrushShape.Diamond:
                    surf.StampDiamond(x, y, size, c);
                    break;
                case BrushShape.Star:
                    surf.StampStar(x, y, Math.Max(3, size), c);
                    break;
                case BrushShape.Ring:
                    surf.StampRing(x, y, Math.Max(3, size), c);
                    break;
                case BrushShape.HollowSquare:
                    surf.StampHollowSquare(x, y, Math.Max(3, size), c);
                    break;
                case BrushShape.HorizontalBar:
                    surf.StampHorizontalBar(x, y, size, c);
                    break;
                case BrushShape.VerticalBar:
                    surf.StampVerticalBar(x, y, size, c);
                    break;
                case BrushShape.Calligraphy:
                    surf.StampCalligraphy(x, y, Math.Max(2, size), c);
                    break;
                case BrushShape.Chalk:
                    surf.StampChalk(x, y, Math.Max(2, size), c, SoftRng);
                    break;
                case BrushShape.Stipple:
                    surf.StampStipple(x, y, Math.Max(2, size), c, SoftRng);
                    break;
            }
        }
    }

    /// <summary>Spray-can: random dot density inside the brush radius (spec section 21).</summary>
    public class AirbrushTool : FreehandToolBase
    {
        public override string Name => "Airbrush";
        public override string StatusHint => "Click and hold to spray color.";

        private static readonly Random Rnd = new Random(12345); // fixed seed: reproducible for testing

        public override void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            ctx.History.PushUndoState(ctx.Document, Name);
            _drawing = true;
            _button = e.Button;
            _last = e.DocPointInt;
            Spray(ctx, e);
        }

        public override void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_drawing) return;
            _last = e.DocPointInt;
            Spray(ctx, e);
        }

        private void Spray(ToolContext ctx, CanvasMouseEventArgs e)
        {
            var c = ActiveColor(ctx, e.Button);
            int radius = Math.Max(2, ctx.PenSize);
            int dots = radius * 2;
            ctx.Document.Surface.Lock();
            for (int i = 0; i < dots; i++)
            {
                double angle = Rnd.NextDouble() * Math.PI * 2;
                double r = Rnd.NextDouble() * radius;
                int x = (int)(e.DocPointInt.X + Math.Cos(angle) * r);
                int y = (int)(e.DocPointInt.Y + Math.Sin(angle) * r);
                ctx.Document.Surface.SetPixel(x, y, c);
            }
            ctx.Document.Surface.Unlock();
            ctx.Document.MarkDirty();
        }

        protected override void Deposit(ToolContext ctx, CanvasMouseEventArgs e) { /* unused - overridden above */ }
    }

    /// <summary>Erases to the BACKGROUND color, not hardcoded white (spec section 15).</summary>
    public class EraserTool : FreehandToolBase
    {
        public override string Name => "Eraser";
        public override string StatusHint => "Click and drag to erase part of the picture.";

        protected override void Deposit(ToolContext ctx, CanvasMouseEventArgs e)
        {
            // Eraser always deposits the background color regardless of mouse button,
            // matching classic Paint (right-drag erases too, still using background).
            var c = ctx.Colors.Background;
            int size = Math.Max(4, ctx.PenSize * 2); // eraser default footprint is chunkier than the pen size
            ctx.Document.Surface.StampSquare((int)e.DocPointInt.X, (int)e.DocPointInt.Y, size, c);
        }
    }
}
