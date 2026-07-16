using System;
using System.Windows;

namespace VirtualPrinter.Manager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, args) =>
            {
                var ex = args.Exception;
                string msg = $"Error: {ex.Message}\n";
                var inner = ex;
                while (inner.InnerException != null)
                {
                    inner = inner.InnerException;
                    msg += $"\n  -> {inner.GetType().Name}: {inner.Message}";
                }
                msg += $"\n\nOuter stack:\n{args.Exception.StackTrace}";
                msg += $"\n\nInner stack:\n{inner.StackTrace}";
                msg += $"\n\nAssembly: {System.Reflection.Assembly.GetExecutingAssembly().Location}";
                MessageBox.Show(msg, "VirtualPrinter", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
