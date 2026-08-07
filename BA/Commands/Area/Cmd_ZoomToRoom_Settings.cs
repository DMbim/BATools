using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using BA.UI.Helpers;
using BATools.Zoom.Settings;
using BATools.Zoom.Views;

namespace BATools.Zoom.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class Cmd_ZoomToRoom_Settings : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;

            var settings = ZoomToRoomSettings.Load();

            var dlg = new ZoomToRoomSettingsWindow(commandData, settings);
            if (RevitWindowHelper.ShowDialogOwnedByRevit(dlg, uiapp) != true)
                return Result.Cancelled;

            settings.Save();
            return Result.Succeeded;
        }
    }
}
