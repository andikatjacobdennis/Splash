using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaintClone.Services;

namespace PaintClone.Dialogs
{
    /// <summary>
    /// Non-modal window (Photoshop-style History panel) listing every step in the undo/redo
    /// history in order, with the current step highlighted. Clicking any entry jumps straight to
    /// it via HistoryManager.JumpTo. Stays open and live-updating while the user keeps drawing,
    /// since it's shown with .Show() rather than .ShowDialog().
    /// </summary>
    public partial class HistoryWindow : Window
    {
        private readonly HistoryManager _history;
        private readonly Action<int> _onJumpRequested;
        private bool _suppressSelection;

        public HistoryWindow(HistoryManager history, Action<int> onJumpRequested)
        {
            InitializeComponent();
            _history = history;
            _onJumpRequested = onJumpRequested;
            _history.StateChanged += History_StateChanged;
            RefreshList();
        }

        private void History_StateChanged(object sender, EventArgs e) => RefreshList();

        private void RefreshList()
        {
            _suppressSelection = true;
            HistoryList.Items.Clear();
            var labels = _history.Labels;
            for (int i = 0; i < labels.Count; i++)
                HistoryList.Items.Add($"{i}. {labels[i]}");

            if (HistoryList.Items.Count > 0)
            {
                HistoryList.SelectedIndex = Math.Min(_history.CurrentIndex, HistoryList.Items.Count - 1);
                HistoryList.ScrollIntoView(HistoryList.SelectedItem);
            }
            _suppressSelection = false;
        }

        private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection || HistoryList.SelectedIndex < 0) return;
            _onJumpRequested?.Invoke(HistoryList.SelectedIndex);
        }

        private void HistoryList_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var container = ItemsControl.ContainerFromElement(HistoryList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (container == null) return;
            int index = HistoryList.ItemContainerGenerator.IndexFromContainer(container);
            if (index < 0) return;

            if (index == _history.CurrentIndex)
            {
                MessageBox.Show(this, "The current step can't be deleted - undo or redo away from it first.",
                    "History", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _history.DeleteEntry(index); // fires StateChanged, which refreshes this list automatically
        }

        protected override void OnClosed(EventArgs e)
        {
            _history.StateChanged -= History_StateChanged;
            base.OnClosed(e);
        }
    }
}
