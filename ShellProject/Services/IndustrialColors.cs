using System.Collections.Generic;
using System.Windows.Media;

namespace PaintClone.Services
{
    /// <summary>
    /// Colour sets from published industrial and web standards.
    ///
    /// A note on Pantone specifically: PANTONE is a trademark and the Matching System's colour
    /// definitions are proprietary and licensed, not public data - there is no legitimate way to
    /// ship accurate PANTONE values in a project like this, and shipping guessed sRGB
    /// approximations under those names would be worse than useless, because the entire point of a
    /// spot-colour system is that the value is exact and reproducible. What's offered instead is
    /// RAL Classic, a genuinely published European standard whose sRGB approximations are widely
    /// documented, plus the CSS/X11 named colours. If you need true PANTONE work, pick the colours
    /// from a licensed Pantone tool and enter the values here via the RGB or hex boxes.
    /// </summary>
    public static class IndustrialColors
    {
        public class NamedColor
        {
            public string Code;
            public string Name;
            public Color Color;
        }

        private static NamedColor C(string code, string name, string hex) => new()
        {
            Code = code,
            Name = name,
            Color = (Color)ColorConverter.ConvertFromString(hex)
        };

        /// <summary>A representative selection across the RAL Classic range (yellows/beiges,
        /// oranges, reds, violets, blues, greens, greys, browns, and whites/blacks), using the
        /// commonly published sRGB approximations. RAL is defined for physical paint, so these are
        /// approximations of how each shade appears on screen, not exact matches.</summary>
        public static readonly List<NamedColor> RalClassic = new()
        {
            C("RAL 1003", "Signal yellow",    "#F9A800"),
            C("RAL 1013", "Oyster white",     "#EAE6CA"),
            C("RAL 1018", "Zinc yellow",      "#F3E03B"),
            C("RAL 1021", "Colza yellow",     "#F3E03B"),
            C("RAL 1023", "Traffic yellow",   "#FAD201"),
            C("RAL 1028", "Melon yellow",     "#FF9B00"),
            C("RAL 2003", "Pastel orange",    "#F75E25"),
            C("RAL 2004", "Pure orange",      "#F44611"),
            C("RAL 2008", "Bright red orange","#F75E25"),
            C("RAL 3000", "Flame red",        "#AF2B1E"),
            C("RAL 3001", "Signal red",       "#A52019"),
            C("RAL 3003", "Ruby red",         "#9B111E"),
            C("RAL 3020", "Traffic red",      "#CC0605"),
            C("RAL 3026", "Luminous red",     "#FE0000"),
            C("RAL 4003", "Heather violet",   "#DE4C8A"),
            C("RAL 4005", "Blue lilac",       "#6C4675"),
            C("RAL 4006", "Traffic purple",   "#A03472"),
            C("RAL 5002", "Ultramarine blue", "#20214F"),
            C("RAL 5005", "Signal blue",      "#1D1E33"),
            C("RAL 5012", "Light blue",       "#3481B8"),
            C("RAL 5015", "Sky blue",         "#2874B2"),
            C("RAL 5017", "Traffic blue",     "#063971"),
            C("RAL 6011", "Reseda green",     "#587246"),
            C("RAL 6018", "Yellow green",     "#57A639"),
            C("RAL 6024", "Traffic green",    "#308446"),
            C("RAL 6029", "Mint green",       "#007243"),
            C("RAL 7016", "Anthracite grey",  "#293133"),
            C("RAL 7035", "Light grey",       "#D7D7D7"),
            C("RAL 7040", "Window grey",      "#9DA1AA"),
            C("RAL 7047", "Telegrey 4",       "#C8C8C8"),
            C("RAL 8003", "Clay brown",       "#7E4B26"),
            C("RAL 8017", "Chocolate brown",  "#45322E"),
            C("RAL 9003", "Signal white",     "#F4F4F4"),
            C("RAL 9004", "Signal black",     "#282828"),
            C("RAL 9005", "Jet black",        "#0A0A0A"),
            C("RAL 9010", "Pure white",       "#FFFFFF"),
            C("RAL 9016", "Traffic white",    "#F6F6F6"),
            C("RAL 9017", "Traffic black",    "#1E1E1E"),
        };

