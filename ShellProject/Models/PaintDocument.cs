using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaintClone.Services;

namespace PaintClone.Models
{
    /// <summary>
    /// The actual image: one or more layers, each an independent RasterSurface of the same
    /// dimensions. Every tool and dialog mutates the *active* layer via Surface (a passthrough
    /// property, kept for compatibility with all the existing single-surface code) - the canvas
    /// control displays every visible layer stacked via its own WPF Image per layer, so no manual
    /// pixel-level recompositing is needed while drawing. A flattened single bitmap is only ever
    /// computed on demand, for saving/printing/copying the whole picture.
    /// </summary>
    public class PaintDocument
    {
        public List<PaintLayer> Layers { get; } = new();
        public int ActiveLayerIndex { get; set; }
        public PaintLayer ActiveLayer => Layers[ActiveLayerIndex];

        /// <summary>The active layer's surface - kept as the primary way tools read/write the
        /// document, unchanged from the single-layer design.</summary>
        public RasterSurface Surface => ActiveLayer.Surface;
        public WriteableBitmap Bitmap => Surface.Bitmap;

        /// <summary>The picture's own resolution in dots per inch, stored as metadata and written
        /// into saved files that support it (PNG/JPEG/TIFF all carry a DPI field). This is the
        /// *document's* DPI - how large the image is meant to be when printed - and is entirely
        /// separate from the display scaling the app runs at. Defaults to 96, the Windows standard.</summary>
        public double DpiX { get; set; } = 96;
        public double DpiY { get; set; } = 96;

        /// <summary>Physical print size implied by the pixel dimensions and DPI.</summary>
        public double WidthInches => DpiX > 0 ? Width / DpiX : 0;
        public double HeightInches => DpiY > 0 ? Height / DpiY : 0;

        public string FilePath { get; set; }
        public bool IsDirty { get; set; }

        /// <summary>Which Save As filter entry was last used (1=Monochrome, 2=16 Color,
        /// 3=256 Color, 4=24-bit Bitmap, 5=JPEG, 6=GIF, 7=TIFF, 8=PNG). Defaults to 24-bit
        /// Bitmap so a plain Ctrl+S on a never-explicitly-formatted .bmp behaves sensibly.</summary>
        public int LastSaveFilterIndex { get; set; } = 4;

        public int Width => Surface.Width;
        public int Height => Surface.Height;

        public event EventHandler Changed;
        /// <summary>Fired when the layer list itself changes shape (added/removed/reordered/
        /// visibility toggled) - distinct from Changed (pixel edits), so the canvas and the
        /// Layers window know when they need to rebuild rather than just redraw.</summary>
        public event EventHandler LayersChanged;

        public PaintDocument(int width, int height, Color background)
        {
            Layers.Add(new PaintLayer { Surface = new RasterSurface(width, height, background), Name = "Background" });
            ActiveLayerIndex = 0;
        }

        public static PaintDocument FromBitmap(WriteableBitmap bmp, string path)
        {
            var doc = new PaintDocument(bmp.PixelWidth, bmp.PixelHeight, Colors.White);
            doc.Layers[0].Surface = new RasterSurface(bmp);
            doc.DpiX = bmp.DpiX > 0 ? bmp.DpiX : 96;
            doc.DpiY = bmp.DpiY > 0 ? bmp.DpiY : 96;
            doc.FilePath = path;
            doc.IsDirty = false;
            doc.LastSaveFilterIndex = System.IO.Path.GetExtension(path)?.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => 5,
                ".gif" => 6,
                ".tif" or ".tiff" => 7,
                ".png" => 8,
                ".wdp" or ".jxr" => 9,
                ".ico" => 10,
                ".tga" => 11,
                _ => 4, // .bmp/.dib - re-save as 24-bit unless the user picks a variant explicitly
            };
            return doc;
        }

