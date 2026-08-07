using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Overhead
{
    /// <summary>
    /// Computes the actual oriented footprint of an element in the XY plane, rather than
    /// its axis aligned bounding box. Used by ProxyManager to build overhead proxy
    /// rectangles that follow an element's true rotation, instead of the always axis
    /// aligned box produced by Element.get_BoundingBox(null), which for a rotated element
    /// is always larger than the element itself in both X and Y, and visually reads as an
    /// oversized, floor like shape rather than a thin strip following the element's edges.
    ///
    /// Deliberately general across categories (walls, framing, casework, furniture, MEP,
    /// generic models, etc), since this walks the element's actual solid geometry rather
    /// than assuming a wall specific Location/Width shortcut. This is meaningfully more
    /// expensive per element than reading a cached bounding box, since it pulls real solid
    /// geometry, tessellates edges, and runs a convex hull plus rotating calipers pass on
    /// every call. This runs once per element per DMU sync, not per frame, so the cost is
    /// bounded by edit frequency, but if this method ever shows up as a hot path in a large
    /// project with frequent batch edits, the first optimization to consider is caching the
    /// computed rectangle per element per document modified counter, rather than
    /// recomputing on every SyncProxiesForElementInView call across every floor plan.
    /// </summary>
    internal static class OverheadFootprintGeometry
    {
        private readonly struct Pt
        {
            public readonly double X;
            public readonly double Y;
            public Pt(double x, double y) { X = x; Y = y; }
        }

        /// <summary>
        /// Attempts to compute the minimum area oriented bounding rectangle of the
        /// element's true footprint in the XY plane, returning its four corners at the
        /// given Z. Returns false if the element's geometry could not be resolved to at
        /// least 3 distinct footprint points, in which case the caller should fall back to
        /// an axis aligned bounding box rectangle.
        /// </summary>
        public static bool TryComputeOrientedFootprintRectangle(
            Element e, double z, out XYZ p1, out XYZ p2, out XYZ p3, out XYZ p4)
        {
            p1 = p2 = p3 = p4 = XYZ.Zero;

            var points = CollectFootprintPointsXY(e);
            if (points.Count < 3) return false;

            var hull = ConvexHull(points);
            if (hull.Count < 3) return false;

            if (!MinimumAreaRectangle(hull, out var c1, out var c2, out var c3, out var c4))
                return false;

            p1 = new XYZ(c1.X, c1.Y, z);
            p2 = new XYZ(c2.X, c2.Y, z);
            p3 = new XYZ(c3.X, c3.Y, z);
            p4 = new XYZ(c4.X, c4.Y, z);
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        // Geometry extraction
        // ═════════════════════════════════════════════════════════════════════════════════

        private static List<Pt> CollectFootprintPointsXY(Element e)
        {
            var points = new List<Pt>();

            Options opts;
            try
            {
                opts = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Fine
                };
            }
            catch
            {
                return points;
            }

            GeometryElement geomElem;
            try
            {
                geomElem = e.get_Geometry(opts);
            }
            catch
            {
                return points;
            }

            if (geomElem == null) return points;

            var solids = new List<Solid>();
            CollectSolids(geomElem, solids);

            foreach (var solid in solids)
            {
                if (solid == null) continue;

                EdgeArray edges;
                try { edges = solid.Edges; }
                catch { continue; }

                if (edges == null) continue;

                foreach (Edge edge in edges)
                {
                    IList<XYZ> tess;
                    try { tess = edge.Tessellate(); }
                    catch { continue; }

                    if (tess == null) continue;

                    foreach (var pt in tess)
                        points.Add(new Pt(pt.X, pt.Y));
                }
            }

            return points;
        }

        private static void CollectSolids(GeometryElement geomElem, List<Solid> solids)
        {
            if (geomElem == null) return;

            foreach (GeometryObject obj in geomElem)
            {
                switch (obj)
                {
                    case Solid solid:
                        // Zero volume solids show up in some family geometry as
                        // degenerate artifacts (construction planes, void remnants).
                        // Excluded so they cannot distort the footprint.
                        if (solid.Faces.Size > 0 && solid.Volume > 1e-9)
                            solids.Add(solid);
                        break;

                    case GeometryInstance gi:
                        // Family instance geometry is only available in world coordinates
                        // via GetInstanceGeometry(), the raw child geometry on the symbol
                        // is in the symbol's own local coordinate system and would produce
                        // a footprint in the wrong place and orientation if used directly.
                        GeometryElement instGeom;
                        try { instGeom = gi.GetInstanceGeometry(); }
                        catch { instGeom = null; }
                        CollectSolids(instGeom, solids);
                        break;

                    case GeometryElement nested:
                        CollectSolids(nested, solids);
                        break;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════════
        // 2D convex hull, Andrew's monotone chain
        // ═════════════════════════════════════════════════════════════════════════════════

        private static List<Pt> ConvexHull(List<Pt> points)
        {
            var pts = points
                .GroupBy(p => (p.X, p.Y))
                .Select(g => g.First())
                .OrderBy(p => p.X)
                .ThenBy(p => p.Y)
                .ToList();

            if (pts.Count < 3) return pts;

            var lower = new List<Pt>();
            foreach (var p in pts)
            {
                while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }

            var upper = new List<Pt>();
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                var p = pts[i];
                while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static double Cross(Pt o, Pt a, Pt b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        // ═════════════════════════════════════════════════════════════════════════════════
        // Minimum area oriented bounding rectangle, rotating calipers over the hull
        // ═════════════════════════════════════════════════════════════════════════════════

        private static bool MinimumAreaRectangle(
            List<Pt> hull, out Pt c1, out Pt c2, out Pt c3, out Pt c4)
        {
            c1 = c2 = c3 = c4 = default;

            int n = hull.Count;
            if (n < 3) return false;

            double minArea = double.MaxValue;
            Pt best1 = default, best2 = default, best3 = default, best4 = default;
            bool found = false;

            for (int i = 0; i < n; i++)
            {
                var edgeStart = hull[i];
                var edgeEnd = hull[(i + 1) % n];

                double dx = edgeEnd.X - edgeStart.X;
                double dy = edgeEnd.Y - edgeStart.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;

                double ux = dx / len;
                double uy = dy / len;
                // Perpendicular direction.
                double vx = -uy;
                double vy = ux;

                double minU = double.MaxValue, maxU = double.MinValue;
                double minV = double.MaxValue, maxV = double.MinValue;

                foreach (var p in hull)
                {
                    double rx = p.X - edgeStart.X;
                    double ry = p.Y - edgeStart.Y;

                    double pu = rx * ux + ry * uy;
                    double pv = rx * vx + ry * vy;

                    if (pu < minU) minU = pu;
                    if (pu > maxU) maxU = pu;
                    if (pv < minV) minV = pv;
                    if (pv > maxV) maxV = pv;
                }

                double width = maxU - minU;
                double height = maxV - minV;
                double area = width * height;

                if (area < minArea)
                {
                    minArea = area;
                    found = true;

                    Pt Corner(double u, double v) => new(
                        edgeStart.X + ux * u + vx * v,
                        edgeStart.Y + uy * u + vy * v);

                    best1 = Corner(minU, minV);
                    best2 = Corner(maxU, minV);
                    best3 = Corner(maxU, maxV);
                    best4 = Corner(minU, maxV);
                }
            }

            if (!found) return false;

            c1 = best1; c2 = best2; c3 = best3; c4 = best4;
            return true;
        }
    }
}