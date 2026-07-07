using System.Windows;
using BA.QA.FamilyVersioning.ViewModels;

namespace BA.QA.FamilyVersioning.Views
{
    /// <summary>
    /// Code-behind for the Family Versioning Setup window. Kept intentionally thin,
    /// all logic lives in FamilyVersioningSetupViewModel, this class only wires the
    /// ViewModel's CloseRequested event to actually closing the window, matching the
    /// CloseRequested/UserConfirmed pattern already established by
    /// ExportScheduleViewModel elsewhere in BA Tools.
    /// </summary>
    public partial class FamilyVersioningSetupWindow : Window
    {
        public FamilyVersioningSetupWindow(FamilyVersioningSetupViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
            viewModel.CloseRequested += () => Close();
        }
    }
}
