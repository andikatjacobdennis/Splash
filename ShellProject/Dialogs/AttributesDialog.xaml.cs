using System;
using System.Windows;
using System.Windows.Controls;

namespace PaintClone.Dialogs
{
    public partial class AttributesDialog : Window
    {
        public int NewWidth { get; private set; }
        public int NewHeight { get; private set; }
        public bool ClearRequested { get; private set; }
        public double NewDpi { get; private set; }

        public AttributesDialog(int width, int height, double dpi)
        {
            InitializeComponent();
            WidthBox.Text = width.ToString();
            HeightBox.Text = height.ToString();
            DpiBox.Text = ((int)Math.Round(dpi)).ToString();
            WidthBox.TextChanged += Dpi_Changed;
            HeightBox.TextChanged += Dpi_Changed;
            UpdatePrintSize();
        }

        /// <summary>Shows the physical size the picture will print at, which is the only place the
        /// DPI value actually becomes visible - pixel dimensions alone don't tell you that.</summary>
        private void UpdatePrintSize()
        {
            if (PrintSizeText == null) return;
            if (int.TryParse(WidthBox.Text, out int w) && int.TryParse(HeightBox.Text, out int h)
                && double.TryParse(DpiBox.Text, out double d) && d > 0)
            {
                double win = w / d, hin = h / d;
                PrintSizeText.Text = $"Prints at {win:0.##} x {hin:0.##} in  ({win * 2.54:0.##} x {hin * 2.54:0.##} cm)";
            }
            else
            {
                PrintSizeText.Text = "";
            }
        }

        private void Dpi_Changed(object sender, TextChangedEventArgs e) => UpdatePrintSize();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(WidthBox.Text, out int w) || w <= 0 || w > 30000)
            {
                MessageBox.Show(this, "Please enter a valid width.", "Attributes", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (!int.TryParse(HeightBox.Text, out int h) || h <= 0 || h > 30000)
            {
                MessageBox.Show(this, "Please enter a valid height.", "Attributes", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (!double.TryParse(DpiBox.Text, out double dpi) || dpi <= 0 || dpi > 4800)
            {
                MessageBox.Show(this, "Please enter a valid resolution between 1 and 4800 DPI.", "Attributes", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            NewWidth = w;
            NewHeight = h;
            NewDpi = dpi;
            DialogResult = true;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ClearRequested = true;
            WidthBox.Text = "1";
            HeightBox.Text = "1";
        }
    }
}
