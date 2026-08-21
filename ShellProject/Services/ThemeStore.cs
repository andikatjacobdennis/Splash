using System;
using System.IO;

namespace PaintClone.Services
{
    /// <summary>
    /// Persists the chosen UI theme (Dark/Light) across app restarts, the same
    /// %AppData%\PaintClone text-file pattern used by <see cref="CustomColorStore"/> and
    /// <see cref="RecentColorStore"/> - no need for a JSON/XML dependency for one word.
    /// </summary>
    public static class ThemeStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PaintClone", "theme.txt");

        /// <summary>Defaults to Dark - the theme this app shipped with first.</summary>
        public static string Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var text = File.ReadAllText(FilePath).Trim();
                    if (text == ThemeManager.Light) return ThemeManager.Light;
                }
            }
            catch
            {
                // Best-effort persistence - if the file is missing, corrupt, or unreadable for any
                // reason, fall through to the default rather than crashing.
            }
            return ThemeManager.Dark;
        }

        public static void Save(string themeName)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, themeName);
            }
            catch
            {
                // Non-fatal: worst case, the theme choice just doesn't persist this run.
            }
        }
    }
}
