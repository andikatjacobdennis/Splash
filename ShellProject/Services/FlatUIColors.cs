using System.Collections.Generic;
using System.Windows.Media;

namespace PaintClone.Services
{
    /// <summary>The classic "Flat UI Color Palette" - twenty well-known, publicly documented
    /// colors widely used in flat design work.</summary>
    public static class FlatUIColors
    {
        public class Swatch
        {
            public string Name;
            public Color Color;
        }

        public static readonly List<Swatch> Colors = new()
        {
            S("Turquoise", "#1ABC9C"), S("Emerald", "#2ECC71"), S("Peter River", "#3498DB"),
            S("Amethyst", "#9B59B6"), S("Wet Asphalt", "#34495E"), S("Green Sea", "#16A085"),
            S("Nephritis", "#27AE60"), S("Belize Hole", "#2980B9"), S("Wisteria", "#8E44AD"),
            S("Midnight Blue", "#2C3E50"), S("Sunflower", "#F1C40F"), S("Carrot", "#E67E22"),
            S("Alizarin", "#E74C3C"), S("Clouds", "#ECF0F1"), S("Concrete", "#95A5A6"),
            S("Orange", "#F39C12"), S("Pumpkin", "#D35400"), S("Pomegranate", "#C0392B"),
            S("Silver", "#BDC3C7"), S("Asbestos", "#7F8C8D"),
        };

        /// <summary>A second, wider set of flat-design tones - softer pastels and deeper shades
        /// that the original twenty don't cover, for when the classic set is too saturated.</summary>
        public static readonly List<Swatch> Extended = new()
        {
            S("Pale Rose", "#FFCCCC"), S("Apricot", "#FFD3B6"), S("Butter", "#FFF3B0"),
            S("Mint Cream", "#C8F7DC"), S("Sky Wash", "#CDE7FF"), S("Lilac Mist", "#E0D7FF"),
            S("Dusty Rose", "#D98880"), S("Sand", "#E5C89A"), S("Sage", "#A9C5A0"),
            S("Seafoam", "#7FCDBB"), S("Steel", "#7F9EB2"), S("Mauve", "#B39DDB"),
            S("Brick", "#A93226"), S("Ochre", "#B9770E"), S("Moss", "#5D6D3F"),
            S("Deep Teal", "#0E6655"), S("Navy", "#1A5276"), S("Aubergine", "#5B2C6F"),
            S("Charcoal", "#3B3B3B"), S("Slate", "#566573"), S("Ash", "#909497"),
            S("Bone", "#F2F1EF"), S("Espresso", "#3E2723"), S("Ink", "#17202A"),
        };

        private static Swatch S(string name, string hex) =>
            new Swatch { Name = name, Color = (Color)ColorConverter.ConvertFromString(hex) };
    }
}
