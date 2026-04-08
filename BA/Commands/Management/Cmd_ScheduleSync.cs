using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BA
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ScheduleSync : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var window = new SyncWindow(commandData);
            window.Show();

            return Result.Succeeded;
        }
    }
}