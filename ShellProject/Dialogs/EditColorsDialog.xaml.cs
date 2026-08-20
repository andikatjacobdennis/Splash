using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaintClone.Services;

namespace PaintClone.Dialogs
{
    public partial class EditColorsDialog : Window
    {
        public Color SelectedColor { get; private set; }
        private static List<Color> CustomColors = CustomColorStore.Load(); // loaded once per app run, persisted to disk
        private static List<Color> RecentColors = RecentColorStore.Load();

        // IMPORTANT: starts true. XAML sets several TextBoxes' initial Text in markup, which fires
        // TextChanged during InitializeComponent() - before every named field on this class is wired
        // up yet. Without defaulting this true, the first such event crashes with a
        // NullReferenceException reading a field that isn't connected yet (this bit us once already
        // with the RGB boxes - same fix applied here for all the new fields too).
        private bool _suppressEvents = true;

        private double _hue, _sat, _lum; // 0-360, 0-1, 0-1
        private WriteableBitmap _hueStripBitmap;

        public EditColorsDialog(Color initial)
        {
            InitializeComponent();

            foreach (var c in ColorManager.ClassicPalette)
                BasicGrid.Children.Add(MakeSwatch(c));
            RefreshCustom();
            RefreshRecent();
            BuildMaterialGrid();
            BuildFlatUIGrid();
            BuildStandardsGrids();

            CurrentSwatch.Background = new SolidColorBrush(initial);
            _hueStripBitmap = BuildHueStripBitmap((int)HueCanvas.Width, (int)HueCanvas.Height);
            HueImage.Source = _hueStripBitmap;

            SetColor(initial, null);
            _suppressEvents = false;
        }

        // ===================================================================
        // Color <-> HSL plumbing
        // ===================================================================

        /// <summary>Single source of truth for "the color changed" - updates every UI element except
        /// the one that triggered the change (so you can type in a box without your cursor/selection
        /// jumping around as it gets reformatted mid-keystroke).</summary>
        private void SetColor(Color c, object source)
        {
            bool wasSuppressed = _suppressEvents;
            _suppressEvents = true;

            SelectedColor = c;
            (_hue, _sat, _lum) = RgbToHsl(c);

            if (source != RBox && source != GBox && source != BBox)
            {
                RBox.Text = c.R.ToString();
                GBox.Text = c.G.ToString();
                BBox.Text = c.B.ToString();
            }
            if (source != HBox && source != SBox && source != LBox)
            {
                HBox.Text = ((int)Math.Round(_hue)).ToString();
                SBox.Text = ((int)Math.Round(_sat * 100)).ToString();
                LBox.Text = ((int)Math.Round(_lum * 100)).ToString();
            }
            if (source != AlphaSlider && source != AlphaBox)
            {
                AlphaSlider.Value = c.A;
                AlphaBox.Text = c.A.ToString();
            }
            if (source != HexBox)
            {
                // Eight-digit #AARRGGBB only when there's actual transparency to express, so the
                // common fully-opaque case still shows the familiar six-digit form.
                HexBox.Text = c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }

            PreviewSwatch.Background = c.A == 0 ? (Brush)FindResource("CheckerboardBrush") : new SolidColorBrush(c);

            if (source != SLCanvas)
            {
                RebuildSLImage();
                PositionSLMarker();
            }
            if (source != HueCanvas)
            {
                PositionHueMarker();
            }

            RebuildShadesRow();

            _suppressEvents = wasSuppressed;
        }

        private void RebuildSLImage()
        {
            SLImage.Source = BuildSLBitmap(_hue, 150);
        }

        private void PositionSLMarker()
        {
            double x = _sat * (SLCanvas.Width - 1);
            double y = (1 - _lum) * (SLCanvas.Height - 1);
            Canvas.SetLeft(SLMarker, x - SLMarker.Width / 2);
            Canvas.SetTop(SLMarker, y - SLMarker.Height / 2);
        }

        private void PositionHueMarker()
        {
            double y = (_hue / 360.0) * (HueCanvas.Height - HueMarker.Height);
            Canvas.SetLeft(HueMarker, 0);
            Canvas.SetTop(HueMarker, y);
        }

