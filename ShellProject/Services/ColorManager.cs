using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PaintClone.Services
{
    public class ColorManager
    {
        public Color Foreground { get; private set; } = Colors.Black;
        public Color Background { get; private set; } = Colors.Transparent;

        public event EventHandler ColorsChanged;

        public void SetForeground(Color c)
        {
            Foreground = c;
            ColorsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetBackground(Color c)
        {
            Background = c;
            ColorsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SwapColors()
        {
            (Foreground, Background) = (Background, Foreground);
            ColorsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The classic 28-swatch Windows Paint palette (2 rows x 14), same arrangement since Win 3.1/9x/XP.</summary>
        /// <summary>A 100-colour palette laid out as a two-row strip (50 columns), the way a
        /// horizontal colour bar works in most drawing applications. Each column pairs a
        /// darker shade on the top row with its lighter counterpart directly beneath, so the
        /// two rows stay related rather than being an arbitrary run of 100 colours: the first
        /// ten columns are a greyscale ramp (black to mid-grey above, mid-grey to white
        /// below) and the remaining forty sweep the hue wheel.</summary>
        public static readonly List<Color> ClassicPalette = new()
        {
            // Row 1 - greyscale (dark half), then deep hues
            Color.FromRgb(0,0,0), Color.FromRgb(13,13,13), Color.FromRgb(27,27,27), Color.FromRgb(40,40,40), Color.FromRgb(53,53,53),
            Color.FromRgb(67,67,67), Color.FromRgb(80,80,80), Color.FromRgb(93,93,93), Color.FromRgb(107,107,107), Color.FromRgb(120,120,120),
            Color.FromRgb(214,0,0), Color.FromRgb(214,32,0), Color.FromRgb(214,64,0), Color.FromRgb(214,96,0), Color.FromRgb(214,129,0),
            Color.FromRgb(214,161,0), Color.FromRgb(214,193,0), Color.FromRgb(203,214,0), Color.FromRgb(171,214,0), Color.FromRgb(139,214,0),
            Color.FromRgb(107,214,0), Color.FromRgb(75,214,0), Color.FromRgb(43,214,0), Color.FromRgb(11,214,0), Color.FromRgb(0,214,21),
            Color.FromRgb(0,214,54), Color.FromRgb(0,214,86), Color.FromRgb(0,214,118), Color.FromRgb(0,214,150), Color.FromRgb(0,214,182),
            Color.FromRgb(0,214,214), Color.FromRgb(0,182,214), Color.FromRgb(0,150,214), Color.FromRgb(0,118,214), Color.FromRgb(0,86,214),
            Color.FromRgb(0,54,214), Color.FromRgb(0,21,214), Color.FromRgb(11,0,214), Color.FromRgb(43,0,214), Color.FromRgb(75,0,214),
            Color.FromRgb(107,0,214), Color.FromRgb(139,0,214), Color.FromRgb(171,0,214), Color.FromRgb(203,0,214), Color.FromRgb(214,0,193),
            Color.FromRgb(214,0,161), Color.FromRgb(214,0,129), Color.FromRgb(214,0,96), Color.FromRgb(214,0,64), Color.FromRgb(214,0,32),
            // Row 2 - greyscale (light half), then pale hues
            Color.FromRgb(135,135,135), Color.FromRgb(148,148,148), Color.FromRgb(162,162,162), Color.FromRgb(175,175,175), Color.FromRgb(188,188,188),
            Color.FromRgb(202,202,202), Color.FromRgb(215,215,215), Color.FromRgb(228,228,228), Color.FromRgb(242,242,242), Color.FromRgb(255,255,255),
            Color.FromRgb(251,96,96), Color.FromRgb(251,119,96), Color.FromRgb(251,142,96), Color.FromRgb(251,166,96), Color.FromRgb(251,189,96),
            Color.FromRgb(251,212,96), Color.FromRgb(251,235,96), Color.FromRgb(243,251,96), Color.FromRgb(220,251,96), Color.FromRgb(197,251,96),
            Color.FromRgb(173,251,96), Color.FromRgb(150,251,96), Color.FromRgb(127,251,96), Color.FromRgb(104,251,96), Color.FromRgb(96,251,111),
            Color.FromRgb(96,251,135), Color.FromRgb(96,251,158), Color.FromRgb(96,251,181), Color.FromRgb(96,251,204), Color.FromRgb(96,251,228),
            Color.FromRgb(96,251,251), Color.FromRgb(96,228,251), Color.FromRgb(96,204,251), Color.FromRgb(96,181,251), Color.FromRgb(96,158,251),
            Color.FromRgb(96,135,251), Color.FromRgb(96,111,251), Color.FromRgb(104,96,251), Color.FromRgb(127,96,251), Color.FromRgb(150,96,251),
            Color.FromRgb(173,96,251), Color.FromRgb(197,96,251), Color.FromRgb(220,96,251), Color.FromRgb(243,96,251), Color.FromRgb(251,96,235),
            Color.FromRgb(251,96,212), Color.FromRgb(251,96,189), Color.FromRgb(251,96,166), Color.FromRgb(251,96,142), Color.FromRgb(251,96,119),
        };

        /// <summary>Formats a color's hex, RGB, and HSL values for a hover tooltip - used on every
        /// palette swatch across the app (classic palette, Material, custom, recent, shades).</summary>
        public static string DescribeColor(Color c)
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
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}\n" +
                   $"RGB: {c.R}, {c.G}, {c.B}\n" +
                   $"HSL: {(int)Math.Round(h)}\u00b0, {(int)Math.Round(s * 100)}%, {(int)Math.Round(l * 100)}%";
        }
    }
}
