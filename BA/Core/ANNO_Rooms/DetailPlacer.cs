using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Rooms
{
    public static class DetailPlacer
    {
        /// <summary>
        /// Places a detail component in the XY center of a LOCAL room, then sets "x" and "y" parameters
        /// to the room bounding box width/depth (in internal units).
        /// </summary>
        public static FamilyInstance? PlaceInLocalRoomCenterSized(
            Document doc,
            View view,
            FamilySymbol symbol,
            Room room,
            string xParamName = "x",
            string yParamName = "y",
            double paddingInternal = 0.0)
        {
            if (!TryGetRoomBoxInHostXY(room, link: null, view, out var min, out var max, out var center))
                return null;

            var inst = doc.Create.NewFamilyInstance(center, symbol, view);
            if (inst == null) return null;

            ApplyXYSize(inst, min, max, xParamName, yParamName, paddingInternal);
            return inst;
        }

        /// <summary>
        /// Places a detail component in the XY center of a LINKED room (transformed into host),
        /// then sets "x" and "y" parameters to the room bounding box width/depth (in host XY).
        /// </summary>
        public static FamilyInstance? PlaceInLinkedRoomCenterSized(
            Document hostDoc,
            View view,
            FamilySymbol symbol,
            Room linkedRoom,
            RevitLinkInstance linkInstance,
            string xParamName = "x",
            string yParamName = "y",
            double paddingInternal = 0.0)
        {
            if (linkInstance == null) return null;

            if (!TryGetRoomBoxInHostXY(linkedRoom, linkInstance, view, out var min, out var max, out var center))
                return null;

            var inst = hostDoc.Create.NewFamilyInstance(center, symbol, view);
            if (inst == null) return null;

            ApplyXYSize(inst, min, max, xParamName, yParamName, paddingInternal);
            return inst;
        }

        private static void ApplyXYSize(
            FamilyInstance inst,
            XYZ min,
            XYZ max,
            string xParamName,
            string yParamName,
            double paddingInternal)
        {
            // Width/Depth in host XY (internal feet)
            var w = Math.Abs(max.X - min.X) - 2.0 * paddingInternal;
            var d = Math.Abs(max.Y - min.Y) - 2.0 * paddingInternal;

            if (w < 0) w = 0;
            if (d < 0) d = 0;

            // Prefer INSTANCE parameters; if your family uses TYPE params, you should convert them to instance
            TrySetInstanceLength(inst, xParamName, w);
            TrySetInstanceLength(inst, yParamName, d);
        }

        private static bool TrySetInstanceLength(FamilyInstance inst, string paramName, double valueInternal)
        {
            var p = inst.LookupParameter(paramName);
            if (p == null) return false;
            if (p.IsReadOnly) return false;
            if (p.StorageType != StorageType.Double) return false;

            try
            {
                return p.Set(valueInternal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets room bounding box in HOST XY.
        /// For linked rooms, applies link transform.
        /// Z is taken from the host view level/plane so detail components land correctly.
        /// </summary>
        private static bool TryGetRoomBoxInHostXY(
            Room room,
            RevitLinkInstance? link,
            View hostView,
            out XYZ minHost,
            out XYZ maxHost,
            out XYZ centerHost)
        {
            minHost = XYZ.Zero;
            maxHost = XYZ.Zero;
            centerHost = XYZ.Zero;

            var bb = room.get_BoundingBox(null);
            if (bb == null) return false;

            var tr = link?.GetTransform() ?? Transform.Identity;

            var minT = tr.OfPoint(bb.Min);
            var maxT = tr.OfPoint(bb.Max);

            // Use host view Z so we're not using linked level elevations (and not using random bb.Z)
            var z = GetHostViewZ(hostView);

            minHost = new XYZ(minT.X, minT.Y, z);
            maxHost = new XYZ(maxT.X, maxT.Y, z);

            centerHost = new XYZ(
                (minHost.X + maxHost.X) * 0.5,
                (minHost.Y + maxHost.Y) * 0.5,
                z);

            return true;
        }

        private static double GetHostViewZ(View view)
        {
            // Plan views: safest is GenLevel elevation
            if (view is ViewPlan vp && vp.GenLevel != null)
                return vp.GenLevel.Elevation;

            // If view has a sketch plane, use it
            try
            {
                if (view.SketchPlane != null)
                    return view.SketchPlane.GetPlane().Origin.Z;
            }
            catch { /* ignore */ }

            // Fallback: view origin
            try
            {
                return view.Origin.Z;
            }
            catch
            {
                return 0.0;
            }
        }
    }
}
