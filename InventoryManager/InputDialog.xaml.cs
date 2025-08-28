using System.Windows;

namespace InventoryManager
{
    public partial class InputDialog : Window
    {
        public string ResponseText => ResponseTextBox.Text;

        public InputDialog(string prompt, string title)
        {
            InitializeComponent();
            this.Title = title;
            PromptTextBlock.Text = prompt;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ResponseTextBox.Text))
            {
                this.DialogResult = true;
            }
            else
            {
                MessageBox.Show("Deskripsi tidak boleh kosong.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
