using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using BA.Settings.Rooms;
using BA.UI.Helpers;
using BA.UI.Rooms;

namespace BATools.Rooms.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class Cmd_TransferAreaValuesToRooms_Settings : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var doc = uiapp.ActiveUIDocument?.Document;
            var settings = AreaTransferSettings.Load();

            var dlg = new AreaTransferSettingsWindow(settings, doc);
            if (RevitWindowHelper.ShowDialogOwnedByRevit(dlg, uiapp) != true)
                return Result.Cancelled;

            settings.Save();
            return Result.Succeeded;
        }
    }
}