using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BA.UI.ContentBrowser
{
    public partial class ContentBrowserWindow : Window
    {
        public ContentBrowserWindow()
        {
            InitializeComponent();
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ContentBrowserViewModel vm && vm.LoadSelectedCommand.CanExecute(null))
            {
                vm.LoadSelectedCommand.Execute(null);
            }
        }
    }
}