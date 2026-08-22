using System.Windows;
using PaintClone.Controls;

namespace PaintClone.Tools
{
    /// <summary>
    /// Matches Photoshop's own Type tool distinction: a plain click starts "point text" - no fixed
    /// width, no wrapping, the box grows to fit whatever's typed - while a real click-drag defines
    /// a fixed-width "paragraph text" box that wraps. MainWindow.BeginTextEditing overlays a real
    /// WPF TextBox (with the classic text toolbar for font/size/bold/italic/underline/color) so
    /// typing feels native. On commit, the text becomes (or updates) its own text layer - live,
    /// re-editable data kept separate from its rendered pixels - rather than being rasterized once
    /// and forgotten, the way classic Paint's text tool worked (spec section 22 documents that
    /// original behavior; this app now goes further, Photoshop-style).
    /// </summary>
    public class TextTool : ITool
    {
        public string Name => "Text";
        public string StatusHint => "Click to type free-standing text, or drag to create a wrapped text box. Click inside existing text to edit it again.";

        // Below this drag distance (document pixels), a press-release counts as a click (point
        // text) rather than a deliberate drag (paragraph text) - matches the small amount of
        // pointer jitter a "plain click" can still have in practice.
        private const double ClickThreshold = 4;

        private bool _dragging;
        private Point _start;

        /// <summary>Set by OnMouseDown when the press landed on existing text; acted on in
        /// OnMouseUp. See the comment there for why this deliberately waits for the release.</summary>
        private bool _reEditPending;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            _reEditPending = false;

            // Clicking on any visible text layer's own text re-opens THAT layer for editing -
            // making it the active layer first if it wasn't already - instead of starting a new
            // box on top of it. Checking only the *active* layer here (as an earlier version of
            // this did) meant clicking back into an
            // earlier piece of text did nothing the moment any other text layer had since become
            // active - which, in practice, is almost immediately: writing a second piece of text
            // makes it the new active layer. Searched front-to-back (last in Layers = frontmost,
            // the app's own convention) so an overlapping topmost text layer wins, matching
            // whatever's actually visible at that point.
            var doc = ctx.Document;
            if (doc != null)
            {
                for (int i = doc.Layers.Count - 1; i >= 0; i--)
                {
                    var layer = doc.Layers[i];
                    if (layer.Visible && layer.Text != null && layer.Text.Bounds.Contains(e.DocPoint))
                    {
                        doc.ActiveLayerIndex = i;
                        // Only flagged here - the editing box is opened on mouse *up* (see
                        // OnMouseUp). PaintCanvas.RaiseDown calls Focus() and, critically,
                        // CaptureMouse() on the canvas immediately before dispatching this event,
                        // so at this instant the canvas holds both keyboard focus and mouse
                        // capture - a TextBox created and focused now can't reliably take or keep
                        // focus, and mouse input keeps routing to the canvas instead of the box.
                        // RaiseUp releases that capture before dispatching mouse-up, which is
                        // exactly why creating a box there (the path new text has always used, and
                        // which has always worked) behaves correctly. Opening on release makes
                        // re-editing take the identical path rather than a subtly different one.
                        _reEditPending = true;
                        return;
                    }
                }
            }

            _dragging = true;
            _start = e.DocPointInt;
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_dragging) return;
            ctx.Canvas.ClearPreview();
            ctx.Canvas.PreviewSurface.Lock();
            var r = Rect(e);
            // A drag too small to be a deliberate box doesn't get an outline preview - it's about
            // to become point text with no box at all, so drawing one here would be misleading.
            if (r.Width >= ClickThreshold || r.Height >= ClickThreshold)
            {
                ctx.Canvas.PreviewSurface.DrawRect(
                    new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height),
                    System.Windows.Media.Colors.Gray, 1, false, System.Windows.Media.Colors.Transparent);
            }
            ctx.Canvas.PreviewSurface.Unlock();
        }

        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            // Re-open existing text now that the canvas has released mouse capture (see the
            // comment in OnMouseDown) - the same point in the gesture at which a brand-new text
            // box gets created below.
            if (_reEditPending)
            {
                _reEditPending = false;
                ctx.BeginTextEditOnActiveLayer?.Invoke();
                return;
            }

            if (!_dragging) return;
            _dragging = false;
            ctx.Canvas.ClearPreview();
            var r = Rect(e);

            if (r.Width < ClickThreshold && r.Height < ClickThreshold)
            {
                // Point text: starts as a small seed box at the click point - MainWindow grows it
                // to fit, in both directions, as text is typed (see AutoGrowTextBox), never wraps.
                ctx.BeginTextEditing?.Invoke(new Rect(_start.X, _start.Y, 20, 16), true);
            }
            else
            {
                // Paragraph text: the dragged box is the fixed wrap width, same as before.
                if (r.Width < 20) r.Width = 120;
                if (r.Height < 16) r.Height = 24;
                ctx.BeginTextEditing?.Invoke(r, false);
            }
        }

        private Rect Rect(CanvasMouseEventArgs e)
        {
            double x = System.Math.Min(_start.X, e.DocPointInt.X);
            double y = System.Math.Min(_start.Y, e.DocPointInt.Y);
            double w = System.Math.Abs(e.DocPointInt.X - _start.X);
            double h = System.Math.Abs(e.DocPointInt.Y - _start.Y);
            return new Rect(x, y, w, h);
        }

        public void Cancel(ToolContext ctx)
        {
            _dragging = false;
            _reEditPending = false;
            ctx.Canvas.ClearPreview();
        }
    }
}
