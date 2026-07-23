// File: BA/UI/CurveToElement/CurveToElementWindow.xaml.cs
// Action: REPLACE (full file)

using System.Windows;
using BA.ViewModels.CurveToElement;

namespace BA.UI.CurveToElement
{
    /// <summary>
    /// Code-behind stays thin, matching LedgerSettingsWindow's convention: no Revit API calls
    /// here. This window is opened via Show() (non-modal) so the user can still interact with
    /// the Revit view/selection while it's open - RequestClose must NOT set DialogResult, since
    /// DialogResult assignment on a Show()-opened window throws InvalidOperationException at
    /// runtime. If this window is ever changed to ShowDialog() (modal), DialogResult = result
    /// can be restored in place of the Close() call below.
    /// </summary>
    public partial class CurveToElementWindow : Window
    {
        private readonly CurveToElementWindowViewModel _viewModel;

        public CurveToElementWindow(CurveToElementWindowViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.RequestClose = result =>
            {
                Close(); // <- CHANGED back: non-modal window, DialogResult is illegal here
            };

            Closed += (sender, args) => _viewModel.Dispose();
        }
    }
}