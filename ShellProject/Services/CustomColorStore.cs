using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace PaintClone.Services
{
    /// <summary>
    /// Persists the "Add to Custom Colors" swatches from the Edit Colors dialog across app
    /// restarts. Classic Paint kept these for the whole Windows session; we go one better and
    /// keep them across runs entirely, saved as a plain "R,G,B per line" text file under
    /// %AppData%\PaintClone - no need for a JSON/XML dependency for sixteen numbers.
    /// </summary>
    public static class CustomColorStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PaintClone", "customcolors.txt");

        /// <summary>How many custom slots the tray shows. Empty slots are drawn as placeholders,
        /// so the tray is a fixed, predictable grid rather than growing and reflowing as you add
        /// colours.</summary>
        public const int Capacity = 30;

        /// <summary>A handful of useful colours the tray starts with, so a brand-new install has
        /// something in it rather than presenting thirty empty holes. Includes one semi-transparent
        /// entry to make it discoverable that custom colours can carry an alpha value at all.</summary>
        public static readonly List<Color> Defaults = new()
        {
            Color.FromRgb(0x2C, 0x3E, 0x50),              // deep slate
            Color.FromRgb(0xE7, 0x4C, 0x3C),              // warm red
            Color.FromRgb(0xF1, 0xC4, 0x0F),              // golden yellow
            Color.FromRgb(0x27, 0xAE, 0x60),              // fresh green
            Color.FromArgb(0x80, 0x34, 0x98, 0xDB),       // 50% blue - shows off alpha support
        };

        public static List<Color> Load()
        {
            var list = new List<Color>();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var parts = line.Split(',');
                        // Four values are A,R,G,B; three are the older R,G,B format from before
                        // custom colours could carry transparency. Both are accepted so an
                        // existing colour file keeps working after an upgrade.
                        if (parts.Length == 4
                            && byte.TryParse(parts[0], out var a4)
                            && byte.TryParse(parts[1], out var r4)
                            && byte.TryParse(parts[2], out var g4)
                            && byte.TryParse(parts[3], out var b4))
                        {
                            list.Add(Color.FromArgb(a4, r4, g4, b4));
                        }
                        else if (parts.Length == 3
                            && byte.TryParse(parts[0], out var r)
                            && byte.TryParse(parts[1], out var g)
                            && byte.TryParse(parts[2], out var b))
                        {
                            list.Add(Color.FromRgb(r, g, b));
                        }
                    }
                }
            }
            catch
            {
                // Best-effort persistence - if the file is missing, corrupt, or unreadable for any
                // reason, fall through to the seeded defaults rather than crashing.
            }
            // A first run (or an unreadable file) starts from the defaults instead of nothing.
            if (list.Count == 0) list.AddRange(Defaults);
            if (list.Count > Capacity) list.RemoveRange(Capacity, list.Count - Capacity);
            return list;
        }

        public static void Save(List<Color> colors)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllLines(FilePath, colors.Select(c => $"{c.A},{c.R},{c.G},{c.B}"));
            }
            catch
            {
                // Non-fatal: worst case, custom colors just don't persist this run.
            }
        }
    }
}
