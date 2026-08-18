using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BA.Zoom.Helpers
{
    /// <summary>
    /// Pure XY geometry operations for zoom rectangle computation.
    /// No document access, no settings, no UI — geometry only.
    /// </summary>
    internal static class ZoomGeometryHelper
    {
        /// <summary>
        /// Normalizes min/max corner order and expands the rectangle by bufferMm on all sides.
        /// Enforces a minimum extent of 200 mm on each axis to prevent degenerate zoom rectangles.
        /// Z is always zeroed — ZoomAndCenterRectangle requires flat 2D XY input.
        /// </summary>
        public static void NormalizeAndBufferRectangle(ref XYZ min, ref XYZ max, double bufferMm)
        {
            double minX = Math.Min(min.X, max.X);
            double minY = Math.Min(min.Y, max.Y);
            double maxX = Math.Max(min.X, max.X);
            double maxY = Math.Max(min.Y, max.Y);

            double buf = UnitUtils.ConvertToInternalUnits(bufferMm, UnitTypeId.Millimeters);
            double minSize = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters);

            if ((maxX - minX) < minSize) { double d = (minSize - (maxX - minX)) / 2.0; minX -= d; maxX += d; }
            if ((maxY - minY) < minSize) { double d = (minSize - (maxY - minY)) / 2.0; minY -= d; maxY += d; }

            min = new XYZ(minX - buf, minY - buf, 0);
            max = new XYZ(maxX + buf, maxY + buf, 0);
        }

        /// <summary>
        /// Computes XY bounds for a room in local document space.
        /// Attempt order: boundary segments -> bounding box -> location point fallback.
        /// Returns false only if all three strategies fail.
        /// </summary>
        public static bool TryGetRoomXYBounds_Local(Room room, out XYZ minXY, out XYZ maxXY)
        {
            if (TryGetXYFromBoundaries(room, Transform.Identity, out minXY, out maxXY))
                return true;

            var bb = room.get_BoundingBox(null);
            if (bb != null)
            {
                minXY = new XYZ(Math.Min(bb.Min.X, bb.Max.X), Math.Min(bb.Min.Y, bb.Max.Y), 0);
                maxXY = new XYZ(Math.Max(bb.Min.X, bb.Max.X), Math.Max(bb.Min.Y, bb.Max.Y), 0);
                return true;
            }

            if (room.Location is LocationPoint lp)
            {
                double pad = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);
                minXY = new XYZ(lp.Point.X - pad, lp.Point.Y - pad, 0);
                maxXY = new XYZ(lp.Point.X + pad, lp.Point.Y + pad, 0);
                return true;
            }

            minXY = maxXY = XYZ.Zero;
            return false;
        }

        /// <summary>
        /// Computes XY bounds for a room in a linked document, applying the link instance transform.
        /// Attempt order: boundary segments (transformed) -> bounding box (transformed) -> location point fallback.
        /// </summary>
        public static bool TryGetRoomXYBounds_Link(Room room, RevitLinkInstance linkInst, out XYZ minXY, out XYZ maxXY)
        {
            var T = linkInst.GetTransform();

            if (TryGetXYFromBoundaries(room, T, out minXY, out maxXY))
                return true;

            var bb = room.get_BoundingBox(null);
            if (bb != null)
            {
                XYZ minT = T.OfPoint(bb.Min);
                XYZ maxT = T.OfPoint(bb.Max);
                minXY = new XYZ(Math.Min(minT.X, maxT.X), Math.Min(minT.Y, maxT.Y), 0);
                maxXY = new XYZ(Math.Max(minT.X, maxT.X), Math.Max(minT.Y, maxT.Y), 0);
                return true;
            }

            if (room.Location is LocationPoint lp)
            {
                var p = T.OfPoint(lp.Point);
                double pad = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);
                minXY = new XYZ(p.X - pad, p.Y - pad, 0);
                maxXY = new XYZ(p.X + pad, p.Y + pad, 0);
                return true;
            }

            minXY = maxXY = XYZ.Zero;
            return false;
        }

        /// <summary>
        /// Iterates boundary segment curve endpoints applying xform to each.
        /// Uses only endpoints per segment — sufficient for rectilinear and moderately curved rooms.
        /// Arc rooms with large bulge may slightly underestimate bounds; the downstream buffer compensates.
        /// </summary>
        private static bool TryGetXYFromBoundaries(Room room, Transform xform, out XYZ minXY, out XYZ maxXY)
        {
            minXY = maxXY = XYZ.Zero;
            try
            {
                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                var loops = room.GetBoundarySegments(opts);
                if (loops == null || loops.Count == 0) return false;

                bool inited = false;
                double minX = 0, minY = 0, maxX = 0, maxY = 0;

                foreach (var loop in loops)
                {
                    if (loop == null) continue;
                    foreach (var seg in loop)
                    {
                        var c = seg?.GetCurve();
                        if (c == null) continue;

                        for (int i = 0; i < 2; i++)
                        {
                            XYZ p = xform.OfPoint(c.GetEndPoint(i));
                            if (!inited) { minX = maxX = p.X; minY = maxY = p.Y; inited = true; }
                            else
                            {
                                if (p.X < minX) minX = p.X;
                                if (p.Y < minY) minY = p.Y;
                                if (p.X > maxX) maxX = p.X;
                                if (p.Y > maxY) maxY = p.Y;
                            }
                        }
                    }
                }

                if (!inited) return false;
                minXY = new XYZ(minX, minY, 0);
                maxXY = new XYZ(maxX, maxY, 0);
                return true;
            }
            catch { return false; }
        }
    }
}