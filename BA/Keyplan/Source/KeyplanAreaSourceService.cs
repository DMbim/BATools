using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanAreaSourceService
    {
        private const double Tol = 1e-6;

        public static CurveLoop GetLargestOuterLoopFromView(Document doc, View sourceView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceView == null) throw new ArgumentNullException(nameof(sourceView));

            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();

            List<Area> areas = new FilteredElementCollector(doc, sourceView.Id)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .Cast<Area>()
                .ToList();

            List<List<XYZ>> candidatePolygons = new List<List<XYZ>>();

            foreach (Area area in areas)
            {
                IList<IList<BoundarySegment>> boundaries = area.GetBoundarySegments(options);
                if (boundaries == null || boundaries.Count == 0)
                    continue;

                foreach (IList<BoundarySegment> segList in boundaries)
                {
                    List<XYZ> polygon = BuildClosedPolygonFromBoundarySegments(segList);
                    if (polygon == null || polygon.Count < 3)
                        continue;

                    polygon = KeyplanPolygonUtils.CleanPolygon(polygon);
                    if (polygon.Count < 3)
                        continue;

                    candidatePolygons.Add(polygon);
                }
            }

            if (candidatePolygons.Count == 0)
                return null;

            List<XYZ> largestPolygon = null;
            double maxArea = double.MinValue;

            foreach (List<XYZ> polygon in candidatePolygons)
            {
                double area = Math.Abs(KeyplanPolygonUtils.ComputeSignedArea2D(polygon));
                if (area > maxArea)
                {
                    maxArea = area;
                    largestPolygon = polygon;
                }
            }

            if (largestPolygon == null || largestPolygon.Count < 3)
                return null;

            largestPolygon = KeyplanPolygonUtils.CleanPolygon(largestPolygon);
            if (largestPolygon.Count < 3)
                return null;

            if (!KeyplanPolygonUtils.TryCreateCurveLoopFromPolygon(largestPolygon, out CurveLoop outerLoop))
                return null;

            return outerLoop;
        }

        private static List<XYZ> BuildClosedPolygonFromBoundarySegments(IList<BoundarySegment> segList)
        {
            if (segList == null || segList.Count == 0)
                return null;

            List<XYZ> pts = new List<XYZ>();
            XYZ currentEnd = null;

            foreach (BoundarySegment seg in segList)
            {
                Curve c = seg?.GetCurve();
                if (c == null)
                    continue;

                XYZ a = KeyplanPolygonUtils.FlattenPoint(c.GetEndPoint(0));
                XYZ b = KeyplanPolygonUtils.FlattenPoint(c.GetEndPoint(1));

                if (a.DistanceTo(b) < Tol)
                    continue;

                if (pts.Count == 0)
                {
                    pts.Add(a);
                    pts.Add(b);
                    currentEnd = b;
                    continue;
                }

                if (currentEnd.DistanceTo(a) < Tol)
                {
                    pts.Add(b);
                    currentEnd = b;
                }
                else if (currentEnd.DistanceTo(b) < Tol)
                {
                    pts.Add(a);
                    currentEnd = a;
                }
                else
                {
                    // Fallback: continue in the closer direction
                    if (currentEnd.DistanceTo(a) <= currentEnd.DistanceTo(b))
                    {
                        if (currentEnd.DistanceTo(a) < 1e-3)
                        {
                            pts.Add(b);
                            currentEnd = b;
                        }
                        else
                        {
                            pts.Add(a);
                            pts.Add(b);
                            currentEnd = b;
                        }
                    }
                    else
                    {
                        if (currentEnd.DistanceTo(b) < 1e-3)
                        {
                            pts.Add(a);
                            currentEnd = a;
                        }
                        else
                        {
                            pts.Add(b);
                            pts.Add(a);
                            currentEnd = a;
                        }
                    }
                }
            }

            pts = KeyplanPolygonUtils.CleanPolygon(pts);

            if (pts.Count < 3)
                return null;

            return pts;
        }
    }
}