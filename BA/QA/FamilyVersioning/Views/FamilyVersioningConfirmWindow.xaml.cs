using System.Windows;
using BA.QA.FamilyVersioning.ViewModels;

namespace BA.QA.FamilyVersioning.Views
{
    public partial class FamilyVersioningConfirmWindow : Window
    {
        public FamilyVersioningConfirmWindow(FamilyVersioningConfirmViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseRequested += () => Close();
        }
    }
}
