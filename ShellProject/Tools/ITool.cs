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
        public System.Action<Rect> BeginTextEditing;
        public System.Action<string> RequestToolSwitch; // called by a tool that wants MainWindow to switch tools for it
        public System.Action<PendingShape> BeginPendingShape; // hands MainWindow a drawn-but-not-yet-rasterized shape

        /// <summary>Arrowhead style for the Arrow tool.</summary>
        public ArrowStyle ArrowStyle = ArrowStyle.End;

        /// <summary>How many points the Star tool draws.</summary>
        public int StarPoints = 5;

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

        /// <summary>Which tool drew this, so the app can switch back to it once the shape is
        /// dropped - otherwise you'd be left on the Select tool after every single shape.</summary>
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
        End,        // open barbs at the end point only
        Both,       // open barbs at both ends
        Filled,     // solid triangular head at the end
        FilledBoth, // solid triangular heads at both ends
        None        // plain line (useful with Shift-constrained angles)
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
