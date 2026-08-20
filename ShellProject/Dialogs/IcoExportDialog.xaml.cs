using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaintClone.Dialogs
{
    /// <summary>
    /// Lets the user pick which sizes go into an exported .ico and preview how the artwork will
    /// actually look at each one. Previewing matters more here than for other formats: a drawing
    /// that reads fine at full size often turns to mush at 16x16, and it's much better to find
    /// that out before exporting than after.
    /// </summary>
    public partial class IcoExportDialog : Window
    {
        private static readonly int[] AllSizes = { 16, 32, 48, 64, 128, 256 };

        private readonly WriteableBitmap _source;
        private readonly List<CheckBox> _boxes = new();

        /// <summary>Sizes the user chose, ascending. Only valid when the dialog returns true.</summary>
        public List<int> SelectedSizes { get; private set; } = new();

        public IcoExportDialog(WriteableBitmap source)
        {
            InitializeComponent();
            _source = source;

            foreach (int size in AllSizes)
            {
                int captured = size;
                var cb = new CheckBox
                {
                    Content = $"{size} x {size}",
                    // 16/32/48 are the sizes Windows actually reaches for in Explorer, the taskbar
                    // and Alt+Tab, so those are on by default; 256 is included as well since it's
                    // what high-DPI displays and large-icon views use.
                    IsChecked = size is 16 or 32 or 48 or 256,
                    Margin = new Thickness(0, 3, 0, 3),
                    Tag = captured
                };
                cb.Checked += (s, e) => { UpdateSummary(); ShowPreview(captured); };
                cb.Unchecked += (s, e) => UpdateSummary();
                cb.MouseEnter += (s, e) => ShowPreview(captured);
                _boxes.Add(cb);
                SizeList.Children.Add(cb);
            }

            ShowPreview(32);
            UpdateSummary();
        }

        /// <summary>Renders the artwork at one target size so the user can judge how it holds up.
        /// Uses the same scaling the exporter itself will use, so the preview is honest.</summary>
        private void ShowPreview(int size)
        {
            try
            {
                var scaled = new TransformedBitmap(_source,
                    new ScaleTransform((double)size / _source.PixelWidth, (double)size / _source.PixelHeight));
                PreviewImage.Source = scaled;
                PreviewLabel.Text = $"{size} x {size} pixels";
            }
            catch
            {
                // Preview is a convenience - never let a scaling hiccup block the export itself.
                PreviewImage.Source = null;
                PreviewLabel.Text = "";
            }
        }

        private void UpdateSummary()
        {
            var chosen = _boxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag).OrderBy(n => n).ToList();
            SummaryText.Text = chosen.Count == 0
                ? "No sizes selected - choose at least one."
                : $"Exporting {chosen.Count} size(s): {string.Join(", ", chosen.Select(n => $"{n}x{n}"))}. " +
                  "Each is stored as 32-bit RGBA PNG data inside the .ico, which preserves full transparency.";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var b in _boxes) b.IsChecked = true;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            SelectedSizes = _boxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag).OrderBy(n => n).ToList();
            if (SelectedSizes.Count == 0)
            {
                MessageBox.Show(this, "Please select at least one size to include.", "Export Icon",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            DialogResult = true;
        }
    }
}
