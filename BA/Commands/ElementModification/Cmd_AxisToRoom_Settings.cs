using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using BA.Settings.Rooms;
using BA.UI.Helpers;
using BA.UI.Rooms;

namespace BA.Commands.Rooms
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_AxisToRoom_Settings : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            var uiapp = c.Application;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            var settings = AxisToRoomSettings.LoadWithLegacyMigration();

            var dlg = new AxisToRoomSettingsWindow(c, settings);
            if (RevitWindowHelper.ShowDialogOwnedByRevit(dlg, uiapp) != true)
                return Result.Cancelled;

            settings.Save();
            return Result.Succeeded;
        }
    }
}
