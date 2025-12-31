using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.Core.Overhead
{
    public static class ProxyManager
    {
        public static void CreateOrUpdateRectangleProxy(Document doc, ViewPlan view, Element owner, BoundingBoxXYZ bb, GraphicsStyle gs)
        {
            if (doc == null || view == null || owner == null || bb == null)
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

            ProxyStateStore.RemoveProxies(view, owner.Id);

            double planeZ = view.SketchPlane.GetPlane().Origin.Z;
            XYZ p1 = new(bb.Min.X, bb.Min.Y, planeZ);
            XYZ p2 = new(bb.Max.X, bb.Min.Y, planeZ);
            XYZ p3 = new(bb.Max.X, bb.Max.Y, planeZ);
            XYZ p4 = new(bb.Min.X, bb.Max.Y, planeZ);

            var edges = new[]
            {
                (Start:p1, End:p2),
                (Start:p2, End:p3),
                (Start:p3, End:p4),
                (Start:p4, End:p1),
            };

            var created = new List<ElementId>();
            double minLen = UnitUtils.ConvertToInternalUnits(0.5, UnitTypeId.Millimeters);

            foreach (var e in edges)
            {
                if (e.Start.DistanceTo(e.End) < minLen) continue;

                var ln = Line.CreateBound(e.Start, e.End);
                var dc = doc.Create.NewDetailCurve(view, ln);

                dc.LineStyle = gs;
                created.Add(dc.Id);

                var cmt = dc.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (cmt != null && !cmt.IsReadOnly)
                    cmt.Set($"OAD_OVERHEAD_PROXY_{owner.Id.Value}");
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
