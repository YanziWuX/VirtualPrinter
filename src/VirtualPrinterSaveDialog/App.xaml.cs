using System.Windows;
using System.Windows.Threading;

namespace VirtualPrinter.SaveDialog
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Unexpected error: {args.Exception.Message}",
                    "VirtualPrinter", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
