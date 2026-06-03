using System.Windows;
using BA_Tools.ScheduleExporter.ViewModels;

namespace BA_Tools.ScheduleExporter.Views
{
    public partial class ImportPreviewWindow : Window
    {
        public ImportPreviewViewModel ViewModel { get; }

        public ImportPreviewWindow(ImportPreviewViewModel viewModel)
        {
            InitializeComponent();
            ViewModel   = viewModel;
            DataContext = viewModel;
            viewModel.CloseRequested += () => Close();
        }
    }
}
