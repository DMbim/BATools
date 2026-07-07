// File: BA_Tools/UI/BimHub/Commands/OpenBimHubCommand.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.BimHub.Views;

namespace BA.UI.BimHub.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class OpenBimHubCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiApp = commandData.Application;

            // No 'using' — window owns its own lifetime via Closed event.
            // Dispose() is called by OnClosed. Adding 'using' here calls
            // Dispose() a second time after ShowDialog() returns, which
            // corrupts the window handle if any invoker callback is still
            // in flight.
            var window = new BimHubWindow(uiApp); // <- CHANGED: removed 'using'
            window.ShowDialog();
            return Result.Succeeded;
        }
    
    }
}