using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
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

            var link = LinkResolver.FindLinkInstance(doc, settings.SelectedLinkInstanceUniqueId, settings.SelectedLinkInstanceName);
            if (link == null)
            {
                TaskDialog.Show("Axis To Room",
                    "No valid Revit link saved. Use the Settings button on the Axis \u2192 Room pulldown to configure.");
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

            double offsetX = UnitUtils.ConvertToInternalUnits(settings.DimensionOffsetXMm, UnitTypeId.Millimeters);
            double offsetY = UnitUtils.ConvertToInternalUnits(settings.DimensionOffsetYMm, UnitTypeId.Millimeters);

            int placed = 0;
            int dimensioned = 0;

            using (var t = new Transaction(doc, "BA \u2013 Axis To Room (Link)"))
            {
                t.Start();
                try
                {
                    // <- CHANGED: symbol lookup moved INSIDE the transaction. It previously ran
                    // before t.Start(), which threw InvalidOperationException from
                    // FindDetailSymbol's doc.IsModifiable check whenever BA_Axis wasn't already
                    // loaded in the document -- Local never had this bug since its lookup was
                    // always inside its transaction; Link now matches that correct pattern.
                    // Also now points at the single unified "BA_Axis" family (was "BA_Axis_Dim"
                    // vs "BA_Axis" depending on settings.PlaceDimensionVariant).
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

                    foreach (var r in linkedRooms)
                    {
                        var inst = DetailPlacer.PlaceInLinkedRoomCenterSized(
                            doc, doc.ActiveView, symbol, r, link,
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
                    // Same whole-run rollback design as Local -- see its comment.
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
