using System;
using System.Windows;

namespace BA.UI.LoadedFamilyBrowser
{
    public partial class RenamePromptWindow : Window
    {
        public string ResultName { get; private set; } = string.Empty;

        public RenamePromptWindow(string promptText, string initialValue, IntPtr ownerHandle)
        {
            InitializeComponent();

            PromptLabel.Text = promptText;
            NameTextBox.Text = initialValue;
            NameTextBox.SelectAll();

            if (ownerHandle != IntPtr.Zero)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = ownerHandle;
            }

            Loaded += (_, _) => NameTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string trimmed = NameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                MessageBox.Show(this, "Name cannot be empty.", "BA Tools",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultName = trimmed;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}