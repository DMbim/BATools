using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.Standards;

namespace BA.Commands.Standards
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_SubcategoryAuditor : IExternalCommand
    {
        private static SubcategoryAuditorWindow _window;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            if (_window == null || !_window.IsLoaded)
            {
                _window = new SubcategoryAuditorWindow(uiApp);
                _window.Show();
            }
            else
            {
                if (_window.WindowState == System.Windows.WindowState.Minimized)
                    _window.WindowState = System.Windows.WindowState.Normal;

                _window.Activate();
            }

            _window.RequestScan();

            return Result.Succeeded;
        }
    }
}