using System;
using System.Windows;

namespace PaintClone.Services
{
    /// <summary>
    /// Switches the app between the Dark and Light resource dictionaries (Themes/DarkTheme.xaml,
    /// Themes/LightTheme.xaml). Every color those dictionaries define is consumed elsewhere via
    /// {DynamicResource ...} rather than {StaticResource ...} specifically so this swap re-paints
    /// every open window immediately - a StaticResource reference freezes to whatever value was in
    /// scope when its Style/template was first loaded and would not react to this at all.
    ///
    /// App.xaml.cs applies the saved theme in OnStartup, before the StartupUri window is
    /// constructed, so there's no visible flash of the "wrong" theme on launch.
    /// </summary>
    public static class ThemeManager
    {
        public const string Dark = "Dark";
        public const string Light = "Light";

        public static string Current { get; private set; } = Dark;

        public static event EventHandler ThemeChanged;

        public static void Apply(string themeName)
        {
            if (themeName != Dark && themeName != Light) themeName = Dark;

            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/{themeName}Theme.xaml", UriKind.Absolute)
            };

            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count == 0) merged.Add(dict);
            else merged[0] = dict; // the theme dictionary is always merged in at index 0 - see App.xaml

            Current = themeName;
            ThemeStore.Save(themeName);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Loads and applies whatever theme was last saved (or Dark, on a first run).</summary>
        public static void ApplySaved() => Apply(ThemeStore.Load());
    }
}
