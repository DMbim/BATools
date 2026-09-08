using Autodesk.Revit.UI;
using BA.SelectionManager.ViewModels;
using BA.SelectionManager.Views;

namespace BA.SelectionManager.Panes
{
    public class SelectionManagerDockablePane : IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new System.Guid("C1D2E3F4-A5B6-7890-CDEF-123456789ABC"));

        private SelectionManagerView? _view;
        private SelectionManagerViewModel? _viewModel;

        public SelectionManagerViewModel? ViewModel => _viewModel;

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _viewModel = new SelectionManagerViewModel();
            _view = new SelectionManagerView
            {
                DataContext = _viewModel
            };
            data.FrameworkElement = _view;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }

        public void LoadForDocument(string fingerprint)
        {
            _viewModel?.LoadForDocument(fingerprint);
        }
    }
}