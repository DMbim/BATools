using Autodesk.Revit.DB;

namespace BA.Core.Rooms
{
    public static class ElementLocationUtils
    {
        public static XYZ? GetBestPoint(Element e, Autodesk.Revit.DB.View? viewForBBox = null)
        {
            if (e == null) return null;

            if (e.Location is LocationPoint lp && lp.Point != null)
                return lp.Point;

            if (e.Location is LocationCurve lc && lc.Curve != null)
                return lc.Curve.Evaluate(0.5, true);

            var bb = e.get_BoundingBox(viewForBBox);
            if (bb == null) bb = e.get_BoundingBox(null);
            if (bb == null) return null;

            return (bb.Min + bb.Max) * 0.5;
        }
    }
}
