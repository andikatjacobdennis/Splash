using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaintClone.Services;

namespace PaintClone.Dialogs
{
    public partial class ShortcutManagerWindow : Window
    {
        private class Row
        {
            public string Id;
            public string DisplayName { get; set; }
            public string BindingText { get; set; }
        }

        private readonly ShortcutManager _shortcuts;
        private List<Row> _rows;
        private bool _listening;
        private string _selectedId;

        public ShortcutManagerWindow(ShortcutManager shortcuts)
        {
            InitializeComponent();
            _shortcuts = shortcuts;
            PreviewKeyDown += Window_PreviewKeyDown;
            RefreshList();
        }

        private void RefreshList()
        {
            _rows = ShortcutManager.Defaults.Select(d =>
            {
                var (key, mods) = _shortcuts.Get(d.Id);
                return new Row { Id = d.Id, DisplayName = d.DisplayName, BindingText = ShortcutManager.DisplayString(key, mods) };
            }).ToList();
            ShortcutList.ItemsSource = null;
            ShortcutList.ItemsSource = _rows;
        }

        private void ShortcutList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = ShortcutList.SelectedItem as Row;
            _selectedId = row?.Id;
            ChangeBtn.IsEnabled = row != null;
            ClearBtn.IsEnabled = row != null;
            StatusLabel.Text = "";
        }

        private void Change_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedId == null) return;
            _listening = true;
            StatusLabel.Text = "Press a key combination... (Esc to cancel)";
            Focus();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedId == null) return;
            _shortcuts.Set(_selectedId, Key.None, ModifierKeys.None);
            RefreshList();
            StatusLabel.Text = "Shortcut cleared.";
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this, "Reset every shortcut to its default? This can't be undone.",
                "Keyboard Shortcuts", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _shortcuts.ResetToDefaults();
                RefreshList();
                StatusLabel.Text = "All shortcuts reset to defaults.";
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_listening) return;
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                _listening = false;
                StatusLabel.Text = "Cancelled.";
                return;
            }
            // Ignore a bare modifier press - wait for the actual key that completes the combo.
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                    or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return;

            var mods = Keyboard.Modifiers;
            _listening = false;

            var conflict = _shortcuts.FindConflict(_selectedId, key, mods);
            if (conflict != null)
            {
                var conflictDef = ShortcutManager.Defaults.First(d => d.Id == conflict);
                var result = MessageBox.Show(this,
                    $"{ShortcutManager.DisplayString(key, mods)} is already used by \"{conflictDef.DisplayName}\".\n\n" +
                    "Reassign it to this action instead?",
                    "Shortcut Conflict", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    StatusLabel.Text = "Not changed.";
                    return;
                }
                _shortcuts.Set(conflict, Key.None, ModifierKeys.None); // free up the old binding
            }

            _shortcuts.Set(_selectedId, key, mods);
            RefreshList();
            StatusLabel.Text = "Shortcut updated.";
        }
    }
}
