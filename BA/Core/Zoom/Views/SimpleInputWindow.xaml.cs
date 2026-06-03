using System.Windows;

namespace BATools.Zoom.Views
{
    public partial class SimpleInputWindow : Window
    {
        /// <summary>
        /// The text entered by the user. Empty string if cancelled.
        /// </summary>
        public string InputText { get; private set; } = string.Empty;

        private SimpleInputWindow(string title, string prompt, string defaultText)
        {
            InitializeComponent();
            Title = title;
            TxtPrompt.Text = prompt;
            TxtInput.Text = defaultText;
            TxtInput.SelectAll();
            TxtInput.Focus();
        }

        /// <summary>
        /// Shows the dialog and returns the entered text, or empty string if cancelled.
        /// </summary>
        public static string Show(string title, string prompt, string defaultText)
        {
            var win = new SimpleInputWindow(title, prompt, defaultText);
            var result = win.ShowDialog();
            return result == true ? win.InputText : string.Empty;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            InputText = TxtInput.Text;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            InputText = string.Empty;
            DialogResult = false;
            Close();
        }
    }
}