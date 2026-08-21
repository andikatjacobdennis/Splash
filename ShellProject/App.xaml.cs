using System.Windows;
using PaintClone.Services;

namespace PaintClone
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Apply the saved Dark/Light theme before the StartupUri window (MainWindow) is
            // constructed, so it renders in the right theme immediately instead of flashing the
            // App.xaml default and then swapping.
            ThemeManager.ApplySaved();
            base.OnStartup(e);
        }
    }
}
