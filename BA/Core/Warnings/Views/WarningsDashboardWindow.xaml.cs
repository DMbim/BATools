// FILE: BA_Tools/Warnings/Views/WarningsDashboardWindow.xaml.cs
using Autodesk.Revit.UI;
using BA.Warnings.ViewModels;

namespace BA.Warnings.Views
{
    public partial class WarningsDashboardWindow : System.Windows.Window
    {
        private static WarningsDashboardWindow _instance;

        private readonly WarningsDashboardViewModel _viewModel;

        private WarningsDashboardWindow(UIApplication uiApp)
        {
            InitializeComponent();

            _viewModel = new WarningsDashboardViewModel(uiApp);
            DataContext = _viewModel;

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.Owner = uiApp.MainWindowHandle;

            Closed += (s, e) =>
            {
                _viewModel.Dispose();
                _instance = null;
            };
        }
        private void WarningRow_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is BA.Warnings.Models.WarningItem item)
            {
                _viewModel.SelectedWarning = item;
            }
        }
        public static WarningsDashboardWindow GetOrCreate(UIApplication uiApp)
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new WarningsDashboardWindow(uiApp);
            }
            return _instance;
        }
    }
}