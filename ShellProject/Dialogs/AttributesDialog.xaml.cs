using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

        private readonly ImageStats _stats;

        /// <summary>stats may be null, in which case the Statistics tab is simply not offered -
        /// the dialog still has to work for anything that only wants to change the canvas size.</summary>
        public AttributesDialog(int width, int height, double dpi, ImageStats stats = null)
        {
            InitializeComponent();
            _stats = stats;
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

            if (_stats == null) StatisticsTab.Visibility = Visibility.Collapsed;
            else PopulateStatistics();
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

        // ===================================================================
        // Statistics tab
        // ===================================================================

        private void PopulateStatistics()
        {
            var s = _stats;

            AddStat("Dimensions", $"{s.Width} x {s.Height} px");
            AddStat("Total pixels", $"{s.PixelCount:N0}  ({s.PixelCount / 1_000_000.0:0.##} MP)");
            AddStat("Layers", s.LayerCount == s.VisibleLayerCount
                ? $"{s.LayerCount}"
                : $"{s.LayerCount}  ({s.VisibleLayerCount} visible)");
            if (s.TextLayerCount > 0) AddStat("Editable text layers", $"{s.TextLayerCount}");
            AddStat("Distinct colors", $"{s.UniqueColors:N0}");
            AddStat("In memory", ImageStatistics.FormatBytes(s.MemoryBytes));

            if (s.ColoredPixels > 0)
            {
                AddStat("Mean R / G / B", $"{s.MeanR:0.#} / {s.MeanG:0.#} / {s.MeanB:0.#}");
                AddStat("Mean brightness", $"{s.MeanLum:0.#}");
                AddStat("Median brightness", $"{s.MedianLum}");
                AddStat("Brightness range", $"{s.MinLum} - {s.MaxLum}");
                AddStat("Std deviation", $"{s.StdDevLum:0.#}");
            }

            DrawCoverage();
            DrawTopColors();
            DrawHistogram();
        }

        private void AddStat(string label, string value)
        {
            int row = StatsGrid.RowDefinitions.Count;
            StatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 1, 8, 1),
                Foreground = (Brush)FindResource("PsTextDim")
            };
            Grid.SetRow(name, row);
            StatsGrid.Children.Add(name);

            var val = new TextBlock { Text = value, Margin = new Thickness(0, 1, 0, 1) };
            Grid.SetRow(val, row);
            Grid.SetColumn(val, 1);
            StatsGrid.Children.Add(val);
        }

        /// <summary>Opaque / semi-transparent / empty, as one proportional bar. A picture that
        /// looks finished but is 60% empty is worth being able to see at a glance.</summary>
        private void DrawCoverage()
        {
            var s = _stats;
            CoverageCanvas.Loaded += (_, __) => LayoutCoverage();
            CoverageCanvas.SizeChanged += (_, __) => LayoutCoverage();

            CoverageText.Text =
                $"Opaque {Pct(s.OpaquePixels)}   ·   Semi-transparent {Pct(s.PartialPixels)}   ·   " +
                $"Empty {Pct(s.TransparentPixels)}";

            string Pct(long n) => s.PixelCount == 0 ? "0%" : $"{100.0 * n / s.PixelCount:0.#}%";
        }

        private void LayoutCoverage()
        {
            var s = _stats;
            CoverageCanvas.Children.Clear();
            double w = CoverageCanvas.ActualWidth, h = CoverageCanvas.ActualHeight;
            if (w <= 0 || h <= 0 || s.PixelCount == 0) return;

            double x = 0;
            foreach (var (count, brush) in new (long, Brush)[]
            {
                (s.OpaquePixels, (Brush)FindResource("PsAccent")),
                (s.PartialPixels, (Brush)FindResource("PsAccentSoft")),
                (s.TransparentPixels, (Brush)FindResource("XpFaceDark")),
            })
            {
                double seg = w * count / s.PixelCount;
                if (seg <= 0) continue;
                var r = new Rectangle { Width = seg, Height = h, Fill = brush };
                Canvas.SetLeft(r, x);
                CoverageCanvas.Children.Add(r);
                x += seg;
            }
        }

        private void DrawTopColors()
        {
            TopColorsPanel.Children.Clear();
            if (_stats.TopColors.Count == 0)
            {
                TopColorsPanel.Children.Add(new TextBlock
                {
                    Text = "Nothing is painted yet.",
                    Foreground = (Brush)FindResource("PsTextDim")
                });
                return;
            }

            double max = _stats.TopColors[0].Percent;
            foreach (var (color, percent) in _stats.TopColors)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

                var chip = new Border
                {
                    Width = 16,
                    Height = 14,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(color),
                    BorderBrush = (Brush)FindResource("XpBorder"),
                    BorderThickness = new Thickness(1)
                };
                row.Children.Add(chip);

                var hex = new TextBlock
                {
                    Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(hex, 1);
                row.Children.Add(hex);

                // Scaled against the largest share rather than against 100%, so the smaller
                // entries are still comparable to each other instead of all reading as zero.
                var barHost = new Border
                {
                    Height = 12,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = (Brush)FindResource("XpFaceDark"),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var bar = new Rectangle
                {
                    Fill = (Brush)FindResource("PsAccent"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0
                };
                barHost.Child = bar;
                barHost.SizeChanged += (_, e) => bar.Width = Math.Max(1, e.NewSize.Width * percent / max);
                Grid.SetColumn(barHost, 2);
                row.Children.Add(barHost);

                var pct = new TextBlock
                {
                    Text = $"{percent:0.#}%",
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(pct, 3);
                row.Children.Add(pct);

                TopColorsPanel.Children.Add(row);
            }

            TopColorsPanel.Children.Add(new TextBlock
            {
                Text = "Near-identical shades are counted together, so these are groups rather than " +
                       "exact values - the exact count is under Image below.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("PsTextDim")
            });
        }

        private void Channel_Changed(object sender, RoutedEventArgs e)
        {
            if (_stats != null && HistogramCanvas != null) DrawHistogram();
        }

        private void DrawHistogram()
        {
            HistogramCanvas.Children.Clear();
            double w = HistogramCanvas.Width, h = HistogramCanvas.Height;
            var s = _stats;

            if (s.ColoredPixels == 0)
            {
                HistSummary.Text = "Nothing is painted yet, so there is nothing to plot.";
                return;
            }

            var series = new List<(int[] Bins, Color Color)>();
            if (ChanRgb.IsChecked == true) { DrawRgbHistogram(w, h); return; }
            else if (ChanR.IsChecked == true) series.Add((s.HistR, Color.FromArgb(200, 232, 76, 76)));
            else if (ChanG.IsChecked == true) series.Add((s.HistG, Color.FromArgb(200, 86, 196, 96)));
            else if (ChanB.IsChecked == true) series.Add((s.HistB, Color.FromArgb(200, 84, 132, 236)));
            else
            {
                var g = ((SolidColorBrush)FindResource("PsIconGlyph")).Color;
                series.Add((s.HistLum, Color.FromArgb(190, g.R, g.G, g.B)));
            }

            // One shared vertical scale across whichever series are showing, or the channels would
            // not be comparable to each other - the whole point of the RGB view.
            int peak = 1;
            foreach (var (bins, _) in series)
                foreach (int v in bins)
                    if (v > peak) peak = v;

            // The top bin of a flat-colour picture dwarfs everything else by orders of magnitude,
            // which flattens every other bin to nothing. A log scale keeps the shape readable
            // without discarding the tall bin.
            double Scale(int v) => Math.Log(1 + v) / Math.Log(1 + peak);

            foreach (var (bins, color) in series)
            {
                var pts = new PointCollection { new Point(0, h) };
                for (int i = 0; i < 256; i++)
                    pts.Add(new Point(i * w / 255.0, h - Scale(bins[i]) * (h - 4)));
                pts.Add(new Point(w, h));

                HistogramCanvas.Children.Add(new Polygon
                {
                    Points = pts,
                    Fill = new SolidColorBrush(color),
                    Stroke = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B)),
                    StrokeThickness = 0.8
                });
            }

            DrawGuides(w, h);   // quarter-tone marks, so a peak can be placed by eye
            HistSummary.Text = SummaryText();
        }

        private string SummaryText()
        {
            var s = _stats;
            return "Left is black, right is white, height is how many pixels sit at that level " +
                   $"(log scale).  Mean {s.MeanLum:0.#}  ·  median {s.MedianLum}  ·  " +
                   $"range {s.MinLum}-{s.MaxLum}  ·  std dev {s.StdDevLum:0.#}";
        }

        /// <summary>The three channels composited *additively*, one column at a time, rather than
        /// painted as three translucent shapes stacked on top of each other.
        ///
        /// Stacking is what this did first, and it is wrong in the most common case there is: on a
        /// greyscale picture all three channels are identical, so whichever was drawn last covered
        /// the other two completely and the whole chart came out blue. Compositing by hand gives
        /// what Photoshop shows and what the eye expects - grey where all three overlap, yellow /
        /// magenta / cyan where exactly two do, and a pure channel colour where only one reaches.</summary>
        private void DrawRgbHistogram(double w, double h)
        {
            var s = _stats;
            int peak = 1;
            foreach (var bins in new[] { s.HistR, s.HistG, s.HistB })
                foreach (int v in bins)
                    if (v > peak) peak = v;

            for (int i = 0; i < 256; i++)
            {
                // Snapped to whole pixels so neighbouring columns tile exactly. At a fractional
                // width their antialiased edges blended against each other and the filled area
                // came out finely striped, as if the data oscillated when it doesn't.
                double x0 = Math.Round(i * w / 256.0);
                double colW = Math.Max(1, Math.Round((i + 1) * w / 256.0) - x0);

                // Ascending by height, carrying a channel mask: bit 0 red, 1 green, 2 blue.
                Span<(double H, int Mask)> ch = stackalloc (double, int)[3]
                {
                    (Bar(s.HistR[i]), 1), (Bar(s.HistG[i]), 2), (Bar(s.HistB[i]), 4)
                };
                for (int a = 0; a < 3; a++)
                    for (int b = a + 1; b < 3; b++)
                        if (ch[b].H < ch[a].H) (ch[a], ch[b]) = (ch[b], ch[a]);

                // Every channel still standing contributes to the segment below its own height, so
                // the mask sheds one channel at each step up.
                int mask = 1 | 2 | 4;
                double from = 0;
                for (int k = 0; k < 3; k++)
                {
                    double to = ch[k].H;
                    if (to > from) AddColumn(x0, colW, h - to, to - from, Additive(mask));
                    from = Math.Max(from, to);
                    mask &= ~ch[k].Mask;
                }
            }

            DrawGuides(w, h);
            HistSummary.Text = SummaryText();

            double Bar(int v) => Math.Log(1 + v) / Math.Log(1 + peak) * (h - 4);
        }

        private void AddColumn(double x, double width, double y, double height, Color c)
        {
            var r = new Rectangle { Width = width, Height = height, Fill = new SolidColorBrush(c) };
            // Aliased: these are axis-aligned bars on whole-pixel boundaries, so smoothing their
            // vertical edges only softens the joins between adjacent columns.
            RenderOptions.SetEdgeMode(r, EdgeMode.Aliased);
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            HistogramCanvas.Children.Add(r);
        }

        /// <summary>What the given combination of channels looks like when their light is added
        /// together: red+green is yellow, all three is white.</summary>
        private static Color Additive(int mask) => mask switch
        {
            1 => Color.FromRgb(226, 62, 62),        // R
            2 => Color.FromRgb(62, 198, 82),        // G
            4 => Color.FromRgb(70, 122, 235),       // B
            3 => Color.FromRgb(219, 199, 60),       // R+G
            5 => Color.FromRgb(210, 82, 190),       // R+B
            6 => Color.FromRgb(62, 195, 200),       // G+B
            7 => Color.FromRgb(200, 200, 200),      // all three
            _ => Colors.Transparent,
        };

        private void DrawGuides(double w, double h)
        {
            for (int i = 1; i < 4; i++)
            {
                double x = i * w / 4.0;
                HistogramCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = h,
                    Stroke = (Brush)FindResource("XpBorder"),
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 3 }
                });
            }
        }
    }
}
