using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PaintClone.Models;

namespace PaintClone.Controls
{
    public class CanvasMouseEventArgs : EventArgs
    {
        public Point DocPoint { get; }       // exact document-pixel coordinate (can be fractional at sub-pixel drag)
        public Point DocPointInt => new Point(Math.Floor(DocPoint.X), Math.Floor(DocPoint.Y));
        public MouseButton Button { get; }
        public MouseButtonState LeftState { get; }
        public MouseButtonState RightState { get; }
        public bool ShiftDown { get; }
        public int ClickCount { get; }

        public CanvasMouseEventArgs(Point docPoint, MouseButton button, bool shift, int clickCount = 1)
        {
            DocPoint = docPoint;
            Button = button;
            ShiftDown = shift;
            LeftState = Mouse.LeftButton;
            RightState = Mouse.RightButton;
            ClickCount = clickCount;
        }
    }

    /// <summary>
    /// Layout, in z-order:
    ///   1. One Image per document layer, bottom to top, each bound directly to that layer's own
    ///      WriteableBitmap - WPF's normal layered alpha rendering does the visual "flattening" of
    ///      layers for free (GPU compositing), so no manual per-pixel recompositing is needed and
    ///      every existing tool keeps mutating its one active layer's bitmap exactly as before.
    ///   2. PreviewImage   - transparent overlay bitmap; tools draw in-progress shapes here
    ///                       without ever touching the real document until commit (spec section 38)
    ///   3. SelectionRect  - dashed "marching ants" adorner (spec section 39)
    ///
    /// All child elements are sized to Document pixels * Zoom, with nearest-neighbor scaling,
    /// so pixel boundaries stay crisp at any zoom level (spec section 43).
    /// </summary>
    public class PaintCanvas : Grid
    {
        public PaintDocument Document { get; private set; }
        public List<Image> LayerImages { get; } = new();
        public Image PreviewImage { get; } = new Image();
        public Rectangle SelectionRect { get; } = new Rectangle
        {
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        public WriteableBitmap PreviewBitmap { get; private set; }
        public RasterSurface PreviewSurface { get; private set; }

        private double _zoom = 1.0;
        public double Zoom
        {
            get => _zoom;
            set { _zoom = value; UpdateSize(); }
        }

        private readonly DispatcherTimer _antsTimer;

        public event EventHandler<CanvasMouseEventArgs> CanvasMouseDown;
        public event EventHandler<CanvasMouseEventArgs> CanvasMouseMove;
        public event EventHandler<CanvasMouseEventArgs> CanvasMouseUp;

        public PaintCanvas()
        {
            RenderOptions.SetBitmapScalingMode(PreviewImage, BitmapScalingMode.NearestNeighbor);
            PreviewImage.Stretch = Stretch.Fill;
            PreviewImage.HorizontalAlignment = HorizontalAlignment.Left;
            PreviewImage.VerticalAlignment = VerticalAlignment.Top;
            SnapsToDevicePixels = true;
            // Without layout rounding, at fractional DPI scales (125%, 150%) the canvas and its
            // layers can land on fractional device-pixel boundaries, which makes nominally-equal
            // document pixels render at visibly uneven widths and softens edges - particularly
            // bad here, since crisp pixel boundaries are the entire point of a pixel editor.
            UseLayoutRounding = true;

            Children.Add(PreviewImage);
            Children.Add(SelectionRect);

            Background = Brushes.Transparent;
            Focusable = true;

            MouseLeftButtonDown += (s, e) => RaiseDown(e, MouseButton.Left);
            MouseRightButtonDown += (s, e) => RaiseDown(e, MouseButton.Right);
            MouseMove += (s, e) => RaiseMove(e);
            MouseLeftButtonUp += (s, e) => RaiseUp(e, MouseButton.Left);
            MouseRightButtonUp += (s, e) => RaiseUp(e, MouseButton.Right);

            _antsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _antsTimer.Tick += (s, e) =>
            {
                if (SelectionRect.Visibility == Visibility.Visible)
                {
                    var offset = SelectionRect.StrokeDashOffset;
                    SelectionRect.StrokeDashOffset = (offset + 1) % 8;
                }
            };
            _antsTimer.Start();
        }

        public void SetDocument(PaintDocument doc)
        {
            Document = doc;
            SuppressedLayerIndex = -1; // an index into the *previous* document means nothing here
            RebuildLayerImages();
            PreviewBitmap = new WriteableBitmap(doc.Width, doc.Height, 96, 96, PixelFormats.Pbgra32, null);
            PreviewSurface = new RasterSurface(PreviewBitmap);
            PreviewImage.Source = PreviewBitmap;
            UpdateSize();
        }

        /// <summary>Rebuilds the per-layer Image stack to match Document.Layers - order, visibility,
        /// and bitmap source. Cheap enough (only ever a handful of layers) to just call this any
        /// time the layer structure or an individual layer's bitmap identity might have changed,
        /// rather than trying to diff and patch incrementally.</summary>
        private void RebuildLayerImages()
        {
            foreach (var img in LayerImages) Children.Remove(img);
            LayerImages.Clear();

            foreach (var layer in Document.Layers)
            {
                var img = new Image
                {
                    Source = layer.Surface.Bitmap,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Visibility = layer.Visible ? Visibility.Visible : Visibility.Collapsed
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                Children.Insert(LayerImages.Count, img); // bottom-to-top, below PreviewImage/SelectionRect
                LayerImages.Add(img);
            }
            SyncLayerSizesAndVisibility();
        }

        /// <summary>Call after any layer-structure change (add/remove/reorder/visibility toggle).</summary>
        public void RefreshLayers()
        {
            RebuildLayerImages();
            UpdateSize();
        }

        public void RefreshDocumentBinding()
        {
            // WriteableBitmap identity can change (e.g. resize creates a new surface per layer) -
            // rebuild the whole stack rather than trying to track which layer's bitmap changed.
            RebuildLayerImages();
            if (PreviewBitmap.PixelWidth != Document.Width || PreviewBitmap.PixelHeight != Document.Height)
            {
                PreviewBitmap = new WriteableBitmap(Document.Width, Document.Height, 96, 96, PixelFormats.Pbgra32, null);
                PreviewSurface = new RasterSurface(PreviewBitmap);
                PreviewImage.Source = PreviewBitmap;
            }
            UpdateSize();
        }

        /// <summary>Index of a layer to hide *on screen only*, or -1 for none. Used while a text
        /// layer is being live-edited: the editable TextBox overlay already shows that text, so
        /// leaving the layer's own rendered pixels on screen underneath it draws the same text
        /// twice, slightly offset - which is what "two texts overlapping" looks like, and why
        /// editing away the live copy still left the baked one behind.
        ///
        /// Deliberately display-only: it does NOT touch the layer's pixels or its Visible flag.
        /// Both of those are captured in HistoryManager's undo snapshots, so hiding the text that
        /// way would leak a transient editing state into the document's real history (undoing to
        /// mid-edit would restore a blanked or hidden layer). Nothing here is part of the document
        /// at all, so there's nothing for undo to capture.</summary>
        public int SuppressedLayerIndex { get; set; } = -1;

        /// <summary>Re-applies layer visibility after changing <see cref="SuppressedLayerIndex"/>.</summary>
        public void RefreshLayerVisibility() => SyncLayerSizesAndVisibility();

        private void SyncLayerSizesAndVisibility()
        {
            if (Document == null) return;
            double w = Document.Width * _zoom, h = Document.Height * _zoom;
            for (int i = 0; i < LayerImages.Count && i < Document.Layers.Count; i++)
            {
                LayerImages[i].Width = w;
                LayerImages[i].Height = h;
                bool visible = Document.Layers[i].Visible && i != SuppressedLayerIndex;
                LayerImages[i].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateSize()
        {
            if (Document == null) return;
            double w = Document.Width * _zoom;
            double h = Document.Height * _zoom;
            Width = w;
            Height = h;
            SyncLayerSizesAndVisibility();
            PreviewImage.Width = w; PreviewImage.Height = h;
        }

        public void ClearPreview()
        {
            PreviewSurface.Clear(Colors.Transparent);
        }

        public void ShowSelection(Int32Rect? rect)
        {
            if (rect == null || rect.Value.Width <= 0 || rect.Value.Height <= 0)
            {
                SelectionRect.Visibility = Visibility.Collapsed;
                return;
            }
            var r = rect.Value;
            SelectionRect.Visibility = Visibility.Visible;
            SelectionRect.Width = r.Width * _zoom;
            SelectionRect.Height = r.Height * _zoom;
            SelectionRect.Margin = new Thickness(r.X * _zoom, r.Y * _zoom, 0, 0);
            SelectionRect.HorizontalAlignment = HorizontalAlignment.Left;
            SelectionRect.VerticalAlignment = VerticalAlignment.Top;
        }

        private Point ToDocPoint(Point p)
        {
            // Prefer the actual rendered scale over the tracked _zoom field, so mouse coordinates
            // can never end up computed against a different scale than what's really on screen.
            // Uses PreviewImage as the sizing reference since it's always present regardless of
            // layer count (a layer stack could theoretically be empty for a split second mid-rebuild).
            double sx = (Document != null && Document.Width > 0 && PreviewImage.ActualWidth > 0)
                ? PreviewImage.ActualWidth / Document.Width : _zoom;
            double sy = (Document != null && Document.Height > 0 && PreviewImage.ActualHeight > 0)
                ? PreviewImage.ActualHeight / Document.Height : _zoom;
            return new Point(p.X / sx, p.Y / sy);
        }

        private void RaiseDown(MouseButtonEventArgs e, MouseButton btn)
        {
            Focus();
            CaptureMouse();
            var p = ToDocPoint(e.GetPosition(this));
            CanvasMouseDown?.Invoke(this, new CanvasMouseEventArgs(p, btn, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), e.ClickCount));
        }

        private void RaiseMove(MouseEventArgs e)
        {
            var p = ToDocPoint(e.GetPosition(this));
            var btn = Mouse.LeftButton == MouseButtonState.Pressed ? MouseButton.Left
                    : Mouse.RightButton == MouseButtonState.Pressed ? MouseButton.Right
                    : MouseButton.Left;
            CanvasMouseMove?.Invoke(this, new CanvasMouseEventArgs(p, btn, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));
        }

        private void RaiseUp(MouseButtonEventArgs e, MouseButton btn)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            var p = ToDocPoint(e.GetPosition(this));
            CanvasMouseUp?.Invoke(this, new CanvasMouseEventArgs(p, btn, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));
        }
    }
}
