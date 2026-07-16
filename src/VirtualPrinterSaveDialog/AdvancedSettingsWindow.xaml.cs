using System.Windows;
using System.Windows.Controls;

namespace VirtualPrinter.SaveDialog
{
    public partial class AdvancedSettingsWindow : Window
    {
        public int JpegQuality
        {
            get => (int)QualitySlider.Value;
            set => QualitySlider.Value = value;
        }

        public string PdfVersion
        {
            get
            {
                var item = PdfVersionBox.SelectedItem as ComboBoxItem;
                return item?.Content?.ToString() ?? "1.7";
            }
            set
            {
                foreach (ComboBoxItem item in PdfVersionBox.Items)
                {
                    if (item.Content.ToString() == value)
                    { PdfVersionBox.SelectedItem = item; break; }
                }
            }
        }

        public bool EmbedFonts
        {
            get => EmbedFontsCheck.IsChecked == true;
            set => EmbedFontsCheck.IsChecked = value;
        }

        public AdvancedSettingsWindow()
        {
            InitializeComponent();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
