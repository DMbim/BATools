using System.Windows;
using System.Windows.Input;

namespace BATools.SelectionManager.Views
{
    public partial class SaveSetDialog : Window
    {
        public string SetName { get; private set; } = string.Empty;

        public SaveSetDialog()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += (_, _) => TxtSetName.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SetName = TxtSetName.Text.Trim();
            if (string.IsNullOrEmpty(SetName)) return;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TxtSetName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Save_Click(sender, e);
            if (e.Key == Key.Escape) Cancel_Click(sender, e);
        }
    }
}