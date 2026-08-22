using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace PaintClone.Services
{
    /// <summary>
    /// Every keyboard shortcut in the app, in one place, rebindable and persisted. MainWindow no
    /// longer hardcodes "Ctrl+Z means Undo" - it asks this class "what action (if any) is bound to
    /// the key/modifier combo that was just pressed?" and dispatches through a small
    /// Dictionary&lt;actionId, Action&gt; instead. This is what makes the Shortcut Manager window
    /// possible: it edits exactly the same bindings MainWindow reads, with nothing hardcoded twice.
    /// </summary>
    public class ShortcutManager
    {
        public class Def
        {
            public string Id;
            public string DisplayName;
            public Key DefaultKey;
            public ModifierKeys DefaultMods;
        }

        public static readonly List<Def> Defaults = new()
        {
            new() { Id = "New", DisplayName = "File: New", DefaultKey = Key.N, DefaultMods = ModifierKeys.Control },
            new() { Id = "Open", DisplayName = "File: Open", DefaultKey = Key.O, DefaultMods = ModifierKeys.Control },
            new() { Id = "Save", DisplayName = "File: Save", DefaultKey = Key.S, DefaultMods = ModifierKeys.Control },
            new() { Id = "Print", DisplayName = "File: Print", DefaultKey = Key.P, DefaultMods = ModifierKeys.Control },
            new() { Id = "Undo", DisplayName = "Edit: Undo", DefaultKey = Key.Z, DefaultMods = ModifierKeys.Control },
            new() { Id = "Redo", DisplayName = "Edit: Redo (Repeat)", DefaultKey = Key.Y, DefaultMods = ModifierKeys.Control },
            new() { Id = "Cut", DisplayName = "Edit: Cut", DefaultKey = Key.X, DefaultMods = ModifierKeys.Control },
            new() { Id = "Copy", DisplayName = "Edit: Copy", DefaultKey = Key.C, DefaultMods = ModifierKeys.Control },
            new() { Id = "Paste", DisplayName = "Edit: Paste", DefaultKey = Key.V, DefaultMods = ModifierKeys.Control },
            new() { Id = "SelectAll", DisplayName = "Edit: Select All", DefaultKey = Key.A, DefaultMods = ModifierKeys.Control },
            new() { Id = "Deselect", DisplayName = "Edit: Deselect", DefaultKey = Key.D, DefaultMods = ModifierKeys.Control },
            new() { Id = "ClearSelection", DisplayName = "Edit: Clear Selection", DefaultKey = Key.Delete, DefaultMods = ModifierKeys.None },
            new() { Id = "Invert", DisplayName = "Image: Invert Colors", DefaultKey = Key.I, DefaultMods = ModifierKeys.Control },
            new() { Id = "Attributes", DisplayName = "Image: Attributes", DefaultKey = Key.E, DefaultMods = ModifierKeys.Control },
            new() { Id = "SwapColors", DisplayName = "Colors: Swap foreground/background", DefaultKey = Key.X, DefaultMods = ModifierKeys.None },
            // Photoshop puts this on D, but D is already Tool: Gradient here, and binding both to
            // it would leave Match() returning whichever happened to enumerate first. Shipped
            // unbound instead - the control in the toolbox is always clickable, and anyone who
            // wants the key can assign it in the Shortcut Manager, reassigning Gradient first.
            new() { Id = "DefaultColors", DisplayName = "Colors: Reset to black/white", DefaultKey = Key.None, DefaultMods = ModifierKeys.None },
            new() { Id = "Cancel", DisplayName = "Cancel current action", DefaultKey = Key.Escape, DefaultMods = ModifierKeys.None },
            new() { Id = "ZoomIn", DisplayName = "View: Zoom In", DefaultKey = Key.OemPlus, DefaultMods = ModifierKeys.Control | ModifierKeys.Shift },
            new() { Id = "ZoomOut", DisplayName = "View: Zoom Out", DefaultKey = Key.OemMinus, DefaultMods = ModifierKeys.Control | ModifierKeys.Shift },
            new() { Id = "ResetZoom", DisplayName = "View: Reset Zoom to 100%", DefaultKey = Key.D0, DefaultMods = ModifierKeys.Control },
            new() { Id = "ToggleFullScreen", DisplayName = "View: Full Screen", DefaultKey = Key.F11, DefaultMods = ModifierKeys.None },
            new() { Id = "SizeUp", DisplayName = "Increase brush/pencil/eraser size", DefaultKey = Key.OemPlus, DefaultMods = ModifierKeys.Control },
            new() { Id = "SizeDown", DisplayName = "Decrease brush/pencil/eraser size", DefaultKey = Key.OemMinus, DefaultMods = ModifierKeys.Control },
            new() { Id = "SizeUpBracket", DisplayName = "Increase size (bracket)", DefaultKey = Key.OemCloseBrackets, DefaultMods = ModifierKeys.None },
            new() { Id = "SizeDownBracket", DisplayName = "Decrease size (bracket)", DefaultKey = Key.OemOpenBrackets, DefaultMods = ModifierKeys.None },
            new() { Id = "MoveSelectionLeft", DisplayName = "Move selection left", DefaultKey = Key.Left, DefaultMods = ModifierKeys.None },
            new() { Id = "MoveSelectionRight", DisplayName = "Move selection right", DefaultKey = Key.Right, DefaultMods = ModifierKeys.None },
            new() { Id = "MoveSelectionUp", DisplayName = "Move selection up", DefaultKey = Key.Up, DefaultMods = ModifierKeys.None },
            new() { Id = "MoveSelectionDown", DisplayName = "Move selection down", DefaultKey = Key.Down, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Pencil", DisplayName = "Tool: Pencil", DefaultKey = Key.P, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Brush", DisplayName = "Tool: Brush", DefaultKey = Key.B, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Airbrush", DisplayName = "Tool: Airbrush", DefaultKey = Key.A, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Eraser", DisplayName = "Tool: Eraser", DefaultKey = Key.E, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Fill", DisplayName = "Tool: Fill With Color", DefaultKey = Key.G, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Pick", DisplayName = "Tool: Pick Color", DefaultKey = Key.I, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Magnifier", DisplayName = "Tool: Magnifier", DefaultKey = Key.Z, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Text", DisplayName = "Tool: Text", DefaultKey = Key.T, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Line", DisplayName = "Tool: Line", DefaultKey = Key.L, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Curve", DisplayName = "Tool: Curve", DefaultKey = Key.C, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Rectangle", DisplayName = "Tool: Rectangle", DefaultKey = Key.R, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Ellipse", DisplayName = "Tool: Ellipse", DefaultKey = Key.O, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_RoundedRectangle", DisplayName = "Tool: Rounded Rectangle", DefaultKey = Key.U, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Select", DisplayName = "Tool: Select", DefaultKey = Key.S, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_FreeFormSelect", DisplayName = "Tool: Free-Form Select", DefaultKey = Key.F, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_MagicWand", DisplayName = "Tool: Magic Wand", DefaultKey = Key.W, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Polygon", DisplayName = "Tool: Polygon", DefaultKey = Key.Y, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Arrow", DisplayName = "Tool: Arrow", DefaultKey = Key.N, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Star", DisplayName = "Tool: Star", DefaultKey = Key.K, DefaultMods = ModifierKeys.None },
            new() { Id = "Tool_Gradient", DisplayName = "Tool: Gradient", DefaultKey = Key.D, DefaultMods = ModifierKeys.None },
        };

        private readonly Dictionary<string, (Key Key, ModifierKeys Mods)> _bindings = new();

        public event EventHandler Changed;

        public ShortcutManager()
        {
            ResetToDefaults(persist: false);
            Load();
        }

        public IEnumerable<Def> AllDefs => Defaults;

        public (Key Key, ModifierKeys Mods) Get(string id) =>
            _bindings.TryGetValue(id, out var v) ? v : (Key.None, ModifierKeys.None);

        /// <summary>Returns the action id bound to this key/modifier combo, or null if none.</summary>
        public string Match(Key key, ModifierKeys mods)
        {
            foreach (var kv in _bindings)
                if (kv.Value.Key == key && kv.Value.Mods == mods) return kv.Key;
            return null;
        }

        /// <summary>Returns the id of whichever OTHER action already uses this combo, if any -
        /// used by the Shortcut Manager window to warn about conflicts before rebinding.</summary>
        public string FindConflict(string excludingId, Key key, ModifierKeys mods)
        {
            foreach (var kv in _bindings)
                if (kv.Key != excludingId && kv.Value.Key == key && kv.Value.Mods == mods) return kv.Key;
            return null;
        }

        public void Set(string id, Key key, ModifierKeys mods)
        {
            _bindings[id] = (key, mods);
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void ResetToDefaults(bool persist = true)
        {
            _bindings.Clear();
            foreach (var d in Defaults) _bindings[d.Id] = (d.DefaultKey, d.DefaultMods);
            if (persist) Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public static string DisplayString(Key key, ModifierKeys mods)
        {
            if (key == Key.None) return "(none)";
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            parts.Add(KeyLabel(key));
            return string.Join("+", parts);
        }

        private static string KeyLabel(Key key) => key switch
        {
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.D0 => "0",
            _ => key.ToString(),
        };

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PaintClone", "shortcuts.txt");

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 3 && Enum.TryParse<Key>(parts[1], out var key) && Enum.TryParse<ModifierKeys>(parts[2], out var mods))
                        _bindings[parts[0]] = (key, mods);
                }
            }
            catch
            {
                // Best-effort - fall back to whatever defaults were already loaded.
            }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllLines(FilePath, _bindings.Select(kv => $"{kv.Key}\t{kv.Value.Key}\t{kv.Value.Mods}"));
            }
            catch
            {
                // Non-fatal.
            }
        }
    }
}
