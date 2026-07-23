using System.Windows;
using BA.ViewModels.Export;

namespace BA.Views.Export
{
    public partial class ExportSettingsWindow : Window
    {
        public ExportSettingsWindow(ExportSettingsViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
            viewModel.RequestClose = Close;
        }
    }
}
