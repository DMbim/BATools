using Autodesk.Revit.DB;
using System;

namespace BA.BIM.Core.Annotations
{
    public static class AnnoLocation
    {
        public static bool TryGetRepresentativePoint(View view, Element e, out XYZ p)
        {
            p = null;

            // Some annotations expose LocationPoint, some don't. BBox center is most universal.
            if (e.Location is LocationPoint lp && lp.Point != null)
            {
                p = lp.Point;
                return true;
            }

            if (e.Location is LocationCurve lc && lc.Curve != null)
            {
                p = lc.Curve.Evaluate(0.5, true);
                return true;
            }

            BoundingBoxXYZ bb = e.get_BoundingBox(view);
            if (bb != null)
            {
                p = (bb.Min + bb.Max) * 0.5;
                return true;
            }

            return false;
        }
    }

    public static class AnnoMove
    {
        public static bool TryMoveBy(Document doc, Element e, XYZ delta, out string reason)
        {
            reason = string.Empty;

            if (e == null) { reason = "null"; return false; }
            if (e.Pinned) { reason = "pinned"; return false; }
            if (e.GroupId != ElementId.InvalidElementId) { reason = "grouped"; return false; }

            if (delta.GetLength() < 1e-9) { reason = "delta~0"; return false; }

            try
            {
                ElementTransformUtils.MoveElement(doc, e.Id, delta);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}