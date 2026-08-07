using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.Overhead
{
    /// <summary>
    /// A wall side face's footprint in the XY plane, represented as a 2D segment. See
    /// OverheadWallContactSuppressor remarks for how this is derived and its limitations.
    /// </summary>
    internal sealed class WallFaceSegment2D
    {
        public XYZ A = XYZ.Zero;
        public XYZ B = XYZ.Zero;
    }

    /// <summary>
    /// Suppresses overhead proxy edges (for Ceilings, Floors, Roofs) that run directly
    /// along the top of a supporting wall below, since the wall's own linework already
    /// marks that boundary in plan, an overhead dashed line drawn on top of it is
    /// redundant clutter. Edges that overhang past any wall (cantilevers, unsupported
    /// edges) are left untouched and still get the overhead line, since nothing else in
    /// the view represents them.
    ///
    /// Tests against each candidate wall's actual side face geometry, not its centerline,
    /// per project decision, so tapered, angled, or non uniform width walls are handled
    /// correctly rather than assuming a constant Wall.Width.
    ///
    /// Known limitation: wall face extraction identifies vertical edges of each side face
    /// and treats their midpoint XY as an endpoint of that face's plan footprint. This is
    /// correct for ordinary straight wall runs. A face split by an opening, a heavily
    /// curved wall, or an unusual profile edit may produce an imperfect 2D footprint for
    /// that face. The failure mode is conservative, it tends to under suppress (an edge
    /// that should be hidden stays visible) rather than over suppress (an edge that should
    /// stay visible gets incorrectly hidden), since a missing or malformed segment simply
    /// means fewer candidates to test against, not spurious ones. If suppression seems too
    /// weak on complex wall geometry, this extraction is the first place to look, not the
    /// tolerance value.
    ///
    /// Not cached. Candidate wall collection and face extraction runs fresh on every call
    /// to CollectCandidateWallSegments. Acceptable for incremental DMU driven syncs (a
    /// handful of modified elements per commit), but this is a real cost during
    /// OverheadGlobalService.SetEnabled's full model repopulation pass, which calls this
    /// once per Ceiling/Floor/Roof element across every floor plan, each call re-collects
    /// and re-extracts geometry for every wall in the document. If that pass becomes slow
    /// on a large project, the fix is to hoist wall face collection to run once per
    /// document per Enable call (grouped by owner bottom Z) and pass the result down,
    /// not to change anything inside this class.
    /// </summary>
    internal static class OverheadWallContactSuppressor
    {
        // Distance, in mm, within which an edge sample point counts as backed by a wall
        // face. Fixed in code per project decision, not exposed in OverheadSettings.
        private const double ContactToleranceMm = 15.0;

        // Vertical tolerance, in mm, for considering a wall a support candidate for a
        // given owner element. The wall's top must be within this margin of the owner's
        // bottom (bb.Min.Z) to be treated as potentially supporting it. Prevents an
        // unrelated wall on a different level from suppressing an edge purely because it
        // shares XY coordinates.
        private const double SupportVerticalToleranceMm = 300.0;

        // Number of sample points along each edge, including both endpoints. An edge is
        // only suppressed if every sample point is wall backed, a single unsupported
        // sample point (a jog, a partial overhang) keeps the whole edge visible.
        private const int SamplesPerEdge = 6;

        /// <summary>
        /// Collects the 2D (XY) footprint segments of every wall side face that could
        /// plausibly support an element whose bottom sits at ownerBottomZ. Call once per
        /// owner element, not once per edge, the result is reused across all four edges of
        /// that element's proxy rectangle.
        /// </summary>
        public static List<WallFaceSegment2D> CollectCandidateWallSegments(Document doc, double ownerBottomZ)
        {
            var result = new List<WallFaceSegment2D>();

            double vTolFt = UnitUtils.ConvertToInternalUnits(SupportVerticalToleranceMm, UnitTypeId.Millimeters);

            List<Wall> walls;
            try
            {
                walls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .ToList();
            }
            catch
            {
                return result;
            }

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
                return result;
            }

            foreach (var wall in walls)
            {
                BoundingBoxXYZ wbb;
                try { wbb = wall.get_BoundingBox(null); }
                catch { continue; }
                if (wbb == null) continue;

                // Candidate only if the wall's top is close to the owner's bottom.
                if (Math.Abs(wbb.Max.Z - ownerBottomZ) > vTolFt)
                    continue;

                GeometryElement geomElem;
                try { geomElem = wall.get_Geometry(opts); }
                catch { continue; }
                if (geomElem == null) continue;

                foreach (GeometryObject obj in geomElem)
                {
                    if (obj is not Solid solid || solid.Faces.Size == 0 || solid.Volume <= 1e-9)
                        continue;

                    foreach (Face face in solid.Faces)
                    {
                        if (!IsVerticalSideFace(face)) continue;

                        var xyPoints = ExtractVerticalEdgeXYPoints(face);
                        if (xyPoints.Count < 2) continue;

                        for (int i = 0; i < xyPoints.Count - 1; i++)
                        {
                            result.Add(new WallFaceSegment2D
                            {
                                A = xyPoints[i],
                                B = xyPoints[i + 1]
                            });
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// True if every sample point along the edge (start to end, inclusive) lies within
        /// ContactToleranceMm of at least one candidate wall face segment.
        /// </summary>
        public static bool IsEdgeWallBacked(XYZ start, XYZ end, List<WallFaceSegment2D> candidateSegments)
        {
            if (candidateSegments == null || candidateSegments.Count == 0) return false;

            double tolFt = UnitUtils.ConvertToInternalUnits(ContactToleranceMm, UnitTypeId.Millimeters);

            for (int i = 0; i < SamplesPerEdge; i++)
            {
                double t = i / (double)(SamplesPerEdge - 1);
                double sx = start.X + (end.X - start.X) * t;
                double sy = start.Y + (end.Y - start.Y) * t;

                double best = double.MaxValue;
                foreach (var seg in candidateSegments)
                {
                    double d = DistancePointToSegment2D(sx, sy, seg.A.X, seg.A.Y, seg.B.X, seg.B.Y);
                    if (d < best) best = d;
                }

                if (best > tolFt) return false;
            }

            return true;
        }

        private static bool IsVerticalSideFace(Face face)
        {
            try
            {
                var bbox = face.GetBoundingBox();
                var mid = new UV(
                    (bbox.Min.U + bbox.Max.U) / 2.0,
                    (bbox.Min.V + bbox.Max.V) / 2.0);
                var normal = face.ComputeNormal(mid);
                // Vertical side face, normal lies (mostly) in the XY plane.
                return Math.Abs(normal.Z) < 0.3;
            }
            catch
            {
                return false;
            }
        }

        private static List<XYZ> ExtractVerticalEdgeXYPoints(Face face)
        {
            var points = new List<XYZ>();

            EdgeArrayArray loops;
            try { loops = face.EdgeLoops; }
            catch { return points; }
            if (loops == null) return points;

            foreach (EdgeArray loop in loops)
            {
                foreach (Edge edge in loop)
                {
                    IList<XYZ> tess;
                    try { tess = edge.Tessellate(); }
                    catch { continue; }
                    if (tess == null || tess.Count < 2) continue;

                    var a = tess[0];
                    var b = tess[tess.Count - 1];

                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double dz = b.Z - a.Z;
                    double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (len < 1e-9) continue;

                    // Vertical edge, direction is (mostly) parallel to global Z. X,Y is
                    // effectively constant along it, its midpoint XY represents one end
                    // of this face's plan footprint.
                    if (Math.Abs(dz) / len > 0.9)
                        points.Add(new XYZ((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0));
                }
            }

            return points;
        }

        private static double DistancePointToSegment2D(
            double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double lenSq = dx * dx + dy * dy;

            if (lenSq < 1e-12)
            {
                double ddx = px - ax, ddy = py - ay;
                return Math.Sqrt(ddx * ddx + ddy * ddy);
            }

            double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
            t = Math.Max(0.0, Math.Min(1.0, t));

            double cx = ax + t * dx;
            double cy = ay + t * dy;

            double rx = px - cx, ry = py - cy;
            return Math.Sqrt(rx * rx + ry * ry);
        }
    }
}