using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.Core.Rooms;
using BA.Filters;
using BA.Settings.Rooms;
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

            // Local previously had no settings dependency at all -- now reads
            // AxisToRoomSettings for the dimension toggle and offsets, same as Link.
            var settings = AxisToRoomSettings.LoadWithLegacyMigration();

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

            double offsetX = UnitUtils.ConvertToInternalUnits(settings.DimensionOffsetXMm, UnitTypeId.Millimeters);
            double offsetY = UnitUtils.ConvertToInternalUnits(settings.DimensionOffsetYMm, UnitTypeId.Millimeters);

            int placed = 0;
            int dimensioned = 0;

            using (var t = new Transaction(doc, "BA \u2013 Axis To Room (Local)"))
            {
                t.Start();
                try
                {
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
                        var inst = DetailPlacer.PlaceInLocalRoomCenterSized(
                            doc, doc.ActiveView, symbol, r,
                            out var roomMin, out var roomMax,
                            xParamName: "x", yParamName: "y");

                        if (inst == null) continue;
                        placed++;

                        if (settings.PlaceDimensionVariant)
                        {
                            AxisDimensionService.CreateAxisDimensions(
                                doc, doc.ActiveView, inst, roomMin, roomMax, offsetX, offsetY);
                            dimensioned++;
                        }
                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    // Whole-run rollback by design: if dimensioning fails partway through
                    // (e.g. a family reload wiped the named references), every axis placed
                    // in this run rolls back too, rather than leaving some rooms axed and
                    // dimensioned and others not. Flagging this choice -- if you'd rather
                    // have per-room fault tolerance (keep what succeeded, report what didn't),
                    // that's a different design and needs a per-room try/catch instead.
                    t.RollBack();
                    TaskDialog.Show("Axis To Room", $"Failed while placing/dimensioning axes: {ex.Message}");
                    return Result.Failed;
                }
            }

            var summary = $"Placed: {placed}";
            if (settings.PlaceDimensionVariant)
                summary += $"\nDimensioned: {dimensioned}";

            TaskDialog.Show("Axis To Room", summary);
            return Result.Succeeded;
        }
    }
}
