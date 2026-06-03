using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BATools.SelectionManager.ViewModels;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace BATools.SelectionManager.Views
{
    public partial class SelectionManagerView : UserControl
    {
        public SelectionManagerView()
        {
            InitializeComponent();
        }

        private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.Tag is SetRowViewModel vm)
                vm.CommitRenameCommand?.Execute(null);
        }

        private void RenameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox tb || tb.Tag is not SetRowViewModel vm) return;

            if (e.Key == Key.Enter)
            {
                vm.CommitRenameCommand?.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.IsRenaming = false;
                e.Handled = true;
            }
        }
    }
}