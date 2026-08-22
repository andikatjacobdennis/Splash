using System.Windows;
using System.Windows.Controls;
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
    /// Structural operations (add/delete/reorder/merge) push an undo state first, the same way
    /// every drawing tool does before it mutates pixels - this is what makes those operations
    /// undoable via Ctrl+Z, closing the "layer structure changes aren't part of undo/redo" gap.
    /// Visibility toggling and simply selecting a different active layer are deliberately left out
    /// of undo, since they're view state rather than a content change.
    /// </summary>
    public partial class LayersPanel : UserControl
    {
        private PaintDocument _document;
        private HistoryManager _history;

        public LayersPanel()
        {
            InitializeComponent();
        }

        public void SetDocument(PaintDocument document, HistoryManager history = null)
        {
            _document = document;
            if (history != null) _history = history;
            Refresh();
        }

        public void Refresh()
        {
            if (_document == null) return;

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

                var cb = new CheckBox { IsChecked = layer.Visible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                cb.Checked += (s, e) => _document.SetLayerVisible(layerIndex, true);
                cb.Unchecked += (s, e) => _document.SetLayerVisible(layerIndex, false);

                string label = layer.Text != null ? $"[T] {layer.Name}" : layer.Name;
                var text = new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                    ToolTip = layer.Text != null ? "Text layer - click into it with the Text tool to edit it again" : null
                };

                panel.Children.Add(cb);
                panel.Children.Add(text);
                row.Child = panel;
                row.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.OriginalSource is CheckBox) return; // let the checkbox handle its own click
                    _document.ActiveLayerIndex = layerIndex;
                    Refresh();
                };

                LayersList.Items.Add(new ListBoxItem { Content = row, Padding = new Thickness(0) });
            }

            DeleteBtn.IsEnabled = !onlyOneLayer;
            MergeBtn.IsEnabled = _document.ActiveLayerIndex > 0;
            UpBtn.IsEnabled = _document.ActiveLayerIndex < _document.Layers.Count - 1;
            DownBtn.IsEnabled = _document.ActiveLayerIndex > 0;
            RasterizeBtn.IsEnabled = _document.ActiveLayer.Text != null;
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

        private void MergeDown_Click(object sender, RoutedEventArgs e)
        {
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