        /// <summary>The 216 "web-safe" colours - every combination of R/G/B from the six values
        /// 00,33,66,99,CC,FF. A genuine published convention from the 256-colour display era, and
        /// still handy as an evenly-spaced sampling of the whole RGB cube.</summary>
        public static readonly List<NamedColor> WebSafe216 = BuildWebSafe();

        private static List<NamedColor> BuildWebSafe()
        {
            var steps = new[] { 0x00, 0x33, 0x66, 0x99, 0xCC, 0xFF };
            var list = new List<NamedColor>();
            foreach (int r in steps)
                foreach (int g in steps)
                    foreach (int b in steps)
                        list.Add(new NamedColor
                        {
                            Code = "",
                            Name = $"#{r:X2}{g:X2}{b:X2}",
                            Color = Color.FromRgb((byte)r, (byte)g, (byte)b)
                        });
            return list;
        }

        /// <summary>Safety and hazard colours in the spirit of ANSI Z535 / ISO 3864 - the
        /// red/orange/yellow/green/blue scheme used on signage and equipment markings. These are
        /// the on-screen conventions rather than certified ink specifications.</summary>
        public static readonly List<NamedColor> SafetyColors = new()
        {
            C("", "Safety Red (danger)",       "#C8102E"),
            C("", "Safety Orange (warning)",   "#FF6900"),
            C("", "Safety Yellow (caution)",   "#FFD100"),
            C("", "Safety Green (safe/first aid)", "#00843D"),
            C("", "Safety Blue (notice)",      "#003DA5"),
            C("", "Safety Purple (radiation)", "#65197B"),
            C("", "Hazard Black",              "#101820"),
            C("", "Hazard White",              "#FFFFFF"),
            C("", "Fire Equipment Red",        "#B01B2E"),
            C("", "Radiation Yellow",          "#FFE700"),
        };

        /// <summary>The classic 16-colour hardware palette from CGA/EGA-era PC displays - fully
        /// documented, and still the fastest way to get an authentic retro look.</summary>
        public static readonly List<NamedColor> RetroHardware16 = new()
        {
            C("0",  "Black",         "#000000"), C("1",  "Blue",          "#0000AA"),
            C("2",  "Green",         "#00AA00"), C("3",  "Cyan",          "#00AAAA"),
            C("4",  "Red",           "#AA0000"), C("5",  "Magenta",       "#AA00AA"),
            C("6",  "Brown",         "#AA5500"), C("7",  "Light Gray",    "#AAAAAA"),
            C("8",  "Dark Gray",     "#555555"), C("9",  "Light Blue",    "#5555FF"),
            C("10", "Light Green",   "#55FF55"), C("11", "Light Cyan",    "#55FFFF"),
            C("12", "Light Red",     "#FF5555"), C("13", "Light Magenta", "#FF55FF"),
            C("14", "Yellow",        "#FFFF55"), C("15", "White",         "#FFFFFF"),
        };

