using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace PaintClone.Services
{
    /// <summary>
    /// Tracks the last several colors actually confirmed (OK'd) in the Edit Colors dialog, most
    /// recent first, separate from the explicit "Add to Custom Colors" tray. Persisted the same
    /// simple way as CustomColorStore.
    /// </summary>
    public static class RecentColorStore
    {
        private const int MaxEntries = 14;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PaintClone", "recentcolors.txt");

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
                        if (parts.Length == 3
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
                // Best-effort - start empty if the file is missing/corrupt/unreadable.
            }
            return list;
        }

        /// <summary>Moves (or inserts) the color to the front of the list, in place, and persists it.</summary>
        public static void Push(List<Color> list, Color c)
        {
            list.RemoveAll(x => x.R == c.R && x.G == c.G && x.B == c.B);
            list.Insert(0, c);
            while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
            Save(list);
        }

        public static void Save(List<Color> colors)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllLines(FilePath, colors.Select(c => $"{c.R},{c.G},{c.B}"));
            }
            catch
            {
                // Non-fatal.
            }
        }
    }
}
