using System.Windows;
using System.Windows.Input;
using PaintClone.Controls;
using PaintClone.Models;
using PaintClone.Services;

namespace PaintClone.Tools
{
    /// <summary>Shared state every tool needs: the document, the canvas (for preview drawing),
    /// active colors, current size/shape option, and history hooks.</summary>
    public class ToolContext
    {
        public PaintDocument Document;
        public PaintCanvas Canvas;
        public ColorManager Colors;
        public HistoryManager History;
        public SelectionManager Selection;

        public int PenSize = 1;                 // pencil/line/eraser/brush size in document pixels
        public ShapeFillMode ShapeFillMode = ShapeFillMode.OutlineOnly;
        public BrushShape BrushShape = BrushShape.Round;

        public System.Action<string> SetStatusText;
        public System.Action RequestCommit; // called by a tool when it wants MainWindow to snapshot+merge preview
        /// <summary>autoWidth: true for a plain click - "point text" that grows in both directions
        /// as you type and never wraps, matching Photoshop's Type tool; false for a click-drag -
        /// "paragraph text" with a fixed wrap width (the rect's own width).</summary>
        public System.Action<Rect, bool> BeginTextEditing;
        /// <summary>Re-opens the active layer's existing text for editing (a Photoshop-style
        /// click-to-edit a type layer) instead of starting a brand-new one - called by TextTool
        /// when a click lands inside a text layer, which it makes the active layer first if it
        /// wasn't already.</summary>
        public System.Action BeginTextEditOnActiveLayer;
        public System.Action<string> RequestToolSwitch; // called by a tool that wants MainWindow to switch tools for it
        public System.Action<PendingShape> BeginPendingShape; // hands MainWindow a drawn-but-not-yet-rasterized shape

        /// <summary>Commits whatever selection/pending shape is currently floating, in place,
        /// without switching tools - called by a drag-shape tool's own OnMouseDown when the user
        /// clicks outside a shape it just drew, right before that same click starts the next shape.
        /// A no-op if nothing is floating. This is deliberately narrower than the
        /// RequestToolSwitch-driven finalize MainWindow uses when switching to a *different* tool
        /// (which also restores the drawing tool afterwards) - here there's nothing to restore,
        /// since the tool never left in the first place.</summary>
        public System.Action FinalizePendingShape;

        /// <summary>Arrowhead style for the Arrow tool.</summary>
        public ArrowStyle ArrowStyle = ArrowStyle.End;

        /// <summary>How many points the Star tool draws.</summary>
        public int StarPoints = 5;

        /// <summary>How deep a star's points cut in, as a percentage of its outer radius. 0 means
        /// "work it out from the point count", which is what the star always did before this was
        /// adjustable - a fixed inner radius makes a many-pointed star read as a gear.</summary>
        public int StarInnerPercent = 0;

        /// <summary>Rotation, in degrees, applied to the vertex-ring shapes (star, polygons, heart,
        /// lightning). Applied about the shape's own centre.</summary>
        public int ShapeRotation = 0;

        /// <summary>Whether the current tool draws with smoothed (anti-aliased) edges. Stored
        /// per-tool by MainWindow, since the right answer differs by tool: a pencil wants hard
        /// pixel edges, a curve usually wants smoothing.</summary>
        public bool AntiAlias;

        /// <summary>How far a pixel's colour may differ from the clicked one and still be
        /// selected by the Magic Wand. 0 means an exact match.</summary>
        public int WandTolerance = 0;

        /// <summary>True: the Magic Wand selects only the connected region you clicked. False:
        /// it selects every matching pixel in the layer, wherever it is.</summary>
        public bool WandContiguous = true;

        /// <summary>Which blend shape the Gradient tool uses.</summary>
        public GradientType GradientType = GradientType.Linear;

        /// <summary>Whether the Gradient tool dithers. Without it, a slow blend across many pixels
        /// shows visible banding, because 8-bit channels can only step in whole units.</summary>
        public bool GradientDither = true;

        /// <summary>True: the gradient paints the whole layer, using the drag only to set its
        /// direction and length. False: it paints just the box that was dragged.</summary>
        public bool GradientFillsCanvas = false;

        /// <summary>Swaps which end of the blend each colour sits at, without having to swap the
        /// foreground and background colours themselves.</summary>
        public bool GradientReverse = false;

        /// <summary>How the outline of a stroked shape or line is broken up.</summary>
        public LineStyle LineStyle = LineStyle.Solid;

        /// <summary>Stretches the drawn runs of a dash pattern, as a percentage of their normal
        /// length. Only meaningful when LineStyle isn't Solid.</summary>
        public int DashLengthPercent = 100;

        /// <summary>Stretches the gaps of a dash pattern, as a percentage of their normal length -
        /// this is the "spacing" control. Only meaningful when LineStyle isn't Solid.</summary>
        public int DashGapPercent = 100;

        /// <summary>Airbrush coverage per spray tick, as a percentage of its normal density.</summary>
        public int AirbrushFlow = 100;

        /// <summary>How far a pixel's colour may differ from the clicked one and still be filled by
        /// the paint bucket. 0 means an exact match - the legacy behaviour, and still the default.</summary>
        public int FillTolerance = 0;

        /// <summary>True: the paint bucket fills only the connected region you clicked. False: it
        /// replaces every matching pixel in the layer, wherever it is.</summary>
        public bool FillContiguous = true;

        /// <summary>Corner rounding for the Rounded Rectangle tool, in document pixels. 0 means
        /// "pick a radius from the shape's own size", which is what it always did before this was
        /// adjustable.</summary>
        public int CornerRadius = 0;
    }

