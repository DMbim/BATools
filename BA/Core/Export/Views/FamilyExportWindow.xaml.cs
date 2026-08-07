using System.Windows;
using BA.ViewModels.Export;

namespace BA.Views.Export
{
    public partial class FamilyExportWindow : Window
    {
        public FamilyExportWindow(FamilyExportViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
