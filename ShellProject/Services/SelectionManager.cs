using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaintClone.Models;

namespace PaintClone.Services
{
    public enum SelectionTransform { FlipHorizontal, FlipVertical, Rotate90, Rotate180, Rotate270 }

    /// <summary>
    /// Owns the current selection: its bounds, an optional free-form mask, and - once the user
    /// starts dragging it - a "floating" copy of its pixels that rides on the preview layer while
    /// the underlying document already shows the vacated (background-filled) hole. This mirrors
    /// classic Paint's behavior where a selection is only actually lifted out of the bitmap once
    /// you drag it (spec sections 12/13/39).
    /// </summary>
    public class SelectionManager
    {
        public Int32Rect? Bounds { get; private set; }
        public bool[,] Mask { get; private set; }          // null == full rectangle selected
        public WriteableBitmap Floating { get; private set; }
        public bool IsFloating => Floating != null;
        public bool HasSelection => Bounds.HasValue;
        public bool DrawOpaque { get; set; } = true;

        public event EventHandler Changed;

        public void BeginSelection(Int32Rect bounds, bool[,] mask = null)
        {
            ContentIsGenerated = false; // a plain selection describes existing document pixels
            Bounds = bounds;
            Mask = mask;
            Floating = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool Contains(Point p)
        {
            if (!Bounds.HasValue) return false;
            var b = Bounds.Value;
            int lx = (int)p.X - b.X, ly = (int)p.Y - b.Y;
            if (lx < 0 || ly < 0 || lx >= b.Width || ly >= b.Height) return false;
            return Mask == null || Mask[lx, ly];
        }

        /// <summary>Extracts the selection's pixels into a floating bitmap and fills the vacated area
        /// in the document with the background color. Call History.PushUndoState before this.</summary>
        public void Lift(RasterSurface doc, Color background)
        {
            if (!Bounds.HasValue || Floating != null) return;
            ContentIsGenerated = false; // this content came out of the document, so keying applies
            var b = Bounds.Value;
            var extracted = doc.CopyRegion(b);

            if (Mask != null)
            {
                var masked = new RasterSurface(extracted);
                masked.Lock();
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                        if (!Mask[x, y]) masked.SetPixel(x, y, Colors.Transparent);
                masked.Unlock();
            }
            Floating = extracted;

            // Vacate the original area.
            doc.Lock();
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (Mask == null || Mask[x, y]) doc.SetPixel(b.X + x, b.Y + y, background);
            doc.Unlock();
        }

        public void MoveTo(int newX, int newY)
        {
            if (!Bounds.HasValue) return;
            var b = Bounds.Value;
            Bounds = new Int32Rect(newX, newY, b.Width, b.Height);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Rescales the floating content to a new size and position via nearest-neighbor
        /// sampling (consistent with the rest of this engine's raster operations, e.g. Stretch/
        /// Skew), and updates Bounds to match. Scoped to rectangular selections (Mask == null) -
        /// a free-form/magic-wand mask would need rescaling too, which isn't implemented, so
        /// resize handles are simply not offered for those. The caller is responsible for having
        /// already lifted the selection (Floating != null) before calling this.</summary>
        /// <summary>Rescales the floating content to a new size and position via nearest-neighbor
        /// sampling (consistent with the rest of this engine's raster operations, e.g. Stretch/
        /// Skew), and updates Bounds to match. A free-form/magic-wand Mask is rescaled alongside
        /// the pixels using the identical sampling, so irregular selections resize correctly too.
        /// The caller is responsible for having already lifted the selection (Floating != null)
        /// before calling this.</summary>
        public void ResizeTo(int newX, int newY, int newWidth, int newHeight)
        {
            if (Floating == null) return;
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);

            int srcW = Floating.PixelWidth, srcH = Floating.PixelHeight;
            var srcSurface = new RasterSurface(Floating);
            var dst = new RasterSurface(newWidth, newHeight, Colors.Transparent);
            bool[,] newMask = Mask != null ? new bool[newWidth, newHeight] : null;

            srcSurface.Lock();
            dst.Lock();
            for (int y = 0; y < newHeight; y++)
            {
                int sy = Math.Min(srcH - 1, (int)((long)y * srcH / newHeight));
                for (int x = 0; x < newWidth; x++)
                {
                    int sx = Math.Min(srcW - 1, (int)((long)x * srcW / newWidth));
                    dst.SetPixel(x, y, srcSurface.ReadRawUnpremultiplied(sx, sy));

                    // Sample the mask with the identical mapping, so the mask and the pixels it
                    // describes stay exactly in register after the rescale. Mask dimensions always
                    // match the pre-resize Floating dimensions (both come from the same Bounds),
                    // so sx/sy are valid indices into it without any separate clamping.
                    if (newMask != null) newMask[x, y] = Mask[sx, sy];
                }
            }
            dst.Unlock();
            srcSurface.Unlock(new Int32Rect(0, 0, 0, 0));

            Floating = dst.Bitmap;
            Mask = newMask;
            Bounds = new Int32Rect(newX, newY, newWidth, newHeight);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Flips or rotates the floating selection content in place, keeping any
        /// free-form mask in exact register with the pixels it describes. Only 90-degree multiples
        /// and axis flips are supported: those keep the result axis-aligned, so it still fits the
        /// Int32Rect bounds model (a 90/270 rotation just swaps width and height). Arbitrary-angle
        /// rotation would need a different bounds representation entirely, which is why it isn't
        /// offered. The caller must have lifted the selection (Floating != null) first.</summary>
        public void TransformFloating(SelectionTransform transform)
        {
            if (Floating == null || !Bounds.HasValue) return;

            int srcW = Floating.PixelWidth, srcH = Floating.PixelHeight;
            bool swapsAxes = transform is SelectionTransform.Rotate90 or SelectionTransform.Rotate270;
            int dstW = swapsAxes ? srcH : srcW;
            int dstH = swapsAxes ? srcW : srcH;

            var srcSurface = new RasterSurface(Floating);
            var dst = new RasterSurface(dstW, dstH, Colors.Transparent);
            bool[,] newMask = Mask != null ? new bool[dstW, dstH] : null;

            srcSurface.Lock();
            dst.Lock();
            for (int y = 0; y < srcH; y++)
            {
                for (int x = 0; x < srcW; x++)
                {
                    int dx, dy;
                    switch (transform)
                    {
                        case SelectionTransform.FlipHorizontal: dx = srcW - 1 - x; dy = y; break;
                        case SelectionTransform.FlipVertical: dx = x; dy = srcH - 1 - y; break;
                        case SelectionTransform.Rotate90: dx = srcH - 1 - y; dy = x; break;
                        case SelectionTransform.Rotate270: dx = y; dy = srcW - 1 - x; break;
                        default: dx = srcW - 1 - x; dy = srcH - 1 - y; break; // Rotate180
                    }
                    dst.SetPixel(dx, dy, srcSurface.ReadRawUnpremultiplied(x, y));
                    if (newMask != null) newMask[dx, dy] = Mask[x, y];
                }
            }
            dst.Unlock();
            srcSurface.Unlock(new Int32Rect(0, 0, 0, 0));

            var b = Bounds.Value;
            Floating = dst.Bitmap;
            Mask = newMask;
            // Keep the selection centered where it was, so a rotation that swaps width and height
            // pivots around the selection's own center rather than jumping to a new corner origin.
            int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
            Bounds = new Int32Rect(cx - dstW / 2, cy - dstH / 2, dstW, dstH);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Starts a floating selection from clipboard/pasted content (nothing to vacate).</summary>
        public void BeginPaste(WriteableBitmap content, int x, int y)
        {
            // Pasted/rendered imagery is generated content, not pixels lifted off the document,
            // so background-colour keying must not be applied to it on commit.
            ContentIsGenerated = true;
            Bounds = new Int32Rect(x, y, content.PixelWidth, content.PixelHeight);
            Mask = null;
            Floating = content;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Writes the floating content permanently into the document (or is a no-op if not floating).</summary>
        public void Commit(RasterSurface doc)
        {
            if (Floating == null || !Bounds.HasValue) return;
            var b = Bounds.Value;
            var src = new RasterSurface(Floating);
            src.Lock();
            doc.Lock();
            for (int y = 0; y < Floating.PixelHeight; y++)
            {
                for (int x = 0; x < Floating.PixelWidth; x++)
                {
                    var px = src.ReadRawUnpremultiplied(x, y);
                    if (px.A == 0) continue; // outside free-form mask
                    if (!DrawOpaque && !ContentIsGenerated && ColorsEqual(px, LastBackground)) continue; // transparent-background paste

                    // A fully opaque pixel is written straight in - fast, and exactly the colour
                    // asked for. A partly transparent one is *blended* instead, because that's what
                    // the on-screen preview was already showing: WPF composites the floating layer
                    // over the canvas with real alpha blending. Hard-overwriting here would replace
                    // the artwork underneath with a semi-transparent pixel rather than laying the
                    // colour over it, which is why a gradient fading into a transparent background
                    // looked right while dragging and then went flat once placed.
                    if (px.A == 255)
                        doc.SetPixel(b.X + x, b.Y + y, px);
                    else
                        doc.BlendPixel(b.X + x, b.Y + y, px, 1.0);
                }
            }
            doc.Unlock();
            src.Unlock(new Int32Rect(0, 0, 0, 0));
            Floating = null;
        }

        public Color LastBackground = Colors.White;

        /// <summary>Clears the selection and throws away any floating content *without* writing it
        /// into the document - the opposite of Deselect, which commits first. Used to cancel an
        /// uncommitted pending shape, where the document was never modified and the intent is to
        /// discard the shape entirely rather than paint it.</summary>
        /// <summary>True when the floating content was *generated* (a pending shape, or pasted
        /// imagery) rather than lifted out of the document. The Opaque/Transparent option exists to
        /// make background-coloured pixels of *lifted* content see-through when you move it - it
        /// must not apply to generated content, where a pixel that happens to match the background
        /// colour is real, deliberately-drawn output. Without this distinction a gradient running
        /// into the background colour lost that entire end of its blend the moment it was placed,
        /// and any shape drawn in the background colour vanished on commit.</summary>
        public bool ContentIsGenerated { get; set; }

        public void Discard()
        {
            ContentIsGenerated = false;
            Floating = null;
            Bounds = null;
            Mask = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Commits any in-progress float, then clears the selection entirely.</summary>
        public void Deselect(RasterSurface doc)
        {
            if (Floating != null) Commit(doc);
            Bounds = null;
            Mask = null;
            Floating = null;
            ContentIsGenerated = false; // don't let it leak into the next selection
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Deletes the selected content, leaving background color behind, and clears the selection outline.</summary>
        public void DeleteSelection(RasterSurface doc, Color background)
        {
            if (!Bounds.HasValue) return;
            if (Floating == null) Lift(doc, background); // vacate now
            Floating = null;
            Bounds = null;
            Mask = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Returns a standalone bitmap suitable for the clipboard (mask applied as alpha).</summary>
        public WriteableBitmap CopyForClipboard(RasterSurface doc)
        {
            if (!Bounds.HasValue) return null;
            if (Floating != null) return Floating;
            var b = Bounds.Value;
            var region = doc.CopyRegion(b);
            if (Mask != null)
            {
                var s = new RasterSurface(region);
                s.Lock();
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                        if (!Mask[x, y]) s.SetPixel(x, y, Colors.Transparent);
                s.Unlock();
            }
            return region;
        }

        private static bool ColorsEqual(Color a, Color b) => a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
