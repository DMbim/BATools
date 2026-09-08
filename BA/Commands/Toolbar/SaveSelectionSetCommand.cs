using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.SelectionManager.Infrastructure;
using BA.SelectionManager.Views;

namespace BA.SelectionManager.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SaveSelectionSetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var dialog = new SaveSetDialog();
            dialog.Owner = System.Windows.Application.Current?.MainWindow;

            if (dialog.ShowDialog() != true) return Result.Cancelled;

            string name = dialog.SetName;
            if (string.IsNullOrWhiteSpace(name)) return Result.Cancelled;

            SelectionManagerBridge.Instance.RequestSaveCurrentSelection(name);
            return Result.Succeeded;
        }
    }
}
