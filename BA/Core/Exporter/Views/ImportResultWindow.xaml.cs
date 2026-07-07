using System.Windows;
using BA_Tools.ScheduleExporter.ViewModels;

namespace BA_Tools.ScheduleExporter.Views
{
    public partial class ImportResultWindow : Window
    {
        public ImportResultViewModel ViewModel { get; }

        public ImportResultWindow(ImportResultViewModel viewModel)
        {
            InitializeComponent();
            ViewModel   = viewModel;
            DataContext = viewModel;
            viewModel.CloseRequested += () => Close();
        }
    }
}
