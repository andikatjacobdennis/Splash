using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PaintClone.Models;
using PaintClone.Services;

namespace PaintClone.Dialogs
{
    /// <summary>
    /// Docked Layers panel (Photoshop-style), embedded directly in MainWindow's right-hand panel
    /// dock rather than shown as a separate floating window. Holds a PaintDocument reference that
    /// MainWindow re-points via SetDocument whenever the document itself is replaced (File > New /
    /// Open create a brand new PaintDocument object rather than mutating the existing one) - without
    /// this, the panel would silently keep editing an orphaned document after New/Open.
    ///
    /// Structural operations (add/delete/reorder/merge/rename) push an undo state first, the same
    /// way every drawing tool does before it mutates pixels - this is what makes those operations
    /// undoable via Ctrl+Z. Visibility toggling and simply selecting a different active layer are
    /// deliberately left out of undo, since they're view state rather than a content change.
    /// </summary>
    public partial class LayersPanel : UserControl
    {
        private PaintDocument _document;
        private HistoryManager _history;

        /// <summary>Layers ticked for a multi-layer merge. Held as layer *objects* rather than
        /// indices so reordering can't silently retarget the selection at whatever now sits at a
        /// remembered index. Pruned against the document on every Refresh, which also means an undo
        /// (whose snapshots rebuild layers as new objects) simply clears the selection rather than
        /// leaving it pointing at layers that no longer exist.</summary>
        private readonly HashSet<PaintLayer> _checkedForMerge = new();

        /// <summary>Set while Refresh is populating the list, so the CheckBox/ToggleButton handlers
        /// it wires up don't fire as a side effect of being given their initial state.</summary>
        private bool _populating;

        public LayersPanel()
        {
            InitializeComponent();
        }

        public void SetDocument(PaintDocument document, HistoryManager history = null)
        {
            _document = document;
            if (history != null) _history = history;
            _checkedForMerge.Clear(); // a selection from the previous document means nothing here
            Refresh();
        }

        public void Refresh()
        {
            if (_document == null) return;

            // Reentrancy guard. Clearing the list below detaches an in-progress rename TextBox,
            // which fires its LostKeyboardFocus handler, which commits the rename, which raises
            // LayersChanged, which calls straight back into this method. Without this the inner
            // call would clear and fully repopulate the list, and then the outer call's loop would
            // carry on appending its own rows on top - showing every layer twice until the next
            // refresh happened to tidy it up.
            if (_populating) return;

            // Drop any ticked layer that's no longer part of the document (deleted, merged away,
            // or replaced wholesale by an undo).
            _checkedForMerge.RemoveWhere(l => !_document.Layers.Contains(l));

            _populating = true;
            try
            {
                LayersList.Items.Clear();
                bool onlyOneLayer = _document.Layers.Count <= 1;

                // Top-to-bottom in the list = front-to-back on the canvas, matching every other
                // layers panel's convention (Document.Layers[last] is the frontmost layer).
                for (int i = _document.Layers.Count - 1; i >= 0; i--)
                {
                    int layerIndex = i; // capture for the closures below
                    var layer = _document.Layers[i];
                    bool isActive = i == _document.ActiveLayerIndex;

                    var row = new Border { Padding = new Thickness(4, 3, 4, 3) };
                    if (isActive) row.SetResourceReference(Border.BackgroundProperty, "PsAccentSoft");
                    else row.Background = Brushes.Transparent;

                    var panel = new StackPanel { Orientation = Orientation.Horizontal };

                    // Checkbox = merge selection (NOT visibility - that's the eye button next to it).
                    var mergeCheck = new CheckBox
                    {
                        IsChecked = _checkedForMerge.Contains(layer),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0),
                        ToolTip = "Tick to include this layer in the next Merge"
                    };
                    mergeCheck.Checked += (s, e) => { if (!_populating) _checkedForMerge.Add(layer); UpdateButtonStates(); };
                    mergeCheck.Unchecked += (s, e) => { if (!_populating) _checkedForMerge.Remove(layer); UpdateButtonStates(); };

                    // Takes the themed OptionToggleStyle rather than the stock WPF ToggleButton
                    // chrome. Left unstyled it drew as a pale system button, and its glyph - a bare
                    // string, so WPF wraps it in a TextBlock that inherits the theme's light
                    // foreground - came out light-on-light: a small blank-looking rectangle.
                    var eye = new ToggleButton
                    {
                        Style = (Style)FindResource("OptionToggleStyle"),
                        Content = layer.Visible ? "◉" : "○",
                        IsChecked = layer.Visible,
                        Width = 22,
                        Height = 20,
                        FontSize = 11,
                        Focusable = false,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0),
                        ToolTip = "Show or hide this layer"
                    };
                    eye.Checked += (s, e) => { if (!_populating) { _document.SetLayerVisible(layerIndex, true); } };
                    eye.Unchecked += (s, e) => { if (!_populating) { _document.SetLayerVisible(layerIndex, false); } };

                    // Live thumbnail of the layer's own pixels. The Image points straight at the
                    // layer's WriteableBitmap, so it keeps itself current as the layer is drawn on
                    // rather than needing to be regenerated. The checkerboard behind it is what
                    // makes a transparent layer read as transparent instead of as a blank box.
                    var thumb = new Border
                    {
                        Width = 34,
                        Height = 26,
                        Margin = new Thickness(0, 0, 6, 0),
                        BorderThickness = new Thickness(1),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = (Brush)FindResource("CheckerboardBrush"),
                        Child = new Image { Source = layer.Surface.Bitmap, Stretch = Stretch.Uniform }
                    };
                    thumb.SetResourceReference(Border.BorderBrushProperty, "XpBorder");

                    string label = layer.Text != null ? $"[T] {layer.Name}" : layer.Name;
                    var text = new TextBlock
                    {
                        Text = label,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                        ToolTip = layer.Text != null
                            ? "Text layer - click into it with the Text tool to edit it again. Double-click here to rename."
                            : "Double-click to rename"
                    };
                    // Double-click the name to rename in place, Photoshop-style.
                    text.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount < 2) return;
                        e.Handled = true; // don't let the row's own click handler also run
                        BeginRename(layerIndex, layer, panel, text);
                    };

