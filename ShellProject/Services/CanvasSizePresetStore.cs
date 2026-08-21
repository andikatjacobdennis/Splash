using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PaintClone.Services
{
    public class SizePreset
    {
        public string Name;
        public int Width;
        public int Height;
    }

    /// <summary>User-saved canvas size presets (distinct from the built-in ones), persisted the
    /// same simple way as the custom/recent color stores.</summary>
    public static class CanvasSizePresetStore
    {
        /// <summary>The fixed, non-persisted size choices offered everywhere a canvas size is
        /// picked from a list (New Picture, Attributes) - one shared table so the two dialogs can
        /// never drift into offering different built-in sizes.</summary>
        public static readonly List<SizePreset> BuiltIn = new()
        {
            new SizePreset { Name = "Default", Width = 480, Height = 360 },
            new SizePreset { Name = "Small", Width = 320, Height = 240 },
            new SizePreset { Name = "VGA", Width = 640, Height = 480 },
            new SizePreset { Name = "SVGA", Width = 800, Height = 600 },
            new SizePreset { Name = "XGA", Width = 1024, Height = 768 },
            new SizePreset { Name = "HD 720p", Width = 1280, Height = 720 },
            new SizePreset { Name = "Full HD 1080p", Width = 1920, Height = 1080 },
            new SizePreset { Name = "Square / Social Post", Width = 1080, Height = 1080 },
            new SizePreset { Name = "A4 @ 96 DPI", Width = 794, Height = 1123 },
            new SizePreset { Name = "US Letter @ 96 DPI", Width = 816, Height = 1056 },
        };

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PaintClone", "sizepresets.txt");

        public static List<SizePreset> Load()
        {
            var list = new List<SizePreset>();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length == 3 && int.TryParse(parts[1], out var w) && int.TryParse(parts[2], out var h) && w > 0 && h > 0)
                            list.Add(new SizePreset { Name = parts[0], Width = w, Height = h });
                    }
                }
            }
            catch
            {
                // Best-effort - start empty if the file is missing/corrupt/unreadable.
            }
            return list;
        }

        public static void Save(List<SizePreset> presets)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllLines(FilePath, presets.Select(p => $"{p.Name}\t{p.Width}\t{p.Height}"));
            }
            catch
            {
                // Non-fatal.
            }
        }
    }
}
