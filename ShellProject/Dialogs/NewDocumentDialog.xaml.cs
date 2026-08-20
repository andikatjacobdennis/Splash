using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PaintClone.Services;

namespace PaintClone.Dialogs
{
    public partial class NewDocumentDialog : Window
    {
        public int ResultWidth { get; private set; }
        public int ResultHeight { get; private set; }

        private static readonly List<SizePreset> BuiltIn = new()
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

        private readonly List<SizePreset> _customPresets;
        private bool _suppress;

        public NewDocumentDialog(int currentWidth, int currentHeight)
        {
            InitializeComponent();
            _customPresets = CanvasSizePresetStore.Load();
            WidthBox.Text = currentWidth.ToString();
            HeightBox.Text = currentHeight.ToString();
            RefreshList();
        }

        private void RefreshList()
        {
            PresetList.Items.Clear();
            PresetList.Items.Add(new ListBoxItem { Content = "Built-in sizes", FontWeight = FontWeights.Bold, IsEnabled = false });
            foreach (var p in BuiltIn)
                PresetList.Items.Add(new ListBoxItem { Content = $"{p.Name}  ({p.Width} x {p.Height})", Tag = p });

            if (_customPresets.Count > 0)
            {
                PresetList.Items.Add(new ListBoxItem { Content = "Your saved sizes (right-click to remove)", FontWeight = FontWeights.Bold, IsEnabled = false });
                foreach (var p in _customPresets)
                {
                    var item = new ListBoxItem { Content = $"{p.Name}  ({p.Width} x {p.Height})", Tag = p };
                    item.MouseRightButtonDown += (s, e) =>
                    {
                        _customPresets.Remove(p);
                        CanvasSizePresetStore.Save(_customPresets);
                        RefreshList();
                    };
                    PresetList.Items.Add(item);
                }
            }
        }

        private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetList.SelectedItem is ListBoxItem { Tag: SizePreset p })
            {
                _suppress = true;
                WidthBox.Text = p.Width.ToString();
                HeightBox.Text = p.Height.ToString();
                _suppress = false;
            }
        }

        private void CustomSize_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppress) return;
            PresetList.SelectedItem = null; // typing a custom size deselects any preset row
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSize(out int w, out int h))
            {
                MessageBox.Show(this, "Please enter a valid width and height first.", "New Picture", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            _customPresets.RemoveAll(p => p.Width == w && p.Height == h); // avoid exact duplicates
            _customPresets.Add(new SizePreset { Name = "Custom", Width = w, Height = h });
            CanvasSizePresetStore.Save(_customPresets);
            RefreshList();
        }

        private bool TryGetSize(out int w, out int h)
        {
            w = h = 0;
            return int.TryParse(WidthBox.Text, out w) && w > 0 && w <= 10000
                && int.TryParse(HeightBox.Text, out h) && h > 0 && h <= 10000;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSize(out int w, out int h))
            {
                MessageBox.Show(this, "Please enter a valid width and height.", "New Picture", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            ResultWidth = w;
            ResultHeight = h;
            DialogResult = true;
        }
    }
}