    /// <summary>
    /// A shape that has been drawn but deliberately NOT yet rasterized into the document. It
    /// keeps the parameters it was defined by (start/end points) plus a renderer that can draw it
    /// into any surface at any size. That's the whole point: while the shape is still selected,
    /// moving or resizing it re-renders it *from these original parameters* rather than resampling
    /// the pixels of a previous render. Resampling a resample compounds quality loss every time -
    /// re-rendering from source stays perfectly crisp no matter how many times you adjust it. Only
    /// when the selection is finally committed does it get rasterized into the document, once.
    /// </summary>
    public class PendingShape
    {
        /// <summary>Draws the shape into the given surface, between the given points, in surface-
        /// local coordinates. Supplied by the shape tool that created it.</summary>
        public System.Action<Point, Point, RasterSurface> Render;

        /// <summary>The shape's defining points, in document coordinates.</summary>
        public Point Start, End;

        /// <summary>How far the rendered stroke can extend beyond the exact start/end bounding box
        /// (thick outlines are stamped centered on boundary points) - the bounds are padded by
        /// this so nothing gets clipped.</summary>
        public int Pad;

        /// <summary>Undo-history label, e.g. "Rectangle".</summary>
        public string Label;

        /// <summary>Which tool drew this. The drawing tool itself stays active the whole time this
        /// shape is pending - see DragShapeToolBase, which handles moving its own just-drawn shape
        /// directly rather than detouring through the Select tool - so this is really only read if
        /// the *user* explicitly switches to some other tool while the shape is still pending
        /// (MainWindow.SelectTool then finalizes it and switches back to this, so you're not left
        /// stranded on whatever tool you happened to pick next).</summary>
        public string OriginToolKey;
    }

    public enum ShapeFillMode { OutlineOnly, FillOnly, OutlineAndFill }

    /// <summary>Shapes the Gradient tool can blend along.</summary>
    public enum GradientType
    {
        Linear,     // straight blend along the drag direction
        Reflected,  // mirrored either side of the start point
        Radial,     // circular, spreading out from the start point
        Diamond,    // square-cornered rings from the start point
        Angular     // sweeps around the start point like a colour wheel
    }

    /// <summary>Arrowhead styles offered by the Arrow tool.</summary>
    public enum ArrowStyle
    {
        End,          // open barbs at the end point only
        Both,         // open barbs at both ends
        Filled,       // solid triangular head at the end
        FilledBoth,   // solid triangular heads at both ends
        None,         // plain line (useful with Shift-constrained angles)
        Diamond,      // solid diamond at the end
        DiamondBoth,  // solid diamonds at both ends
        Circle,       // solid dot at the end
        CircleBoth,   // solid dots at both ends
        Bar,          // flat cross-bar (a "tee") at the end
        BarBoth,      // flat cross-bars at both ends
    }

    /// <summary>How a stroked line or shape outline is broken up. Applies to every tool that draws
    /// an outline rather than a filled area.</summary>
    public enum LineStyle { Solid, Dashed, Dotted, DashDot, LongDash }

    public static class LineStyles
    {
        /// <summary>On/off run lengths for a line style, or null for solid. Scaled by the pen size
        /// so a thick dashed line still reads as dashed rather than as a nearly-solid one - a fixed
        /// 4px gap disappears entirely under a 12px stroke.
        ///
        /// dashPercent stretches the drawn runs and gapPercent the empty ones, both independently,
        /// so the same style can be tuned from tight ticks to widely-spaced marks without needing a
        /// separate LineStyle for each combination.</summary>
        public static double[] PatternFor(LineStyle style, int penSize, int dashPercent = 100, int gapPercent = 100)
        {
            double u = System.Math.Max(1, penSize);
            double[] baseRuns = style switch
            {
                LineStyle.Dashed => new[] { u * 4, u * 3 },
                LineStyle.Dotted => new[] { u, u * 2 },
                LineStyle.DashDot => new[] { u * 5, u * 2, u, u * 2 },
                LineStyle.LongDash => new[] { u * 9, u * 4 },
                _ => null,
            };
            if (baseRuns == null) return null;

            double dashScale = System.Math.Clamp(dashPercent, 10, 500) / 100.0;
            double gapScale = System.Math.Clamp(gapPercent, 10, 500) / 100.0;
            var runs = new double[baseRuns.Length];
            for (int i = 0; i < baseRuns.Length; i++)
            {
                // Even entries are the drawn runs, odd ones the gaps. Never let a run round to
                // nothing, or the pattern would silently become solid (or invisible).
                double scaled = baseRuns[i] * (i % 2 == 0 ? dashScale : gapScale);
                runs[i] = System.Math.Max(0.5, scaled);
            }
            return runs;
        }
    }
    public enum BrushShape
    {
        // Original seven
        Round, Square, DiagonalRight, DiagonalLeft, Splatter, Cross, Soft,
        // Ten added for more expressive/professional work
        Triangle, Diamond, Star, Ring, HollowSquare,
        HorizontalBar, VerticalBar, Calligraphy, Chalk, Stipple
    }

    /// <summary>Every tool in the toolbox implements this. Coordinates arrive already converted
    /// to exact document-pixel space by PaintCanvas.</summary>
    public interface ITool
    {
        string Name { get; }
        string StatusHint { get; }

        void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e);
        void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e);
        void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e);

        /// <summary>Esc key / tool switch while mid-operation.</summary>
        void Cancel(ToolContext ctx);
    }
}
