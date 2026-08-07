using System.Windows;
using BA.ViewModels.Export;

namespace BA.Views.Export
{
    public partial class BookletWindow : Window
    {
        public BookletWindow(BookletViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
