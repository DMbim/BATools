using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.Management;

namespace BA.Commands.Management
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ProjectToolsHub : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            try
            {
                var wnd = new ProjectToolsHubWindow(c)
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                wnd.ShowDialog();
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("BA – Project Tools", ex.Message);
                return Result.Failed;
            }
        }
    }
}
