using System.Windows;
using BA.Families.ViewModels;

namespace BA.Families.Views
{
    public partial class SaveFamiliesView : Window
    {
        public SaveFamiliesView(SaveFamiliesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}