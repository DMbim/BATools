using System.Windows;

namespace BA.UI
{
    public partial class RevisionDetailsWindow : Window
    {
        public string Description => TxtDescription.Text.Trim();
        public string DateText => TxtDate.Text.Trim();

        public RevisionDetailsWindow(
            string header,
            string initialDescription = "",
            string initialDateText = "")
        {
            InitializeComponent();

            LblHeader.Text = header;
            TxtDescription.Text = initialDescription ?? "";
            TxtDate.Text = initialDateText ?? "";

            Loaded += (s, e) =>
            {
                TxtDescription.Focus();
                TxtDescription.SelectAll();
            };
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
