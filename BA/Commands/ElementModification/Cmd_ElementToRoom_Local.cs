using System;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using BA.Core.Classification;
using BA.Core.Rooms;
using BA.Settings.Rooms;
using BA.UI.Rooms;


namespace BA.Commands.Rooms
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_ElementToRoom_Local : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            var uiapp = c.Application;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            var settings = ElementToRoomSettings.LoadWithLegacyMigration();

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var dlg = new ElementToRoomSettingsWindow(c, settings, linkMode: false);
                if (dlg.ShowDialog() != true) return Result.Cancelled;
                settings.Save();
            }

            if (string.IsNullOrWhiteSpace(settings.SelectedCategoryToken) ||
                string.IsNullOrWhiteSpace(settings.DestinationParameter) ||
                string.IsNullOrWhiteSpace(settings.SourceParameter))
            {
                TaskDialog.Show("Element → Room", "Settings missing. Hold SHIFT to configure.");
                return Result.Cancelled;
            }

            var category = CategoryResolver.TryResolveCategory(doc, settings.SelectedCategoryToken);
            if (category == null)
            {
                TaskDialog.Show("Element → Room", $"Category '{settings.SelectedCategoryToken}' not found.");
                return Result.Cancelled;
            }

            using (var t = new Transaction(doc, "BA – Element To Room (Local)"))
            {
                t.Start();
                var stats = ElementToRoomService.AssignFromLocalRooms(
                    doc, category, settings.SourceParameter, settings.DestinationParameter);
                t.Commit();

                TaskDialog.Show("Element → Room (Local)",
                    $"Considered: {stats.ElementsConsidered}\n" +
                    $"Written: {stats.ElementsWritten}\n" +
                    $"No point: {stats.ElementsNoPoint}\n" +
                    $"No room: {stats.ElementsNoRoom}\n" +
                    $"Missing params: {stats.ElementsNoParams}");
            }

            return Result.Succeeded;
        }
    }
}
