using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Diagnostics;

namespace BA.Core.Overhead
{
    public static class ProxyManager
    {
        private const string ProxyCommentPrefix = "OAD_OVERHEAD_PROXY_";

        // Categories eligible for wall contact suppression. Deliberately excludes Walls
        // and StructuralColumns, wall on wall contact suppression was never the request,
        // this only applies to the horizontal elements sitting above the cut plane.
        private static bool IsWallContactEligibleCategory(Category cat)
        {
            if (cat == null) return false;
            return cat.Id == new ElementId(BuiltInCategory.OST_Ceilings)
                   || cat.Id == new ElementId(BuiltInCategory.OST_Floors)
                   || cat.Id == new ElementId(BuiltInCategory.OST_Roofs);
        }

        public static void CreateOrUpdateRectangleProxy(Document doc, ViewPlan view, Element owner, BoundingBoxXYZ bb, GraphicsStyle gs, OverheadSettings settings)
        {
            if (doc == null || view == null || owner == null || bb == null || settings == null)
                return;
            if (gs == null)
            {
                ProxyStateStore.RemoveProxies(view, owner.Id);
                return;
            }
            if (view.SketchPlane == null)
            {
                double z = view.GenLevel?.Elevation ?? 0.0;
                var plane = Plane.CreateByNormalAndOrigin(view.ViewDirection, new XYZ(0, 0, z));
                view.SketchPlane = SketchPlane.Create(doc, plane);
            }

            // Remove old proxies for this owner in this view
            ProxyStateStore.RemoveProxies(view, owner.Id);

            double planeZ = view.SketchPlane.GetPlane().Origin.Z;

            XYZ p1, p2, p3, p4;

            // Prefer the element's actual oriented footprint over its axis aligned bounding
            // box, since get_BoundingBox(null) is always world axis aligned regardless of
            // the element's own rotation. Falls back to the axis aligned bounding box if
            // geometry extraction fails.
            bool gotOriented = OverheadFootprintGeometry.TryComputeOrientedFootprintRectangle(
                owner, planeZ, out p1, out p2, out p3, out p4);

            if (!gotOriented)
            {
                Trace.WriteLine(
                    $"[ProxyManager] CreateOrUpdateRectangleProxy: oriented footprint " +
                    $"unavailable for element {owner.Id}, falling back to axis aligned " +
                    $"bounding box.");

                p1 = new XYZ(bb.Min.X, bb.Min.Y, planeZ);
                p2 = new XYZ(bb.Max.X, bb.Min.Y, planeZ);
                p3 = new XYZ(bb.Max.X, bb.Max.Y, planeZ);
                p4 = new XYZ(bb.Min.X, bb.Max.Y, planeZ);
            }

            var edges = new[]
            {
                (Start:p1, End:p2),
                (Start:p2, End:p3),
                (Start:p3, End:p4),
                (Start:p4, End:p1),
            };

            // Wall contact suppression: for Ceilings, Floors, and Roofs, drop any edge
            // that runs directly along the top of a supporting wall below, since the
            // wall's own linework already marks that boundary. Edges that overhang past
            // any wall are left untouched. Candidate wall segments are collected once per
            // owner (using its real bottom elevation, bb.Min.Z, not the proxy drawing
            // plane), then reused across all four edge tests.
            bool suppressWallBackedEdges = IsWallContactEligibleCategory(owner.Category);
            List<WallFaceSegment2D>? candidateWallSegments = null;

            if (suppressWallBackedEdges)
                candidateWallSegments = OverheadWallContactSuppressor.CollectCandidateWallSegments(doc, bb.Min.Z);

            var created = new List<ElementId>();
            double minLen = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);

            foreach (var e in edges)
            {
                if (e.Start.DistanceTo(e.End) < minLen) continue;

                if (suppressWallBackedEdges &&
                    OverheadWallContactSuppressor.IsEdgeWallBacked(e.Start, e.End, candidateWallSegments))
                {
                    continue;
                }

                var ln = Line.CreateBound(e.Start, e.End);
                var dc = doc.Create.NewDetailCurve(view, ln);
                dc.LineStyle = gs;

                var cmt = dc.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (cmt != null && !cmt.IsReadOnly)
                    cmt.Set($"{ProxyCommentPrefix}{owner.Id.Value}");

                created.Add(dc.Id);
            }

            if (created.Count > 0)
                ProxyStateStore.AddProxies(view, owner.Id, created);
        }

        public static void RemoveProxies(ViewPlan view, ElementId ownerId)
        {
            if (view == null) return;
            ProxyStateStore.RemoveProxies(view, ownerId);
        }
    }
}