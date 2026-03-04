using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

using BA.BIM.Core.Annotations;
using BA.UI.Common;

namespace BA.BIM.Commands.Anno
{
    [Transaction(TransactionMode.Manual)]
    public class ArrangeAnnotationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            UIApplication uiapp = c.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            try
            {
                IList<Reference> picked = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new ViewSpecificAnnotationSelectionFilter(view.Id),
                    "Window-select annotations. ESC to cancel.");

                var elems = picked
                    .Select(r => doc.GetElement(r))
                    .Where(e => e != null)
                    .Distinct(new ElementIdComparer())
                    .ToList();

                if (elems.Count < 2)
                {
                    TaskDialog.Show("BA", "Pick at least 2 annotations.");
                    return Result.Cancelled;
                }

                var cfg = ArrangeAnnotationsDialog.GetConfig();
                if (cfg == null || cfg.Mode == ArrangeMode.Cancel)
                    return Result.Cancelled;

                // For guide line mode, acquire guide first (2 points in view plane)
                GuideLine2D? guide = null;
                if (cfg.Mode == ArrangeMode.SnapToGuideLine)
                {
                    XYZ p0 = uidoc.Selection.PickPoint("Pick first point of guide line");
                    XYZ p1 = uidoc.Selection.PickPoint("Pick second point of guide line");

                    var plane = ViewPlane2D.FromView(view);
                    guide = new GuideLine2D(plane.ToUV(p0), plane.ToUV(p1));
                    if (guide == null)
                        return Result.Cancelled;
                }

                // Build items
                var items = new List<AnnoItem>();
                foreach (var e in elems)
                {
                    var bb = e.get_BoundingBox(view);
                    if (bb == null) continue;

                    items.Add(new AnnoItem(e, bb));
                }

                if (items.Count < 2)
                {
                    TaskDialog.Show("BA", "No movable annotations detected in this view.");
                    return Result.Cancelled;
                }

                using (Transaction t = new Transaction(doc, $"Arrange: {cfg.Mode}"))
                {
                    t.Start();

                    var plane = ViewPlane2D.FromView(view);

                    var report = AnnoArrangeOps.Execute(doc, view, plane, items, cfg, guide);

                    t.Commit();

                    // Optional: show report for collision solver
                    if (cfg.Mode == ArrangeMode.ResolveCollisions)
                    {
                        TaskDialog.Show("BA - Arrange report",
                            $"Mode: {cfg.Mode}\n" +
                            $"Elements: {report.Total}\n" +
                            $"Initially colliding: {report.InitiallyColliding}\n" +
                            $"Moved: {report.Moved}\n" +
                            $"Iterations: {report.Iterations}\n" +
                            $"Remaining collisions: {report.RemainingCollisions}");
                    }
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }
        private sealed class ElementIdComparer : IEqualityComparer<Element>
        {
            public bool Equals(Element x, Element y)
                => (x?.Id?.Value ?? -1) == (y?.Id?.Value ?? -2);

            public int GetHashCode(Element obj)
                => unchecked((int)(obj?.Id?.Value ?? 0));
        }
    }

    internal class ViewSpecificAnnotationSelectionFilter : ISelectionFilter
    {
        private readonly ElementId _viewId;
        public ViewSpecificAnnotationSelectionFilter(ElementId viewId) => _viewId = viewId;

        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            if (!elem.ViewSpecific) return false;
            if (elem.Pinned) return false;

            // sheet safety: don’t move stuff owned by another view
            if (elem.OwnerViewId != ElementId.InvalidElementId && elem.OwnerViewId != _viewId)
                return false;

            return true;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}