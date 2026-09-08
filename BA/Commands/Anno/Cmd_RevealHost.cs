using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.BAApplication;
using BA.Zoom.Helpers;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_RevealHost : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }
        public static Result Run(UIApplication uiApp, ref string message)
        {
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }
            var doc = uiDoc.Document;
            // ---------------------------------------------------------------- //
            //  Pick exactly one element.
            // ---------------------------------------------------------------- //
            Reference pickedRef;
            try
            {
                pickedRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    "Pick a dimension, hosted element, or stacked wall segment");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            var element = doc.GetElement(pickedRef.ElementId);
            if (element == null)
            {
                message = "Selected element could not be resolved.";
                return Result.Failed;
            }
            // ---------------------------------------------------------------- //
            //  Resolve host / references.
            // ---------------------------------------------------------------- //
            var targetIds = new HashSet<ElementId>();
            switch (element)
            {
                case Dimension dimension:
                    CollectDimensionReferences(dimension, targetIds);
                    break;
                case FamilyInstance familyInstance:
                    if (familyInstance.Host != null)
                        targetIds.Add(familyInstance.Host.Id);
                    break;
                case Wall wall:
                    if (wall.StackedWallOwnerId != ElementId.InvalidElementId)
                        targetIds.Add(wall.StackedWallOwnerId);
                    break;
            }
            targetIds.Remove(element.Id);
            if (targetIds.Count == 0)
            {
                TaskDialog.Show("Reveal Host",
                    $"No host or reference found for this " +
                    $"{element.Category?.Name ?? "element"}.");
                return Result.Succeeded;
            }
            // ---------------------------------------------------------------- //
            //  If the active view is plan-like and cropped, make sure the crop
            //  box does not clip ShowElements the same way an active crop
            //  clips ZoomAndCenterRectangle in the Zoom commands. ShowElements
            //  will not pan or zoom past an active crop, so a host outside it
            //  reports success while showing nothing. Only handled for plan
            //  like views here; a 3D view, section, or elevation has a crop
            //  box that is not necessarily axis aligned to world XY and needs
            //  a different containment test, not covered by this check.
            // ---------------------------------------------------------------- //
            bool cropWasDisabled = false;
            string cropLockedMessage = null;
            var activeView = doc.ActiveView;
            if (ZoomRevitHelper.IsPlanLike(activeView) &&
                TryGetUnionXYBounds(doc, activeView, targetIds, out XYZ minXY, out XYZ maxXY))
            {
                ZoomGeometryHelper.NormalizeAndBufferRectangle(ref minXY, ref maxXY, 600);
                cropWasDisabled = ZoomCropHelper.EnsureRectangleVisible(doc, activeView, minXY, maxXY, out cropLockedMessage, out string adjustmentNote);
                if (cropLockedMessage != null)
                    TaskDialog.Show("Reveal Host", cropLockedMessage);
            }
            // ---------------------------------------------------------------- //
            //  Select and zoom to results.
            // ---------------------------------------------------------------- //
            try
            {
                uiDoc.Selection.SetElementIds(targetIds);
                uiDoc.ShowElements(targetIds);
            }
            catch (Exception ex)
            {
                message = $"Failed to select or show referenced elements: {ex.Message}";
                AppLogger.LogError("Cmd_RevealHost.Execute", ex);
                return Result.Failed;
            }
            AppLogger.LogInfo(
                $"Cmd_RevealHost: revealed {targetIds.Count} reference(s) " +
                $"for element {element.Id.Value}." +
                (cropWasDisabled ? " Crop View was disabled because the target was outside it." : ""));
            return Result.Succeeded;
        }
        // ------------------------------------------------------------------ //
        //  HELPERS
        // ------------------------------------------------------------------ //
        private static void CollectDimensionReferences(
            Dimension dimension,
            HashSet<ElementId> targetIds)
        {
            var references = dimension.References;
            if (references == null) return;
            foreach (Reference reference in references)
            {
                if (reference == null) continue;
                var id = reference.ElementId;
                if (id != null && id != ElementId.InvalidElementId)
                    targetIds.Add(id);
            }
        }

        /// <summary>
        /// Unions the view specific XY bounding boxes of every target element into a single
        /// flattened (Z = 0) rectangle, consistent with ZoomGeometryHelper's convention.
        /// Elements with no bounding box in this view (hidden, wrong phase, workset off, etc.)
        /// are skipped. Returns false if none of the targets produced a usable bounding box.
        /// </summary>
        private static bool TryGetUnionXYBounds(
            Document doc,
            Autodesk.Revit.DB.View view,
            IEnumerable<ElementId> ids,
            out XYZ minXY,
            out XYZ maxXY)
        {
            bool inited = false;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;

            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;

                var bb = el.get_BoundingBox(view);
                if (bb == null) continue;

                double x0 = Math.Min(bb.Min.X, bb.Max.X);
                double y0 = Math.Min(bb.Min.Y, bb.Max.Y);
                double x1 = Math.Max(bb.Min.X, bb.Max.X);
                double y1 = Math.Max(bb.Min.Y, bb.Max.Y);

                if (!inited)
                {
                    minX = x0; minY = y0; maxX = x1; maxY = y1;
                    inited = true;
                }
                else
                {
                    if (x0 < minX) minX = x0;
                    if (y0 < minY) minY = y0;
                    if (x1 > maxX) maxX = x1;
                    if (y1 > maxY) maxY = y1;
                }
            }

            if (!inited)
            {
                minXY = maxXY = XYZ.Zero;
                return false;
            }

            minXY = new XYZ(minX, minY, 0);
            maxXY = new XYZ(maxX, maxY, 0);
            return true;
        }
    }
}