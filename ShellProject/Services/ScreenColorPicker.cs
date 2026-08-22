using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PaintClone.Services
{
    /// <summary>
    /// Samples a colour from anywhere on screen, not just from the picture - the "pick a colour
    /// from something else on my desktop" gesture an eyedropper is expected to support.
    ///
    /// Reads the pixel under the pointer straight from the screen device context. It only ever
    /// *reads* one pixel at a time, and only while the user is actively holding the mouse down
    /// having chosen to do this - nothing is captured, stored, or read in the background.
    /// </summary>
    public static class ScreenColorPicker
    {
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hDC, int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        /// <summary>The colour of the screen pixel currently under the mouse pointer, or null if
        /// the screen can't be read.</summary>
        public static Color? ColorUnderCursor()
        {
            if (!GetCursorPos(out var p)) return null;
            return ColorAt(p.X, p.Y);
        }

        /// <summary>The colour of one screen pixel, in physical screen coordinates.</summary>
        public static Color? ColorAt(int screenX, int screenY)
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            try
            {
                uint bgr = GetPixel(dc, screenX, screenY);
                if (bgr == 0xFFFFFFFF) return null; // CLR_INVALID - point isn't on any display
                return Color.FromRgb((byte)(bgr & 0xFF), (byte)((bgr >> 8) & 0xFF), (byte)((bgr >> 16) & 0xFF));
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }
    }

    /// <summary>
    /// Drives one "pick a colour from anywhere on screen" gesture: capture the mouse, follow it
    /// wherever it goes - including outside the app - reporting the colour under it live, and
    /// finish on the next click.
    ///
    /// Mouse capture is what makes this work off-window at all; without it the app stops receiving
    /// move events the moment the pointer leaves it. Capture is always released in a finally-style
    /// path (including on Escape and on losing capture to something else), because a stuck capture
    /// would leave the whole app unable to be clicked.
    /// </summary>
    public sealed class ScreenPickSession
    {
        private readonly UIElement _capturer;
        private readonly Action<Color> _onPreview;
        private readonly Action<Color?> _onFinished;
        private bool _active;

        public ScreenPickSession(UIElement capturer, Action<Color> onPreview, Action<Color?> onFinished)
        {
            _capturer = capturer;
            _onPreview = onPreview;
            _onFinished = onFinished;
        }

        public bool Start()
        {
            if (!_capturer.CaptureMouse()) return false;
            _active = true;
            _capturer.PreviewMouseMove += OnMove;
            _capturer.PreviewMouseLeftButtonUp += OnClick;
            _capturer.PreviewKeyDown += OnKey;
            _capturer.LostMouseCapture += OnLostCapture;
            return true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_active) return;
            var c = ScreenColorPicker.ColorUnderCursor();
            if (c.HasValue) _onPreview?.Invoke(c.Value);
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            if (!_active) return;
            e.Handled = true;
            Finish(ScreenColorPicker.ColorUnderCursor());
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (!_active || e.Key != Key.Escape) return;
            e.Handled = true;
            Finish(null); // cancelled - leave the colour as it was
        }

        private void OnLostCapture(object sender, MouseEventArgs e) => Finish(null);

        private void Finish(Color? result)
        {
            if (!_active) return;
            _active = false;
            _capturer.PreviewMouseMove -= OnMove;
            _capturer.PreviewMouseLeftButtonUp -= OnClick;
            _capturer.PreviewKeyDown -= OnKey;
            _capturer.LostMouseCapture -= OnLostCapture;
            if (_capturer.IsMouseCaptured) _capturer.ReleaseMouseCapture();
            _onFinished?.Invoke(result);
        }
    }
}
