using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.Core.Rooms;
using BA.Filters;
using System;
using System.Collections.Generic;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Commands.Rooms
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_AxisToRoom_Local : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            var uidoc = c.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            if (doc.ActiveView == null || doc.ActiveView.ViewType == ViewType.ThreeD)
            {
                TaskDialog.Show("Axis To Room", "Open a 2D view (plan/section/elevation) to place detail components.");
                return Result.Cancelled;
            }

            IList<Reference> refs;
            try
            {
                refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new RoomTagSelectionFilter(),
                    "Select Room Tags");
            }
            catch
            {
                return Result.Cancelled;
            }

            var rooms = AxisToRoomService.GetLocalRoomsFromRoomTags(doc, refs);
            if (rooms.Count == 0)
            {
                TaskDialog.Show("Axis To Room", "No rooms found from selected tags.");
                return Result.Cancelled;
            }

            int placed = 0;

            using (var t = new Transaction(doc, "BA – Axis To Room (Local)"))
            {
                t.Start();

                // IMPORTANT: load/activate under the same transaction
                var symbol = FamilySymbolUtils.FindDetailSymbol(
                    doc,
                    familyName: "BA_Axis",
                    symbolName: null,
                    loadIfMissing: true,
                    familyFileNameOrRelativePath: "BA_Axis.rfa",
                    activateIfFound: true);

                if (symbol == null)
                {
                    t.RollBack();
                    TaskDialog.Show("Axis To Room", "Detail family 'BA_Axis' could not be found/loaded.");
                    return Result.Failed;
                }

                foreach (var r in rooms)
                {
                    var inst = DetailPlacer.PlaceInLocalRoomCenterSized(doc, doc.ActiveView, symbol, r, xParamName: "x", yParamName: "y");
                    if (inst != null) placed++;
                }

                t.Commit();
            }

            TaskDialog.Show("Axis To Room", $"Placed: {placed}");
            return Result.Succeeded;
        }
    }
}
