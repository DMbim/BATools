using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using Nice3point.Revit.Toolkit.External;
using BA.Core.Rooms;
using BA.Settings.Rooms;
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

            if (string.IsNullOrWhiteSpace(settings.SelectedCategoryToken) ||
                string.IsNullOrWhiteSpace(settings.DestinationParameter) ||
                string.IsNullOrWhiteSpace(settings.SourceParameter))
            {
                TaskDialog.Show("Element \u2192 Room",
                    "Settings missing. Open the Settings panel from the Element \u2192 Room pulldown to configure.");
                return Result.Cancelled;
            }

            var link = LinkResolver.FindLinkInstance(doc, settings.SelectedLinkInstanceUniqueId, settings.SelectedLinkInstanceName);
            if (link == null)
            {
                TaskDialog.Show("Element \u2192 Room",
                    "No valid Revit link saved. Open the Settings panel from the Element \u2192 Room pulldown to configure.");
                return Result.Cancelled;
            }

            var category = CategoryResolver.TryResolveCategory(doc, settings.SelectedCategoryToken);
            if (category == null)
            {
                TaskDialog.Show("Element \u2192 Room", $"Category '{settings.SelectedCategoryToken}' not found.");
                return Result.Cancelled;
            }

            using (var t = new Transaction(doc, "BA \u2013 Element To Room (Link)"))
            {
                t.Start();
                var stats = ElementToRoomService.AssignFromLinkedRooms(
                    doc, link, category, settings.SourceParameter, settings.DestinationParameter);
                t.Commit();
                TaskDialog.Show("Element \u2192 Room (Link)",
                    $"Considered: {stats.ElementsConsidered}\n" +
                    $"Written: {stats.ElementsWritten}\n" +
                    $"  (via source fallback: {stats.ElementsWrittenViaSourceFallback}, via destination fallback: {stats.ElementsWrittenViaDestinationFallback})\n" +
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
