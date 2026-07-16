using System.ComponentModel;
using System.Windows;

namespace VirtualPrinter.Manager
{
    public partial class ProgressWindow : Window
    {
        private bool _allowClose;

        public ProgressWindow(Window owner)
        {
            Owner = owner;
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (Owner != null)
                    Left = Owner.Left + (Owner.Width - Width) / 2;
                    Top = Owner.Top + (Owner.Height - Height) / 2;
            };
        }

        public void SetStatus(string text)
        {
            Dispatcher.Invoke(() => StatusText.Text = text);
        }

        public new void Close()
        {
            _allowClose = true;
            base.Close();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            e.Cancel = !_allowClose;
        }
    }
}
