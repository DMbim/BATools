
using System;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using Nice3point.Revit.Toolkit.External;
using BA.Core.Rooms;
using BA.Settings.Rooms;
using BA.UI.Rooms;
using BA.Core.Classification;

namespace BA.Commands.Rooms
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_ElementToRoom_Link : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            var uiapp = c.Application;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            var settings = ElementToRoomSettings.LoadWithLegacyMigration();

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var dlg = new ElementToRoomSettingsWindow(c, settings, linkMode: true);
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

            var link = LinkResolver.FindLinkInstance(doc, settings.SelectedLinkInstanceUniqueId, settings.SelectedLinkInstanceName);
            if (link == null)
            {
                TaskDialog.Show("Element → Room", "No valid Revit link saved. Hold SHIFT to configure.");
                return Result.Cancelled;
            }

            var category = CategoryResolver.TryResolveCategory(doc, settings.SelectedCategoryToken);
            if (category == null)
            {
                TaskDialog.Show("Element → Room", $"Category '{settings.SelectedCategoryToken}' not found.");
                return Result.Cancelled;
            }

            using (var t = new Transaction(doc, "BA – Element To Room (Link)"))
            {
                t.Start();
                var stats = ElementToRoomService.AssignFromLinkedRooms(
                    doc, link, category, settings.SourceParameter, settings.DestinationParameter);
                t.Commit();

                TaskDialog.Show("Element → Room (Link)",
                    $"Considered: {stats.ElementsConsidered}\n" +
                    $"Written: {stats.ElementsWritten}\n" +
                    $"No point: {stats.ElementsNoPoint}\n" +
                    $"No room: {stats.ElementsNoRoom}\n" +
                    $"Missing params: {stats.ElementsNoParams}");
            }

            return Result.Succeeded;
        }
    }

    internal static class CategoryResolver
    {
        public static Category? TryResolveCategory(Document doc, string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            token = token.Trim();

            if (Enum.TryParse(token, true, out BuiltInCategory bic))
                return Category.GetCategory(doc, bic);

            return doc.Settings.Categories
                .Cast<Category>()
                .FirstOrDefault(c => c != null && c.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
        }
    }
}
