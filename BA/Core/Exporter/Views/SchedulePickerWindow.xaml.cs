using System.Windows;
using BA_Tools.ScheduleExporter.ViewModels;

namespace BA_Tools.ScheduleExporter.Views
{
    public partial class SchedulePickerWindow : Window
    {
        public SchedulePickerViewModel ViewModel { get; }

        public SchedulePickerWindow(SchedulePickerViewModel viewModel)
        {
            InitializeComponent();
            ViewModel   = viewModel;
            DataContext = viewModel;
            viewModel.CloseRequested += () => Close();
        }
    }
}