        /// <summary>Traditional artists' pigment colours. The names are historic and in common use
        /// (they describe the pigment, not any brand's product), and these are the usual on-screen
        /// approximations of how each one appears.</summary>
        public static readonly List<NamedColor> ArtistPigments = new()
        {
            C("", "Titanium White",     "#FBFBF9"), C("", "Naples Yellow",   "#FADA5E"),
            C("", "Yellow Ochre",       "#CC7722"), C("", "Raw Sienna",      "#C69F6E"),
            C("", "Burnt Sienna",       "#8A3324"), C("", "Raw Umber",       "#826644"),
            C("", "Burnt Umber",        "#4E3524"), C("", "Venetian Red",    "#C80815"),
            C("", "Alizarin Crimson",   "#E32636"), C("", "Cadmium Red",     "#D22B2B"),
            C("", "Cadmium Orange",     "#ED872D"), C("", "Cadmium Yellow",  "#FFF600"),
            C("", "Viridian",           "#40826D"), C("", "Sap Green",       "#507D2A"),
            C("", "Terre Verte",        "#6B7C5C"), C("", "Prussian Blue",   "#003153"),
            C("", "Ultramarine",        "#3F00FF"), C("", "Cerulean Blue",   "#2A52BE"),
            C("", "Cobalt Blue",        "#0047AB"), C("", "Dioxazine Purple","#582F70"),
            C("", "Payne's Grey",       "#536878"), C("", "Ivory Black",     "#231F20"),
            C("", "Lamp Black",         "#2B2B2B"), C("", "Zinc White",      "#F5F5F0"),
        };

        /// <summary>An even neutral ramp in 5% steps - useful for shading, mockups and anywhere a
        /// predictable tonal step matters more than a specific hue.</summary>
        public static readonly List<NamedColor> GrayRamp = BuildGrayRamp();

        private static List<NamedColor> BuildGrayRamp()
        {
            var list = new List<NamedColor>();
            for (int i = 0; i <= 20; i++)
            {
                int v = (int)System.Math.Round(i * 255.0 / 20);
                list.Add(new NamedColor { Code = "", Name = $"{i * 5}% white", Color = Color.FromRgb((byte)v, (byte)v, (byte)v) });
            }
            return list;
        }

        /// <summary>The CSS / X11 named colours - an actual published web standard, and handy when
        /// you need a colour that will match a name used in HTML or CSS exactly.</summary>
        public static readonly List<NamedColor> WebNamed = new()
        {
            C("", "AliceBlue", "#F0F8FF"),      C("", "Beige", "#F5F5DC"),
            C("", "Coral", "#FF7F50"),          C("", "CornflowerBlue", "#6495ED"),
            C("", "Crimson", "#DC143C"),        C("", "DarkCyan", "#008B8B"),
            C("", "DarkOliveGreen", "#556B2F"), C("", "DarkOrange", "#FF8C00"),
            C("", "DarkSlateGray", "#2F4F4F"),  C("", "DeepPink", "#FF1493"),
            C("", "DodgerBlue", "#1E90FF"),     C("", "Firebrick", "#B22222"),
            C("", "ForestGreen", "#228B22"),    C("", "Gold", "#FFD700"),
            C("", "Indigo", "#4B0082"),         C("", "Khaki", "#F0E68C"),
            C("", "Lavender", "#E6E6FA"),       C("", "LimeGreen", "#32CD32"),
            C("", "Maroon", "#800000"),         C("", "MidnightBlue", "#191970"),
            C("", "Olive", "#808000"),          C("", "OrangeRed", "#FF4500"),
            C("", "Orchid", "#DA70D6"),         C("", "PaleGreen", "#98FB98"),
            C("", "Peru", "#CD853F"),           C("", "Plum", "#DDA0DD"),
            C("", "RebeccaPurple", "#663399"),  C("", "RoyalBlue", "#4169E1"),
            C("", "SaddleBrown", "#8B4513"),    C("", "Salmon", "#FA8072"),
            C("", "SeaGreen", "#2E8B57"),       C("", "Sienna", "#A0522D"),
            C("", "SlateBlue", "#6A5ACD"),      C("", "SteelBlue", "#4682B4"),
            C("", "Teal", "#008080"),           C("", "Thistle", "#D8BFD8"),
            C("", "Tomato", "#FF6347"),         C("", "Turquoise", "#40E0D0"),
            C("", "Violet", "#EE82EE"),         C("", "Wheat", "#F5DEB3"),
        };
    }
}
