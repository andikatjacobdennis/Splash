namespace PaintClone.Models
{
    public class PaintLayer
    {
        public RasterSurface Surface;
        public string Name;
        public bool Visible = true;

        /// <summary>Null for an ordinary raster layer. Set for a text layer - Surface above is
        /// always kept rendered from this (see Services/TextLayerRenderer), so every existing piece
        /// of code that reads Surface/Bitmap directly (canvas display, flattening, merge-down,
        /// save/export) keeps working completely unmodified; only the Text tool and the Layers
        /// panel need to know this layer is actually live, re-editable text rather than raw pixels
        /// someone painted.</summary>
        public TextLayerData Text;
    }
}
