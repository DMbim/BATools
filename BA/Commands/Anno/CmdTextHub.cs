using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.TextHub;

namespace BA.Commands.TextHub
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CmdTextHub : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var doc = uiApp.ActiveUIDocument?.Document;

            if (doc == null)
            {
                TaskDialog.Show("BA", "No active document.");
                return Result.Cancelled;
            }

            TextHubWindow.ShowOrFocus(uiApp);
            return Result.Succeeded;
        }
    }
}