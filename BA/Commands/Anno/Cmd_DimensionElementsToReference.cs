using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.Core.Dimensioning;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Commands.Dimensioning
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_DimensionElementsToReference : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            var uidoc = c.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            var view = doc.ActiveView;
            if (view == null || view.ViewType == ViewType.ThreeD)
            {
                TaskDialog.Show("Dimension To Reference", "Open a 2D view (plan/section/elevation) to place dimensions.");
                return Result.Cancelled;
            }

            // 1st prompt: elements to dimension
            IList<Reference> elementPicks;
            try
            {
                elementPicks = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    "Select elements to dimension, then press Finish");
            }
            catch
            {
                return Result.Cancelled;
            }

            if (elementPicks.Count == 0)
            {
                TaskDialog.Show("Dimension To Reference", "No elements selected.");
                return Result.Cancelled;
            }

            // 2nd prompt: what to dimension in relation to.
            // PointOnElement, not plain ObjectType.Reference: this is the pick mode that
            // reliably populates Reference.GlobalPoint, which auto-detection and the dimension
            // line both need. A face/edge-only pick doesn't guarantee a usable point the same
            // way. Not verified against every possible anchor type (wall face, grid, one of
            // the axis's own reference planes) -- report back anything that behaves oddly.
            Reference anchorRef;
            try
            {
                anchorRef = uidoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    "Select a reference to dimension relative to");
            }
            catch
            {
                return Result.Cancelled;
            }

            if (anchorRef.GlobalPoint == null)
            {
                TaskDialog.Show("Dimension To Reference",
                    "Could not resolve a point from the selected reference. Try picking a different point " +
                    "(an edge, face, or reference plane) rather than this exact spot.");
                return Result.Cancelled;
            }

            using (var t = new Transaction(doc, "BA \u2013 Dimension Elements To Reference"))
            {
                t.Start();
                try
                {
                    var result = DimensionToReferenceService.CreateDimensionToAnchor(
                        doc, view, anchorRef, anchorRef.GlobalPoint, (IReadOnlyList<Reference>)elementPicks);

                    if (result.Dimension == null)
                    {
                        t.RollBack();
                        var reasons = string.Join("\n", result.Skipped.ConvertAll(s => $"  - {s.Reason}"));
                        TaskDialog.Show("Dimension To Reference", "Could not create a dimension.\n" + reasons);
                        return Result.Cancelled;
                    }

                    t.Commit();

                    var summary = $"Dimension created: {result.SegmentCount} segment(s).";
                    if (result.Skipped.Count > 0)
                    {
                        summary += $"\n\nSkipped {result.Skipped.Count} element(s):\n" +
                                   string.Join("\n", result.Skipped.ConvertAll(s => $"  - {s.Reason}"));
                    }
                    TaskDialog.Show("Dimension To Reference", summary);
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    TaskDialog.Show("Dimension To Reference", $"Failed: {ex.Message}");
                    return Result.Failed;
                }
            }

            return Result.Succeeded;
        }
    }
}
