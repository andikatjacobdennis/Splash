using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PaintClone.Services
{
    /// <summary>
    /// The toolbox's icons, drawn as vector geometry in a single theme-following colour rather than
    /// loaded from the colour flat-art PNGs this app used previously.
    ///
    /// Those PNGs were drawn assuming a light backdrop (a dark navy outline around a pale fill), so
    /// once the toolbox could be either a dark or a light panel, a different half of every icon went
    /// illegible depending on the active theme - previously worked around by giving each icon its own
    /// constant near-white "chip" to sit on, which looked nothing like a real creative tool's
    /// toolbar. Monochrome vector glyphs solve that properly: one colour, taken from the theme
    /// (<c>PsIconGlyph</c>) via a DynamicResource reference so it re-paints live on a theme switch,
    /// legible on either panel with no chip needed, and crisp at any DPI or button size.
    /// </summary>
    public static class ToolIcons
    {
        /// <summary>One drawn element of an icon. Most icons are a single stroked outline; a few
        /// combine several parts (e.g. the paint bucket's stroked pail plus its filled drip).</summary>
        private sealed class Part
        {
            public string Data;
            public bool Filled;
            public double Thickness = 1.7;
            public double[] Dash;
            /// <summary>Fill this part with a fade from the glyph colour to transparent instead of
            /// a solid - used only by the Gradient tool's icon, where a gradient IS the subject.</summary>
            public bool GradientFill;
        }

        // All geometry is authored in a 24x24 box and scaled to fit the button by the Viewbox in
        // Create below, so tweaking the icon size never means touching any of these numbers.
        private static readonly Dictionary<string, Part[]> Icons = new()
        {
            ["Select"] = new[]
            {
                new Part { Data = "M3.5,5.5 H20.5 V18.5 H3.5 Z", Dash = new[] { 2.2, 1.8 }, Thickness = 1.6 },
            },

            ["FreeFormSelect"] = new[]
            {
                new Part
                {
                    Data = "M12,4.5 C6.5,4.5 3.5,8 3.5,11.2 C3.5,14.8 7.3,17.6 12,17.6 " +
                           "C14.6,17.6 17,17 18.6,15.6 C19.8,17.4 19.2,19.8 17.2,20.5",
                    Dash = new[] { 2.2, 1.8 }, Thickness = 1.6
                },
            },

            ["Pencil"] = new[]
            {
                new Part { Data = "M4,20 L7.8,19 L19.3,7.5 L16.5,4.7 L5,16.2 Z" },
                new Part { Data = "M15,6.2 L17.8,9", Thickness = 1.4 },
                new Part { Data = "M4,20 L5,16.2", Thickness = 1.4 },
            },

            ["Brush"] = new[]
            {
                new Part { Data = "M16.8,3.6 L20.4,7.2 L11.4,16.2 C10.2,17.4 8,18 5.6,18.4 C6,16 6.6,13.8 7.8,12.6 Z" },
                new Part { Data = "M14.6,5.8 L18.2,9.4", Thickness = 1.4 },
            },

            ["Airbrush"] = new[]
            {
                // Spray can: body, nozzle, and a scatter of droplets leaving it.
                new Part { Data = "M6.5,10.5 H13.5 V19.5 A1.5,1.5 0 0 1 12,21 H8 A1.5,1.5 0 0 1 6.5,19.5 Z" },
                new Part { Data = "M8.5,10.5 V7.5 H12 V10.5", Thickness = 1.5 },
                new Part { Data = "M6.5,13.8 H13.5", Thickness = 1.4 },
                new Part { Data = "M15.6,4.6 a1,1 0 1,0 0.01,0 Z", Filled = true },
                new Part { Data = "M19,3 a0.85,0.85 0 1,0 0.01,0 Z", Filled = true },
                new Part { Data = "M18.4,7 a0.85,0.85 0 1,0 0.01,0 Z", Filled = true },
                new Part { Data = "M21.4,6.2 a0.7,0.7 0 1,0 0.01,0 Z", Filled = true },
                new Part { Data = "M16.2,9.4 a0.7,0.7 0 1,0 0.01,0 Z", Filled = true },
            },

            ["Eraser"] = new[]
            {
                new Part { Data = "M3,16.4 L11.4,8 A2,2 0 0 1 14.2,8 L20,13.8 A2,2 0 0 1 20,16.6 L16.4,20.2 H6.6 Z" },
                new Part { Data = "M7.8,11.6 L16.4,20.2", Thickness = 1.4 },
            },

            ["Fill"] = new[]
            {
                // Tilted pail with a handle, plus a falling drip - the classic paint-bucket read.
                new Part { Data = "M4.2,12.6 L10.8,6 A1.4,1.4 0 0 1 12.8,6 L18.6,11.8 A1.4,1.4 0 0 1 18.6,13.8 L12.6,19.8 A1.4,1.4 0 0 1 10.6,19.8 Z" },
                new Part { Data = "M8,9.6 A3.6,3.6 0 0 1 14.6,9.6", Thickness = 1.4 },
                new Part { Data = "M20.4,14.4 C20.4,14.4 22.6,17.2 22.6,18.6 A2.2,2.2 0 0 1 18.2,18.6 C18.2,17.2 20.4,14.4 20.4,14.4 Z", Filled = true },
            },

            ["Pick"] = new[]
            {
                new Part { Data = "M16.8,3.2 L20.8,7.2 L14,14 L10,10 Z" },
                new Part { Data = "M10.6,10.6 L4.8,16.4 C3.8,17.4 3.8,19 4.8,20 C5.8,21 7.4,21 8.4,20 L14.2,14.2", Thickness = 1.6 },
            },

            ["Magnifier"] = new[]
            {
                new Part { Data = "M10.5,10.5 m-6.5,0 a6.5,6.5 0 1,0 13,0 a6.5,6.5 0 1,0 -13,0 Z" },
                new Part { Data = "M15.4,15.4 L20.5,20.5", Thickness = 2 },
            },

            ["MagicWand"] = new[]
            {
                // Wand shaft with a brighter tip, plus sparkles - Photoshop's own read for this tool.
                new Part { Data = "M3.6,20.4 L14.2,9.8", Thickness = 2 },
                new Part { Data = "M13.4,9 L16.6,5.8 L18.2,7.4 L15,10.6 Z", Filled = true },
                new Part { Data = "M18.6,2 L19.3,3.9 L21.2,4.6 L19.3,5.3 L18.6,7.2 L17.9,5.3 L16,4.6 L17.9,3.9 Z", Filled = true },
                new Part { Data = "M21.4,9 L21.9,10.2 L23.1,10.7 L21.9,11.2 L21.4,12.4 L20.9,11.2 L19.7,10.7 L20.9,10.2 Z", Filled = true },
                new Part { Data = "M13.6,2.4 L14,3.5 L15.1,3.9 L14,4.3 L13.6,5.4 L13.2,4.3 L12.1,3.9 L13.2,3.5 Z", Filled = true },
            },

            ["Text"] = new[]
            {
                new Part { Data = "M4.5,6.5 V4.5 H19.5 V6.5", Thickness = 1.8 },
                new Part { Data = "M12,4.5 V19.5", Thickness = 1.8 },
                new Part { Data = "M8.5,19.5 H15.5", Thickness = 1.8 },
            },

            ["Line"] = new[]
            {
                new Part { Data = "M4,20 L20,4", Thickness = 1.8 },
            },

            ["Curve"] = new[]
            {
                new Part { Data = "M3,18.5 C6.5,6.5 17.5,6.5 21,18.5", Thickness = 1.8 },
            },

            ["Rectangle"] = new[]
            {
                new Part { Data = "M3.5,6 H20.5 V18 H3.5 Z" },
            },

            ["RoundedRectangle"] = new[]
            {
                new Part { Data = "M6.5,6 H17.5 A3,3 0 0 1 20.5,9 V15 A3,3 0 0 1 17.5,18 H6.5 A3,3 0 0 1 3.5,15 V9 A3,3 0 0 1 6.5,6 Z" },
            },

            ["Ellipse"] = new[]
            {
                new Part { Data = "M12,12 m-8.5,0 a8.5,6 0 1,0 17,0 a8.5,6 0 1,0 -17,0 Z" },
            },

            ["Polygon"] = new[]
            {
                new Part { Data = "M12,3.5 L20.5,9.7 L17.3,19.7 L6.7,19.7 L3.5,9.7 Z" },
            },

            ["Star"] = new[]
            {
                new Part { Data = "M12,2.8 L14.5,9.4 L21.5,9.8 L16.1,14.2 L17.9,21 L12,17.2 L6.1,21 L7.9,14.2 L2.5,9.8 L9.5,9.4 Z" },
            },

            ["Arrow"] = new[]
            {
                new Part { Data = "M4,20 L19,5", Thickness = 1.8 },
                new Part { Data = "M11.5,4.6 L19.6,4.4 L19.4,12.5", Thickness = 1.8 },
            },

            // The rest of the shape family that shares the Star slot's flyout.
            ["Triangle"] = new[] { new Part { Data = "M12,3.5 L20.8,19.5 H3.2 Z" } },
            ["Diamond"] = new[] { new Part { Data = "M12,3 L20.5,12 L12,21 L3.5,12 Z" } },
            ["Pentagon"] = new[] { new Part { Data = "M12,3.2 L20.6,9.4 L17.3,19.6 H6.7 L3.4,9.4 Z" } },
            ["Hexagon"] = new[] { new Part { Data = "M12,3 L20,7.5 V16.5 L12,21 L4,16.5 V7.5 Z" } },
            ["Octagon"] = new[] { new Part { Data = "M8.4,3.5 H15.6 L20.5,8.4 V15.6 L15.6,20.5 H8.4 L3.5,15.6 V8.4 Z" } },
            ["Cross"] = new[] { new Part { Data = "M9.2,3.4 H14.8 V9.2 H20.6 V14.8 H14.8 V20.6 H9.2 V14.8 H3.4 V9.2 H9.2 Z" } },
            ["Heart"] = new[]
            {
                new Part { Data = "M12,20.6 C5,15.6 3,11.9 3,9.1 A4.6,4.6 0 0 1 12,7.3 A4.6,4.6 0 0 1 21,9.1 C21,11.9 19,15.6 12,20.6 Z" },
            },
            ["Lightning"] = new[] { new Part { Data = "M13.6,2.4 L4.8,13.2 H10.4 L8.9,21.6 L19.2,10.2 H13.1 Z" } },

            ["Gradient"] = new[]
            {
                new Part { Data = "M3.5,6 H20.5 V18 H3.5 Z", GradientFill = true },
                new Part { Data = "M3.5,6 H20.5 V18 H3.5 Z", Thickness = 1.4 },
            },
        };

        public static bool Has(string toolKey) => Icons.ContainsKey(toolKey);

        /// <summary>Builds the icon for a tool as a live visual. Colours are attached with
        /// SetResourceReference rather than resolved once, so a Dark/Light switch re-paints every
        /// icon already sitting in the toolbox instead of only newly created ones.</summary>
        public static UIElement Create(string toolKey, double size = 20)
        {
            if (!Icons.TryGetValue(toolKey, out var parts)) return null;

            var canvas = new Canvas { Width = 24, Height = 24 };
            foreach (var part in parts)
            {
                var path = new Path
                {
                    Data = Geometry.Parse(part.Data),
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    SnapsToDevicePixels = false
                };

                if (part.GradientFill)
                {
                    // Filled with the ordinary themed glyph brush, then faded out by an opacity
                    // mask. A mask only reads alpha, so this needs no knowledge of the theme's
                    // actual colour - which is the point: the fade tracks a Dark/Light switch for
                    // free, where baking the colour into gradient stops here would freeze it.
                    path.SetResourceReference(Shape.FillProperty, "PsIconGlyph");
                    var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    mask.GradientStops.Add(new GradientStop(Colors.Black, 0));       // opaque end
                    mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1)); // faded end
                    path.OpacityMask = mask;
                }
                else if (part.Filled)
                {
                    path.SetResourceReference(Shape.FillProperty, "PsIconGlyph");
                }
                else
                {
                    path.SetResourceReference(Shape.StrokeProperty, "PsIconGlyph");
                    path.StrokeThickness = part.Thickness;
                    if (part.Dash != null) path.StrokeDashArray = new DoubleCollection(part.Dash);
                }

                canvas.Children.Add(path);
            }

            return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
        }
    }
}
