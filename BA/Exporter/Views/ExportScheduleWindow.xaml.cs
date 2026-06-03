using System.Windows;
using BA_Tools.ScheduleExporter.ViewModels;

namespace BA_Tools.ScheduleExporter.Views
{
    public partial class ExportScheduleWindow : Window
    {
        public ExportScheduleViewModel ViewModel { get; }

        public ExportScheduleWindow(ExportScheduleViewModel viewModel)
        {
            InitializeComponent();
            ViewModel   = viewModel;
            DataContext = viewModel;
            viewModel.CloseRequested += () => Close();
        }
    }
}
