using System.Windows;
using PaintClone.Controls;

namespace PaintClone.Tools
{
    /// <summary>
    /// Click-drag defines the text box rectangle; MainWindow.BeginTextEditing overlays a real
    /// WPF TextBox (with the classic text toolbar for font/size/bold/italic/underline/color) so
    /// typing feels native. On commit the text is rasterized into the bitmap via FormattedText -
    /// it never remains a live WPF text object (spec section 22).
    /// </summary>
    public class TextTool : ITool
    {
        public string Name => "Text";
        public string StatusHint => "Click and drag to create a text box, then type.";

        private bool _dragging;
        private Point _start;

        public void OnMouseDown(ToolContext ctx, CanvasMouseEventArgs e)
        {
            _dragging = true;
            _start = e.DocPointInt;
        }

        public void OnMouseMove(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_dragging) return;
            ctx.Canvas.ClearPreview();
            ctx.Canvas.PreviewSurface.Lock();
            var r = Rect(e);
            ctx.Canvas.PreviewSurface.DrawRect(
                new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height),
                System.Windows.Media.Colors.Gray, 1, false, System.Windows.Media.Colors.Transparent);
            ctx.Canvas.PreviewSurface.Unlock();
        }

        public void OnMouseUp(ToolContext ctx, CanvasMouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ctx.Canvas.ClearPreview();
            var r = Rect(e);
            if (r.Width < 20) r.Width = 120;
            if (r.Height < 16) r.Height = 24;
            ctx.BeginTextEditing?.Invoke(r);
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
            ctx.Canvas.ClearPreview();
        }
    }
}