        // ===================================================================
        // Spectrum picker mouse handling
        // ===================================================================

        private void SLCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SLCanvas.CaptureMouse();
            UpdateFromSLPoint(e.GetPosition(SLCanvas));
        }

        private void SLCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !SLCanvas.IsMouseCaptured) return;
            UpdateFromSLPoint(e.GetPosition(SLCanvas));
        }

        private void UpdateFromSLPoint(Point p)
        {
            double s = Clamp01(p.X / (SLCanvas.Width - 1));
            double l = Clamp01(1 - p.Y / (SLCanvas.Height - 1));
            var c = HslToRgb(_hue, s, l);
            SetColor(c, SLCanvas);
        }

        private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            HueCanvas.CaptureMouse();
            UpdateFromHuePoint(e.GetPosition(HueCanvas));
        }

        private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !HueCanvas.IsMouseCaptured) return;
            UpdateFromHuePoint(e.GetPosition(HueCanvas));
        }

        private void UpdateFromHuePoint(Point p)
        {
            double hue = Clamp01(p.Y / HueCanvas.Height) * 360.0;
            var c = HslToRgb(hue, _sat, _lum);
            SetColor(c, HueCanvas);
        }

        private void Picker_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (SLCanvas.IsMouseCaptured) SLCanvas.ReleaseMouseCapture();
            if (HueCanvas.IsMouseCaptured) HueCanvas.ReleaseMouseCapture();
        }

        private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

        // ===================================================================
        // Numeric field editing
        // ===================================================================

        private void RgbChannel_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (RBox == null || GBox == null || BBox == null) return;
            byte r = ParseByte(RBox.Text), g = ParseByte(GBox.Text), b = ParseByte(BBox.Text);
            SetColor(Color.FromRgb(r, g, b), sender);
        }

        private void HslField_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (HBox == null || SBox == null || LBox == null) return;
            double h = ParseDouble(HBox.Text, 0, 360);
            double s = ParseDouble(SBox.Text, 0, 100) / 100.0;
            double l = ParseDouble(LBox.Text, 0, 100) / 100.0;
            SetColor(HslToRgb(h, s, l), sender);
        }

        private void HexBox_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (HexBox == null) return;
            var text = HexBox.Text.Trim().TrimStart('#');
            // Eight digits carry an alpha value up front (#AARRGGBB); six is the familiar opaque form.
            if (text.Length == 8 &&
                byte.TryParse(text.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var a8) &&
                byte.TryParse(text.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var r8) &&
                byte.TryParse(text.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var g8) &&
                byte.TryParse(text.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out var b8))
            {
                SetColor(Color.FromArgb(a8, r8, g8, b8), HexBox);
                return;
            }
            if (text.Length == 6 &&
                byte.TryParse(text.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(text.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(text.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                SetColor(Color.FromArgb(SelectedColor.A, r, g, b), HexBox);
            }
        }

        private static byte ParseByte(string s) => byte.TryParse(s, out var v) ? v : (byte)0;
        private static double ParseDouble(string s, double min, double max) =>
            double.TryParse(s, out var v) ? Math.Max(min, Math.Min(max, v)) : min;

        // ===================================================================
        // Material Design colors
        // ===================================================================

        private void BuildMaterialGrid()
        {
            MaterialGrid.Children.Clear();
            foreach (var family in MaterialColors.Families)
            {
                var swatch = new Border
                {
                    Width = 30,
                    Height = 30,
                    Margin = new Thickness(2),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(family.Primary),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{family.Name}\n{ColorManager.DescribeColor(family.Primary)}"
                };
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    SetColor(family.Primary, null);
                    ShowMaterialShades(family);
                };
                MaterialGrid.Children.Add(swatch);
            }
        }

        /// <summary>The authentic 50-900 tonal range for one Material color family - distinct from
        /// the generic HSL-interpolated Shades row, since Material's tones aren't simple lightness
        /// steps of a single hue/saturation (each step has its own hand-tuned hue/saturation too).</summary>
        private void ShowMaterialShades(MaterialColors.Family family)
        {
            MaterialShadeLabel.Visibility = Visibility.Visible;
            MaterialShadeLabel.Text = $"{family.Name} shades:";
            MaterialShadeRow.Children.Clear();
            for (int i = 0; i < family.Shades.Length; i++)
            {
                var c = family.Shades[i];
                var sw = new Border
                {
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(c),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0.5),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{family.Name} {MaterialColors.Weights[i]}\n{ColorManager.DescribeColor(c)}"
                };
                sw.MouseLeftButtonDown += (s, e) => SetColor(c, null);
                MaterialShadeRow.Children.Add(sw);
            }
        }

        // ===================================================================
        // Flat UI colors
        // ===================================================================

        private void BuildFlatUIGrid()
        {
            FlatUIGrid.Children.Clear();
            foreach (var s in FlatUIColors.Colors)
            {
                var swatch = new Border
                {
                    Width = 30,
                    Height = 30,
                    Margin = new Thickness(2),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(s.Color),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{s.Name}\n{ColorManager.DescribeColor(s.Color)}"
                };
                swatch.MouseLeftButtonDown += (o, e) => SetColor(s.Color, null);
                FlatUIGrid.Children.Add(swatch);
            }
            foreach (var s in FlatUIColors.Extended)
                AddNamedSwatch(FlatUIExtendedGrid, s.Color, s.Name);
        }

        // ===================================================================
        // Published standards (RAL, CSS named colours)
        // ===================================================================

        /// <summary>Builds the Standards tab as a stack of collapsible sections. There are now
        /// seven sets totalling several hundred swatches, which is far too much to show at once -
        /// each section is an Expander so you can open just the one you need. Only the first is
        /// expanded initially, so the tab opens tidy rather than as a wall of colour.</summary>
        private void BuildStandardsGrids()
        {
            var sections = new (string Title, System.Collections.Generic.List<IndustrialColors.NamedColor> Colors, int Columns, string Note)[]
            {
                ("RAL Classic", IndustrialColors.RalClassic, 6,
                    "Published European standard. RAL is defined for physical paint, so these are approximate on-screen matches."),
                ("Artists' pigments", IndustrialColors.ArtistPigments, 6,
                    "Historic pigment names in common use, with the usual on-screen approximations."),
                ("Safety / hazard (ANSI Z535, ISO 3864 style)", IndustrialColors.SafetyColors, 5,
                    "On-screen conventions for signage and equipment markings, not certified ink specifications."),
                ("Retro hardware (CGA / EGA 16)", IndustrialColors.RetroHardware16, 8,
                    "The fully documented 16-colour PC display palette."),
                ("Neutral ramp (5% steps)", IndustrialColors.GrayRamp, 7,
                    "An even tonal ramp for shading and mockups."),
                ("CSS / X11 named colors", IndustrialColors.WebNamed, 6,
                    "The named colours from the CSS standard - these match their HTML names exactly."),
                ("Web-safe 216", IndustrialColors.WebSafe216, 12,
                    "Every combination of 00/33/66/99/CC/FF - an evenly spaced sampling of the RGB cube."),
            };

            bool first = true;
            foreach (var section in sections)
            {
                var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = section.Columns };
                foreach (var n in section.Colors)
                    AddNamedSwatch(grid, n.Color, string.IsNullOrEmpty(n.Code) ? n.Name : $"{n.Code} - {n.Name}");

                var body = new StackPanel { Margin = new Thickness(2, 4, 2, 8) };
                body.Children.Add(grid);
                body.Children.Add(new TextBlock
                {
                    Text = section.Note,
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });

                StandardsPanel.Children.Add(new Expander
                {
                    Header = $"{section.Title}  ({section.Colors.Count})",
                    IsExpanded = first,
                    Content = body,
                    Margin = new Thickness(0, 0, 0, 2)
                });
                first = false;
            }

            StandardsPanel.Children.Add(new TextBlock
            {
                Text = "PANTONE is a licensed, proprietary system, so accurate values can't be bundled here - "
                     + "guessed approximations would defeat the purpose of a spot-colour system. Use a licensed "
                     + "Pantone tool and enter the value in the RGB or Hex box.",
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 2, 0)
            });
        }

        private void AddNamedSwatch(System.Windows.Controls.Primitives.UniformGrid grid, Color color, string label)
        {
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(color),
                Cursor = Cursors.Hand,
                ToolTip = $"{label}\n{ColorManager.DescribeColor(color)}"
            };
            swatch.MouseLeftButtonDown += (o, e) => SetColor(color, null);
            grid.Children.Add(swatch);
        }

        // ===================================================================
        // Quick actions
        // ===================================================================

        private void CopyHex_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(HexBox.Text); } catch { /* clipboard can be locked by another app - non-fatal */ }
        }

        private void Complementary_Click(object sender, RoutedEventArgs e)
        {
            SetColor(HslToRgb(_hue + 180, _sat, _lum), null);
        }

        private static readonly Random Rng = new();

        private void Random_Click(object sender, RoutedEventArgs e)
        {
            var c = Color.FromRgb((byte)Rng.Next(256), (byte)Rng.Next(256), (byte)Rng.Next(256));
            SetColor(c, null);
        }

        private void Transparent_Click(object sender, RoutedEventArgs e)
        {
            SetColor(Color.FromArgb(0, SelectedColor.R, SelectedColor.G, SelectedColor.B), null);
        }

        // ===================================================================
        // Basic / custom color swatches
        // ===================================================================

        private Border MakeSwatch(Color c, List<Color> removableFrom = null)
        {
            var b = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(2),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                // A partly transparent swatch is painted over the checkerboard, so its opacity is
                // visible at a glance rather than looking like a slightly different solid colour.
                Background = c.A == 255 ? new SolidColorBrush(c) : null,
                Cursor = Cursors.Hand,
                ToolTip = removableFrom != null ? $"Left-click to use, right-click to remove\n{ColorManager.DescribeColor(c)}" : ColorManager.DescribeColor(c)
            };
            if (c.A != 255)
            {
                var checker = new Border
                {
                    Background = (Brush)FindResource("CheckerboardBrush"),
                    Child = new Border { Background = new SolidColorBrush(c) }
                };
                b.Child = checker;
            }
            b.MouseLeftButtonDown += (s, e) => SetColor(c, null);
            if (removableFrom != null)
            {
                b.MouseRightButtonDown += (s, e) =>
                {
                    removableFrom.RemoveAll(x => x.R == c.R && x.G == c.G && x.B == c.B);
                    if (removableFrom == CustomColors) { RefreshCustom(); CustomColorStore.Save(CustomColors); }
                    else { RefreshRecent(); RecentColorStore.Save(RecentColors); }
                };
            }
            return b;
        }

        private void AlphaSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressEvents) return;
            byte a = (byte)Math.Round(AlphaSlider.Value);
            SetColor(Color.FromArgb(a, SelectedColor.R, SelectedColor.G, SelectedColor.B), AlphaSlider);
        }

        private void AlphaBox_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (!byte.TryParse(AlphaBox.Text, out byte a)) return;
            SetColor(Color.FromArgb(a, SelectedColor.R, SelectedColor.G, SelectedColor.B), AlphaBox);
        }

        private void RefreshCustom()
        {
            CustomGrid.Children.Clear();
            foreach (var c in CustomColors) CustomGrid.Children.Add(MakeSwatch(c, CustomColors));
            // Pad out to a fixed tray of slots so the grid stays a predictable shape instead of
            // reflowing every time a colour is added or removed.
            for (int i = CustomColors.Count; i < CustomColorStore.Capacity; i++)
            {
                CustomGrid.Children.Add(new Border
                {
                    Margin = new Thickness(2),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                    ToolTip = "Empty slot - pick a color and click \"Add to Custom Colors\""
                });
            }
        }

        private void RefreshRecent()
        {
            RecentGrid.Children.Clear();
            foreach (var c in RecentColors) RecentGrid.Children.Add(MakeSwatch(c, RecentColors));
        }

        private void AddCustom_Click(object sender, RoutedEventArgs e)
        {
            CustomColors.Add(SelectedColor);
            if (CustomColors.Count > 16) CustomColors.RemoveAt(0);
            RefreshCustom();
            CustomColorStore.Save(CustomColors);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            RecentColorStore.Push(RecentColors, SelectedColor);
            DialogResult = true;
        }

        private void Grayscale_Click(object sender, RoutedEventArgs e)
        {
            var c = SelectedColor;
            byte g = (byte)Math.Round(0.3 * c.R + 0.59 * c.G + 0.11 * c.B);
            SetColor(Color.FromRgb(g, g, g), null);
        }

        private void Invert_Click(object sender, RoutedEventArgs e)
        {
            var c = SelectedColor;
            SetColor(Color.FromRgb((byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B)), null);
        }

        /// <summary>A row of lightness variations of the current hue/saturation - quick tint/shade picks.</summary>
        private void RebuildShadesRow()
        {
            if (ShadesPanel == null) return;
            ShadesPanel.Children.Clear();
            const int count = 9;
            for (int i = 0; i < count; i++)
            {
                double l = 0.08 + i * (0.86 / (count - 1));
                var c = HslToRgb(_hue, _sat, l);
                var sw = new Border
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(c),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0.5),
                    Cursor = Cursors.Hand,
                    ToolTip = ColorManager.DescribeColor(c)
                };
                sw.MouseLeftButtonDown += (s, e) => SetColor(c, null);
                ShadesPanel.Children.Add(sw);
            }
        }

        // ===================================================================
        // RGB <-> HSL conversion and spectrum bitmap generation
        // ===================================================================

        private static (double h, double s, double l) RgbToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double h = 0, s, l = (max + min) / 2;
            double d = max - min;
            if (d < 0.00001)
            {
                s = 0;
            }
            else
            {
                s = l < 0.5 ? d / (max + min) : d / (2 - max - min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h *= 60;
            }
            return (h, s, l);
        }

        private static Color HslToRgb(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            double r, g, b;
            if (s <= 0.00001)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h / 360.0 + 1.0 / 3);
                g = HueToRgb(p, q, h / 360.0);
                b = HueToRgb(p, q, h / 360.0 - 1.0 / 3);
            }
            return Color.FromRgb(
                (byte)Math.Round(Math.Max(0, Math.Min(1, r)) * 255),
                (byte)Math.Round(Math.Max(0, Math.Min(1, g)) * 255),
                (byte)Math.Round(Math.Max(0, Math.Min(1, b)) * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        /// <summary>Saturation (x-axis) / Luminosity (y-axis, light at top) square at a fixed hue.</summary>
        private static unsafe WriteableBitmap BuildSLBitmap(double hue, int size)
        {
            var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgr32, null);
            wb.Lock();
            int stride = wb.BackBufferStride;
            byte* buffer = (byte*)wb.BackBuffer.ToPointer();
            for (int y = 0; y < size; y++)
            {
                double l = 1.0 - (double)y / (size - 1);
                for (int x = 0; x < size; x++)
                {
                    double s = (double)x / (size - 1);
                    var c = HslToRgb(hue, s, l);
                    int* p = (int*)(buffer + y * stride + x * 4);
                    *p = (c.R << 16) | (c.G << 8) | c.B;
                }
            }
            wb.AddDirtyRect(new Int32Rect(0, 0, size, size));
            wb.Unlock();
            return wb;
        }

        /// <summary>Vertical rainbow strip, one full hue rotation top to bottom.</summary>
        private static unsafe WriteableBitmap BuildHueStripBitmap(int width, int height)
        {
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            wb.Lock();
            int stride = wb.BackBufferStride;
            byte* buffer = (byte*)wb.BackBuffer.ToPointer();
            for (int y = 0; y < height; y++)
            {
                double hue = 360.0 * y / (height - 1);
                var c = HslToRgb(hue, 1.0, 0.5);
                int packed = (c.R << 16) | (c.G << 8) | c.B;
                for (int x = 0; x < width; x++)
                {
                    int* p = (int*)(buffer + y * stride + x * 4);
                    *p = packed;
                }
            }
            wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
            wb.Unlock();
            return wb;
        }
    }
}
