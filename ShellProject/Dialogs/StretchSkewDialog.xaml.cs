using System.Windows;

namespace PaintClone.Dialogs
{
    public partial class StretchSkewDialog : Window
    {
        public double StretchX { get; private set; } = 100;
        public double StretchY { get; private set; } = 100;
        public double SkewX { get; private set; } = 0;
        public double SkewY { get; private set; } = 0;

        public StretchSkewDialog() => InitializeComponent();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(StretchXBox.Text, out double sx) || sx <= 0 || sx > 2000 ||
                !double.TryParse(StretchYBox.Text, out double sy) || sy <= 0 || sy > 2000)
            {
                MessageBox.Show(this, "Please enter a valid stretch percentage (1-2000).", "Stretch and Skew",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (!double.TryParse(SkewXBox.Text, out double kx) || kx <= -89 || kx >= 89 ||
                !double.TryParse(SkewYBox.Text, out double ky) || ky <= -89 || ky >= 89)
            {
                MessageBox.Show(this, "Please enter a valid skew angle (-89 to 89 degrees).", "Stretch and Skew",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            StretchX = sx; StretchY = sy; SkewX = kx; SkewY = ky;
            DialogResult = true;
        }
    }
}
