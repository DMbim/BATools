using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanElementCollector
    {
        public static IList<ElementId> CollectAllOwnedViewElements(Document doc, ElementId viewId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (viewId == null || viewId == ElementId.InvalidElementId)
                return new List<ElementId>();

            return new FilteredElementCollector(doc, viewId)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e != null)
                .Where(e => e.OwnerViewId == viewId)
                .Where(IsDeletableGeneratedViewElement)
                .Select(e => e.Id)
                .ToList();
        }

        public static ICollection<ElementId> CollectCopyableViewSpecificElementIds(Document doc, View sourceView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceView == null) throw new ArgumentNullException(nameof(sourceView));

            return new FilteredElementCollector(doc, sourceView.Id)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e != null)
                .Where(e => e.Id != sourceView.Id)
                .Where(e => e.ViewSpecific)
                .Where(e => e.OwnerViewId == sourceView.Id)
                .Where(IsCopyableViewSpecificElement)
                .Select(e => e.Id)
                .ToList();
        }

        public static IList<CurveElement> CollectVisibleNonViewSpecificCurveElements(Document doc, View sourceView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceView == null) throw new ArgumentNullException(nameof(sourceView));

            return new FilteredElementCollector(doc, sourceView.Id)
                .WhereElementIsNotElementType()
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(x => x != null)
                .Where(x => !x.ViewSpecific)
                .Where(IsSupportedCurveElement)
                .ToList();
        }

        private static bool IsDeletableGeneratedViewElement(Element e)
        {
            if (e == null) return false;
            if (e.Pinned) return true;

            // Keep broad. Drafting view is intended to be generated-only.
            return true;
        }
        public static IList<CurveLoop> GetAreaBoundaryLoops(Document doc, View sourceView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceView == null) throw new ArgumentNullException(nameof(sourceView));

            List<CurveLoop> loops = new List<CurveLoop>();

            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();

            List<Area> areas = new FilteredElementCollector(doc, sourceView.Id)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .Cast<Area>()
                .ToList();

            foreach (Area area in areas)
            {
                IList<IList<BoundarySegment>> boundaries = area.GetBoundarySegments(options);
                if (boundaries == null || boundaries.Count == 0)
                    continue;

                foreach (IList<BoundarySegment> segmentList in boundaries)
                {
                    if (segmentList == null || segmentList.Count == 0)
                        continue;

                    CurveLoop loop = new CurveLoop();
                    bool hasAny = false;

                    foreach (BoundarySegment seg in segmentList)
                    {
                        if (seg == null)
                            continue;

                        Curve c = seg.GetCurve();
                        if (c == null || !c.IsBound)
                            continue;

                        loop.Append(c);
                        hasAny = true;
                    }

                    if (hasAny)
                    {
                        loops.Add(loop);
                    }
                }
            }

            return loops;
        }
        private static bool IsCopyableViewSpecificElement(Element e)
        {
            if (e == null) return false;

            if (e is DetailCurve) return true;
            if (e is FilledRegion) return true;
            if (e is TextNote) return true;
            if (e is IndependentTag) return true;
            if (e is FamilyInstance) return true; // detail items, symbols, annotations
            if (e is Dimension) return true;
            if (e is DetailArc) return true;
            if (e is DetailLine) return true;
            if (e is DetailEllipse) return true;
            if (e is DetailNurbSpline) return true;

            // Safe fallback for other owned annotation/detail elements
            Category cat = e.Category;
            if (cat != null && cat.CategoryType == CategoryType.Annotation)
                return true;

            return false;
        }

        private static bool IsSupportedCurveElement(CurveElement curveElement)
        {
            if (curveElement == null) return false;

            Curve curve = curveElement.GeometryCurve;
            if (curve == null) return false;

            return curve is Line
                || curve is Arc
                || curve is Ellipse
                || curve is NurbSpline
                || curve is HermiteSpline;
        }
    }
}