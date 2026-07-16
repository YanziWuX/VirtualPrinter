using System.Windows;

namespace VirtualPrinter.SaveDialog
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string text)
        {
            StatusText.Text = text;
        }
    }
}