        public void MarkDirty()
        {
            IsDirty = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Resizes every layer identically, keeping them all the same dimensions. A text
        /// layer is re-rendered fresh from its own TextLayerData instead of having its old pixels
        /// copied/cropped like a raster layer's - re-deriving from the source text is strictly
        /// better than resampling a bitmap (stays crisp, and a canvas resize is always anchored
        /// top-left in this app, so the text's own X/Y position is still valid unchanged).</summary>
        public void Resize(int newWidth, int newHeight, Color background, bool anchorTopLeft = true)
        {
            foreach (var layer in Layers)
            {
                if (layer.Text != null)
                {
                    layer.Surface = new RasterSurface(newWidth, newHeight, Colors.Transparent);
                    TextLayerRenderer.Render(layer.Surface, layer.Text);
                    continue;
                }
                var fresh = new RasterSurface(newWidth, newHeight, background);
                var copyW = Math.Min(newWidth, layer.Surface.Width);
                var copyH = Math.Min(newHeight, layer.Surface.Height);
                var region = layer.Surface.CopyRegion(new Int32Rect(0, 0, copyW, copyH));
                fresh.Blit(region, 0, 0);
                layer.Surface = fresh;
            }
            MarkDirty();
        }

        public void ReplaceSurface(RasterSurface newSurface)
        {
            ActiveLayer.Surface = newSurface;
            MarkDirty();
        }

        /// <summary>Replaces the entire layer list in place (Layers itself can't be reassigned -
        /// it's a read-only auto-property - so this clears and repopulates it instead). Used by
        /// HistoryManager to restore a full undo/redo snapshot, which is what makes layer
        /// structure changes (add/delete/reorder/merge) undoable alongside pixel edits.</summary>
        public void RestoreLayers(List<PaintLayer> layers, int activeIndex)
        {
            Layers.Clear();
            Layers.AddRange(layers);
            ActiveLayerIndex = Math.Max(0, Math.Min(activeIndex, Layers.Count - 1));
            LayersChanged?.Invoke(this, EventArgs.Empty);
            MarkDirty();
        }

        // ===================================================================
        // Layer management (not itself part of the pixel undo/redo history -
        // see README for why that's a deliberate simplification)
        // ===================================================================

        public PaintLayer AddLayer(string name = null)
        {
            var layer = new PaintLayer
            {
                Surface = new RasterSurface(Width, Height, Colors.Transparent),
                Name = name ?? $"Layer {Layers.Count + 1}"
            };
            Layers.Add(layer);
            ActiveLayerIndex = Layers.Count - 1;
            LayersChanged?.Invoke(this, EventArgs.Empty);
            MarkDirty();
            return layer;
        }

        /// <summary>Adds a new text layer rendered from the given (already-populated) TextLayerData
        /// and makes it active - the text-layer counterpart to AddLayer. The caller owns pushing
        /// undo state first, same convention as every other structural layer operation here.</summary>
        public PaintLayer AddTextLayer(TextLayerData text, string name)
        {
            var layer = new PaintLayer
            {
                Surface = new RasterSurface(Width, Height, Colors.Transparent),
                Name = name,
                Text = text
            };
            TextLayerRenderer.Render(layer.Surface, text);
            Layers.Add(layer);
            ActiveLayerIndex = Layers.Count - 1;
            LayersChanged?.Invoke(this, EventArgs.Empty);
            MarkDirty();
            return layer;
        }

        /// <summary>Re-renders a text layer already in the document after its TextLayerData has
        /// been edited in place (content, font, position, ...) - the layer stays exactly where it
        /// was in the stack and keeps its identity, unlike AddTextLayer.</summary>
        public void RefreshTextLayer(PaintLayer layer)
        {
            if (layer.Text == null) return;
            TextLayerRenderer.Render(layer.Surface, layer.Text);
            MarkDirty();
        }

        /// <summary>Converts a text layer into an ordinary raster layer in place, keeping whatever
        /// it currently renders as (Surface is already an up-to-date render - there's nothing to
        /// redraw) but discarding the live TextLayerData, so it's no longer double-click-to-re-edit
        /// and can be painted on directly like any other layer. The Photoshop-style escape hatch for
        /// "I'm done adjusting this text and just want to draw over/under it now."</summary>
        public void RasterizeLayer(int index)
        {
            if (index < 0 || index >= Layers.Count || Layers[index].Text == null) return;
            Layers[index].Text = null;
            LayersChanged?.Invoke(this, EventArgs.Empty);
            MarkDirty();
        }

        public void DeleteLayer(int index)
        {
            if (Layers.Count <= 1 || index < 0 || index >= Layers.Count) return;
            Layers.RemoveAt(index);
            ActiveLayerIndex = Math.Min(ActiveLayerIndex, Layers.Count - 1);
            LayersChanged?.Invoke(this, EventArgs.Empty);
            MarkDirty();
        }

        /// <summary>direction: +1 moves the layer toward the front (later in the list / drawn on
        /// top), -1 moves it toward the back.</summary>
        public void MoveLayer(int index, int direction)
        {
            int target = index + direction;
            if (target < 0 || target >= Layers.Count) return;
            (Layers[index], Layers[target]) = (Layers[target], Layers[index]);
            if (ActiveLayerIndex == index) ActiveLayerIndex = target;
            else if (ActiveLayerIndex == target) ActiveLayerIndex = index;
            LayersChanged?.Invoke(this, EventArgs.Empty);
            MarkDirty();
        }

        /// <summary>Flattens the layer at index into the one below it and removes it.</summary>
        /// <summary>Flattens the layer at index into the one below it and removes it. A hidden
        /// layer's content is never part of the visible composite, so if the layer being merged is
        /// hidden, its pixels are deliberately NOT blitted down - merging should preserve what you
        /// actually see, not surprise you by reviving invisible content into the layer below. The
        /// hidden layer simply disappears, same as it visually already had.</summary>
        public void MergeDown(int index)
        {
            if (index <= 0 || index >= Layers.Count) return;
            var top = Layers[index];
            var bottom = Layers[index - 1];
            if (top.Visible)
                bottom.Surface.Blit(top.Surface.Bitmap, 0, 0, transparentColor: Colors.Transparent);
            Layers.RemoveAt(index);
            ActiveLayerIndex = Math.Min(ActiveLayerIndex, Layers.Count - 1);
            LayersChanged?.Invoke(this, EventArgs.Empty);
            MarkDirty();
        }

        public void SetLayerVisible(int index, bool visible)
        {
            if (index < 0 || index >= Layers.Count) return;
            Layers[index].Visible = visible;
            LayersChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Composites every visible layer, bottom to top, into a single bitmap - used for
        /// Save/Print/Copy-all, never for live on-screen display (the canvas shows layers directly
        /// via WPF's own compositing instead, which is far cheaper during active drawing). Note:
        /// like the rest of this raster engine, this does a hard per-pixel overwrite rather than
        /// true alpha blending, so semi-transparent pixels (e.g. from the Soft brush) won't blend
        /// with the layer below - a known, documented simplification consistent with how drawing
        /// works everywhere else in the app.</summary>
        public WriteableBitmap GetFlattenedBitmap()
        {
            var result = new WriteableBitmap(Width, Height, DpiX, DpiY, PixelFormats.Pbgra32, null);
            var resultSurface = new RasterSurface(result);
            foreach (var layer in Layers)
            {
                if (!layer.Visible) continue;
                resultSurface.Blit(layer.Surface.Bitmap, 0, 0, transparentColor: Colors.Transparent);
            }
            return result;
        }
    }
}
