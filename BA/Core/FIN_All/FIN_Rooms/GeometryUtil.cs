using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.Rooms
{
    public static class GeometryUtil
    {
        private const double MinVolume = 1e-6;

        public static Solid TryGetRoomSolid(SpatialElementGeometryCalculator calc, Autodesk.Revit.DB.Architecture.Room room)
        {
            try
            {
                var res = calc.CalculateSpatialElementGeometry(room);
                return res?.GetGeometry();
            }
            catch
            {
                return null!;
            }
        }

        public static Solid TryGetElementSolid(Element e)
        {
            if (e == null) return null!;

            try
            {
                var opt = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = true,
                    DetailLevel = ViewDetailLevel.Fine
                };

                var geo = e.get_Geometry(opt);
                if (geo == null) return null!;

                var solids = new List<Solid>();
                CollectSolids(geo, Transform.Identity, solids);

                // Merge solids into one (union) for stable intersection results.
                Solid merged = null!;
                foreach (var s in solids)
                {
                    if (s == null || s.Volume < MinVolume) continue;

                    if (merged == null) merged = s;
                    else
                    {
                        try
                        {
                            merged = BooleanOperationsUtils.ExecuteBooleanOperation(merged, s, BooleanOperationsType.Union);
                        }
                        catch
                        {
                            // union can fail on bad geometry, keep what we have
                        }
                    }
                }

                return merged;
            }
            catch
            {
                return null!;
            }
        }

        private static void CollectSolids(GeometryElement geo, Transform current, List<Solid> solids)
        {
            foreach (var obj in geo)
            {
                if (obj is Solid s)
                {
                    if (s.Volume > MinVolume)
                    {
                        var ts = SolidUtils.CreateTransformed(s, current);
                        solids.Add(ts);
                    }
                }
                else if (obj is GeometryInstance inst)
                {
                    var t = current.Multiply(inst.Transform);
                    var instGeo = inst.GetInstanceGeometry();
                    if (instGeo != null)
                        CollectSolids(instGeo, t, solids);
                }
            }
        }

        public static bool BbOverlaps(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null) return false;

            var amin = a.Min; var amax = a.Max;
            var bmin = b.Min; var bmax = b.Max;

            bool x = amin.X <= bmax.X && amax.X >= bmin.X;
            bool y = amin.Y <= bmax.Y && amax.Y >= bmin.Y;
            bool z = amin.Z <= bmax.Z && amax.Z >= bmin.Z;

            return x && y && z;
        }

        public static double IntersectionVolume(Solid a, Solid b)
        {
            if (a == null || b == null) return 0;

            try
            {
                var inter = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, BooleanOperationsType.Intersect);
                if (inter == null) return 0;
                return inter.Volume;
            }
            catch
            {
                return 0;
            }
        }
    }
}