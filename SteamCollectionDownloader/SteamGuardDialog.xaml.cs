using System.Windows;
using System.Windows.Input;

namespace SteamDownloader
{
    public partial class SteamGuardDialog : Window
    {
        public string Code => CodeTextBox.Text.Trim();

        public SteamGuardDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => CodeTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                return;
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, new RoutedEventArgs());
            }
        }
    }
}
