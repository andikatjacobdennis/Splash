using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PaintClone.Services;

namespace PaintClone.Dialogs
{
    public partial class AttributesDialog : Window
    {
        public int NewWidth { get; private set; }
        public int NewHeight { get; private set; }
        public bool ClearRequested { get; private set; }
        public double NewDpi { get; private set; }

        private readonly List<SizePreset> _customPresets;
        private readonly int _originalWidth, _originalHeight;
        private bool _suppress;

        public AttributesDialog(int width, int height, double dpi)
        {
            InitializeComponent();
            _originalWidth = width;
            _originalHeight = height;
            _customPresets = CanvasSizePresetStore.Load();

            WidthBox.Text = width.ToString();
            HeightBox.Text = height.ToString();
            DpiBox.Text = ((int)Math.Round(dpi)).ToString();

            PopulatePresetCombo();
            SelectMatchingPreset(width, height);

            WidthBox.TextChanged += Dpi_Changed;
            HeightBox.TextChanged += Dpi_Changed;
            WidthBox.TextChanged += Size_Changed;
            HeightBox.TextChanged += Size_Changed;
            UpdatePrintSize();
        }

        /// <summary>Same built-in list New Picture offers (Services/CanvasSizePresetStore.BuiltIn),
        /// plus whatever custom sizes the user has saved from there - so resizing an existing
        /// picture to "the size I usually use" doesn't mean re-typing pixel dimensions from
        /// memory. A leading "Custom size" entry represents typing your own Width/Height directly,
        /// which is also what any preset row falls back to the moment those boxes are edited by
        /// hand (see Size_Changed) - the combo should never silently keep showing a preset name
        /// next to numbers that no longer match it.</summary>
        private void PopulatePresetCombo()
        {
            PresetCombo.Items.Clear();
            PresetCombo.Items.Add(new ComboBoxItem { Content = "Custom size" });
            PresetCombo.Items.Add(new ComboBoxItem { Content = "Built-in sizes", FontWeight = FontWeights.Bold, IsEnabled = false });
            foreach (var p in CanvasSizePresetStore.BuiltIn)
                PresetCombo.Items.Add(new ComboBoxItem { Content = $"{p.Name}  ({p.Width} x {p.Height})", Tag = p });

            if (_customPresets.Count > 0)
            {
                PresetCombo.Items.Add(new ComboBoxItem { Content = "Your saved sizes", FontWeight = FontWeights.Bold, IsEnabled = false });
                foreach (var p in _customPresets)
                    PresetCombo.Items.Add(new ComboBoxItem { Content = $"{p.Name}  ({p.Width} x {p.Height})", Tag = p });
            }

            PresetCombo.SelectedIndex = 0;
        }

        /// <summary>If the picture's current size happens to exactly match one of the preset rows,
        /// show that instead of "Custom size" - purely cosmetic (it doesn't change Width/Height,
        /// which are already set from the real current size), but it's what tells you at a glance
        /// that you're looking at, say, "Full HD 1080p" rather than a coincidentally identical
        /// custom size.</summary>
        private void SelectMatchingPreset(int w, int h)
        {
            foreach (var obj in PresetCombo.Items)
            {
                if (obj is ComboBoxItem { Tag: SizePreset p } item && p.Width == w && p.Height == h)
                {
                    PresetCombo.SelectedItem = item;
                    return;
                }
            }
            PresetCombo.SelectedIndex = 0;
        }

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetCombo.SelectedItem is ComboBoxItem { Tag: SizePreset p })
            {
                _suppress = true;
                WidthBox.Text = p.Width.ToString();
                HeightBox.Text = p.Height.ToString();
                _suppress = false;
            }
        }

        /// <summary>Editing Width/Height by hand no longer matches whatever preset (if any) was
        /// selected, so drop back to "Custom size" rather than leave a stale, now-inaccurate preset
        /// name showing. Guarded by _suppress so this doesn't immediately undo the combo's own
        /// selection when it's the one that just wrote these same boxes.</summary>
        private void Size_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppress) return;
            PresetCombo.SelectedIndex = 0;
            // Typing a real size by hand after clicking Clear cancels that request - otherwise OK
            // would silently wipe the picture to 1x1 regardless of whatever was just typed here,
            // since ClearRequested (not these boxes) is what MainWindow actually acts on.
            ClearRequested = false;
        }

        /// <summary>Shows the physical size the picture will print at, which is the only place the
        /// DPI value actually becomes visible - pixel dimensions alone don't tell you that.</summary>
        private void UpdatePrintSize()
        {
            if (PrintSizeText == null) return;
            if (int.TryParse(WidthBox.Text, out int w) && int.TryParse(HeightBox.Text, out int h)
                && double.TryParse(DpiBox.Text, out double d) && d > 0)
            {
                double win = w / d, hin = h / d;
                PrintSizeText.Text = $"Prints at {win:0.##} x {hin:0.##} in  ({win * 2.54:0.##} x {hin * 2.54:0.##} cm)";
            }
            else
            {
                PrintSizeText.Text = "";
            }
        }

        private void Dpi_Changed(object sender, TextChangedEventArgs e) => UpdatePrintSize();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(WidthBox.Text, out int w) || w <= 0 || w > 30000)
            {
                MessageBox.Show(this, "Please enter a valid width.", "Attributes", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (!int.TryParse(HeightBox.Text, out int h) || h <= 0 || h > 30000)
            {
                MessageBox.Show(this, "Please enter a valid height.", "Attributes", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (!double.TryParse(DpiBox.Text, out double dpi) || dpi <= 0 || dpi > 4800)
            {
                MessageBox.Show(this, "Please enter a valid resolution between 1 and 4800 DPI.", "Attributes", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            // Confirm before anything destructive - shrinking crops away whatever falls outside
            // the new size, and Clear wipes the picture entirely. A DPI-only change or growing the
            // canvas never loses pixels, so those go through without asking. Ctrl+Z can still undo
            // either one afterward, but that's not obvious from inside this dialog, so it's worth
            // spelling out rather than letting a size typo or a stray Clear click through silently.
            if (ClearRequested)
            {
                if (MessageBox.Show(this,
                        "This clears the entire picture. You can still undo it afterward with Ctrl+Z, but not from this dialog. Continue?",
                        "Attributes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
            else if (w < _originalWidth || h < _originalHeight)
            {
                if (MessageBox.Show(this,
                        $"The new size ({w} x {h}) is smaller than the current picture ({_originalWidth} x {_originalHeight}) - anything outside it will be cropped away. You can still undo this afterward with Ctrl+Z. Continue?",
                        "Attributes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            NewWidth = w;
            NewHeight = h;
            NewDpi = dpi;
            DialogResult = true;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            // Set the boxes first: they're wired to Size_Changed, which resets ClearRequested to
            // false on any hand-edit (see there) - setting the flag itself afterward is what makes
            // it stick instead of immediately cancelling itself out.
            WidthBox.Text = "1";
            HeightBox.Text = "1";
            ClearRequested = true;
        }
    }
}
