using System.Windows;

namespace BA.UI.Common
{
    public partial class InputDialog : Window
    {
        public string Value => Txt.Text ?? string.Empty;

        public InputDialog(string title, string label, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            Lbl.Text = label;
            Txt.Text = defaultValue ?? "";
            Txt.SelectAll();
            Txt.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
