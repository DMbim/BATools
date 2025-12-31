using System.Windows;

namespace BA.UI.Views.Warnings
{
    public partial class ImportCadWarningWindow : Window
    {
        public bool SuppressForSession { get; private set; }

        public ImportCadWarningWindow()
        {
            InitializeComponent();
        }

        private void UseLinkCad_Click(object sender, RoutedEventArgs e)
        {
            SuppressForSession = chkSuppress.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SuppressForSession = chkSuppress.IsChecked == true;
            DialogResult = false;
            Close();
        }
    }
}
