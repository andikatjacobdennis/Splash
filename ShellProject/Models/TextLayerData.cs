using System.Windows;
using System.Windows.Media;

namespace PaintClone.Models
{
    /// <summary>
    /// The live, editable source data behind a text layer - what you actually typed, not the pixels
    /// it currently renders as. A <see cref="PaintLayer"/> whose <see cref="PaintLayer.Text"/> is
    /// non-null keeps its Surface as a rendered *cache* of this data (see
    /// Services/TextLayerRenderer), regenerated on every edit, move, resize, or document resize -
    /// so the layer stays double-click-to-re-edit forever, the way a Photoshop type layer does,
    /// instead of becoming permanent pixels the moment you click away. It only stops being this and
    /// becomes an ordinary raster layer once explicitly rasterized or merged into another layer.
    /// </summary>
    public class TextLayerData
    {
        public string Content = "";
        public string FontFamily = "Segoe UI";
        public double FontSize = 16;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public Color Color = Colors.Black;

        /// <summary>Anti-aliasing captured at the moment this text was last (re)rendered, so a
        /// later re-render (a move, a document resize, reloading from a saved file) reproduces the
        /// same look rather than picking up whatever the currently-selected tool happens to have
        /// its own AntiAlias flag set to.</summary>
        public bool AntiAlias = true;

        /// <summary>True for Photoshop-style "point text" - created with a plain click, never
        /// wraps, and both Width and Height grow to fit while live-editing (see
        /// MainWindow.AutoGrowTextBox). False for "paragraph text" - created with a click-drag,
        /// wraps within a fixed Width, and only Height auto-grows.</summary>
        public bool AutoWidth;

        /// <summary>Bounding box in document pixels. Paragraph text wraps within Width; Height
        /// always grows to fit while live-editing (see MainWindow.AutoGrowTextBox), and so does
        /// Width when AutoWidth is set - otherwise this is exactly what it was last set to.</summary>
        public double X, Y, Width, Height;

        public Rect Bounds => new(X, Y, Width, Height);

        public TextLayerData Clone() => new()
        {
            Content = Content,
            FontFamily = FontFamily,
            FontSize = FontSize,
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Color = Color,
            AntiAlias = AntiAlias,
            AutoWidth = AutoWidth,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height
        };
    }
}
