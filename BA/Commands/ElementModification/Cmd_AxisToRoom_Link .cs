using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.Core.Rooms;
using BA.Filters;
using BA.Settings.Rooms;
using BA.UI.Rooms;
using System;
using System.Collections.Generic;
using System.Windows.Input;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Commands.Rooms
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_AxisToRoom_Link : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            var uiapp = c.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;

            if (doc == null) return Result.Failed;

            // Detail components need a view that supports them (not 3D)
            if (doc.ActiveView == null || doc.ActiveView.ViewType == ViewType.ThreeD)
            {
                TaskDialog.Show("Axis To Room", "Open a 2D view (plan/section/elevation) to place detail components.");
                return Result.Cancelled;
            }

            var settings = AxisToRoomSettings.LoadWithLegacyMigration();

            // SHIFT = configure
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var dlg = new AxisToRoomSettingsWindow(c, settings) { Owner = System.Windows.Application.Current?.MainWindow };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                settings.Save();
            }

            var link = LinkResolver.FindLinkInstance(doc, settings.SelectedLinkInstanceUniqueId, settings.SelectedLinkInstanceName);
            if (link == null)
            {
                TaskDialog.Show("Axis To Room", "No valid Revit link saved. Hold SHIFT to configure.");
                return Result.Cancelled;
            }

            // Pick host room tags (that tag linked rooms)
            IList<Reference> refs;
            try
            {
                refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new RoomTagSelectionFilter(),
                    "Select Room Tags (tagging linked rooms)");
            }
            catch
            {
                return Result.Cancelled;
            }

            var linkedRooms = AxisToRoomService.GetLinkedRoomsFromHostRoomTags(doc, refs, link);
            if (linkedRooms.Count == 0)
            {
                TaskDialog.Show("Axis To Room", "No linked rooms found from selected tags.");
                return Result.Cancelled;
            }

            var famName = settings.PlaceDimensionVariant ? "BA_Axis_Dim" : "BA_Axis";
            var symbol = FamilySymbolUtils.FindDetailSymbol(doc, famName);
            if (symbol == null)
            {
                TaskDialog.Show("Axis To Room", $"Detail family '{famName}' is not loaded in this project.");
                return Result.Failed;
            }

            int placed = 0;

            using (var t = new Transaction(doc, "BA – Axis To Room (Link)"))
            {
                t.Start();

                foreach (var r in linkedRooms)
                {
                    var inst = DetailPlacer.PlaceInLinkedRoomCenter(doc, doc.ActiveView, symbol, r, link);
                    if (inst != null) placed++;
                }

                t.Commit();
            }

            TaskDialog.Show("Axis To Room", $"Placed: {placed}");
            return Result.Succeeded;
        }
    }
}