                    panel.Children.Add(mergeCheck);
                    panel.Children.Add(eye);
                    panel.Children.Add(thumb);
                    panel.Children.Add(text);
                    row.Child = panel;
                    row.MouseLeftButtonDown += (s, e) =>
                    {
                        // Let the checkbox/eye/rename handle their own clicks.
                        if (e.OriginalSource is CheckBox || e.OriginalSource is ToggleButton || e.OriginalSource is TextBox) return;
                        _document.ActiveLayerIndex = layerIndex;
                        Refresh();
                    };

                    LayersList.Items.Add(new ListBoxItem { Content = row, Padding = new Thickness(0) });
                }

                DeleteBtn.IsEnabled = !onlyOneLayer;
                UpBtn.IsEnabled = _document.ActiveLayerIndex < _document.Layers.Count - 1;
                DownBtn.IsEnabled = _document.ActiveLayerIndex > 0;
                RasterizeBtn.IsEnabled = _document.ActiveLayer.Text != null;
            }
            finally
            {
                _populating = false;
            }

            UpdateButtonStates();
        }

        /// <summary>Merge is enabled either for a real multi-layer selection (two or more ticked) or
        /// for the classic single "merge down into the layer below" when nothing is ticked.</summary>
        private void UpdateButtonStates()
        {
            if (_document == null) return;
            MergeBtn.IsEnabled = _checkedForMerge.Count >= 2
                || (_checkedForMerge.Count == 0 && _document.ActiveLayerIndex > 0);
        }

        /// <summary>Swaps the layer's name label for an editable box, committing on Enter or focus
        /// loss and abandoning on Escape.</summary>
        private void BeginRename(int layerIndex, PaintLayer layer, StackPanel panel, TextBlock label)
        {
            var edit = new TextBox
            {
                Text = layer.Name,
                MinWidth = 90,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = label.FontSize
            };

            int labelPos = panel.Children.IndexOf(label);
            panel.Children.Remove(label);
            panel.Children.Insert(labelPos, edit);

            bool finished = false;
            void Commit(bool save)
            {
                if (finished) return;
                finished = true;
                if (save && edit.Text.Trim().Length > 0 && edit.Text.Trim() != layer.Name)
                {
                    _history.PushUndoState(_document, "Rename Layer");
                    _document.RenameLayer(layerIndex, edit.Text);
                }
                Refresh(); // rebuilds the row either way, putting the label back
            }

            edit.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Commit(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; Commit(false); }
            };
            edit.LostKeyboardFocus += (s, e) => Commit(true);

            edit.Focus();
            edit.SelectAll();
        }

        private void AddLayer_Click(object sender, RoutedEventArgs e)
        {
            _history.PushUndoState(_document, "Add Layer");
            _document.AddLayer();
        }

        private void DeleteLayer_Click(object sender, RoutedEventArgs e)
        {
            _history.PushUndoState(_document, "Delete Layer");
            _document.DeleteLayer(_document.ActiveLayerIndex);
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            _history.PushUndoState(_document, "Reorder Layers");
            _document.MoveLayer(_document.ActiveLayerIndex, +1);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            _history.PushUndoState(_document, "Reorder Layers");
            _document.MoveLayer(_document.ActiveLayerIndex, -1);
        }

        /// <summary>Merges the ticked layers together, or - when nothing is ticked - falls back to
        /// the original "merge the active layer into the one below" behaviour, so the button still
        /// does the obvious thing without requiring anything to be ticked first.</summary>
        private void MergeDown_Click(object sender, RoutedEventArgs e)
        {
            if (_checkedForMerge.Count >= 2)
            {
                var indices = _checkedForMerge
                    .Select(l => _document.Layers.IndexOf(l))
                    .Where(i => i >= 0)
                    .ToList();
                if (indices.Count < 2) return;

                _history.PushUndoState(_document, "Merge Layers");
                _checkedForMerge.Clear();
                _document.MergeLayers(indices);
                return;
            }

            if (_document.ActiveLayerIndex <= 0) return;
            _history.PushUndoState(_document, "Merge Layers");
            _document.MergeDown(_document.ActiveLayerIndex);
        }

        private void Rasterize_Click(object sender, RoutedEventArgs e)
        {
            _history.PushUndoState(_document, "Rasterize Text Layer");
            _document.RasterizeLayer(_document.ActiveLayerIndex);
        }
    }
}
