using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using BA.Settings.Rooms;
using BA.UI.Rooms;

namespace BA.Commands.Rooms
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_ElementToRoom_Settings : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            var uiapp = c.Application;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            var settings = ElementToRoomSettings.LoadWithLegacyMigration();

            // Mode (Local vs Link) is now a radio toggle inside the window itself,
            // defaulting to whichever mode has saved data. No linkMode argument
            // needed here anymore.
            var dlg = new ElementToRoomSettingsWindow(c, settings);
            if (dlg.ShowDialog() != true)
                return Result.Cancelled;

            settings.Save();
            return Result.Succeeded;
        }
    }
}
