using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanFaceBuilder
    {
        public static List<List<XYZ>> BuildFaces(
            CurveLoop outerLoop,
            double[] xBreaks,
            double[] yBreaks)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));
            if (xBreaks == null) throw new ArgumentNullException(nameof(xBreaks));
            if (yBreaks == null) throw new ArgumentNullException(nameof(yBreaks));

            return BuildFacesInternal(
                outerLoop,
                KeyplanGridBuilder.BuildGridLines(outerLoop, xBreaks, yBreaks));
        }

        public static List<List<XYZ>> BuildFaces(
            CurveLoop outerLoop,
            IReadOnlyCollection<KeyplanSplitLineItem> verticalSplits,
            IReadOnlyCollection<KeyplanSplitLineItem> horizontalSplits)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));

            return BuildFacesInternal(
                outerLoop,
                KeyplanGridBuilder.BuildGridLines(outerLoop, verticalSplits, horizontalSplits));
        }

        private static List<List<XYZ>> BuildFacesInternal(
            CurveLoop outerLoop,
            IList<(XYZ A, XYZ B)> gridLines)
        {
            List<XYZ> outline = KeyplanPolygonUtils.CurveLoopToPolyline(outerLoop);
            outline = KeyplanPolygonUtils.CleanPolygonStrict(outline);

            if (outline == null || outline.Count < 3)
                return new List<List<XYZ>>();

            List<Segment2D> rawSegments = new List<Segment2D>();

            for (int i = 0; i < outline.Count; i++)
            {
                XYZ a = KeyplanPolygonUtils.FlattenPoint(outline[i]);
                XYZ b = KeyplanPolygonUtils.FlattenPoint(outline[(i + 1) % outline.Count]);

                if (a.DistanceTo(b) > KeyplanGeometryTolerance.FaceSplitPoint)
                    rawSegments.Add(new Segment2D(a, b));
            }

            foreach ((XYZ A, XYZ B) line in gridLines ?? Enumerable.Empty<(XYZ A, XYZ B)>())
            {
                foreach ((XYZ A, XYZ B) seg in KeyplanPolygonUtils.ClipLineByPolygon(outline, line.A, line.B))
                {
                    XYZ a = KeyplanPolygonUtils.FlattenPoint(seg.A);
                    XYZ b = KeyplanPolygonUtils.FlattenPoint(seg.B);

                    if (a != null && b != null && a.DistanceTo(b) > KeyplanGeometryTolerance.FaceSplitPoint)
                        rawSegments.Add(new Segment2D(a, b));
                }
            }

            List<Segment2D> splitSegments = SplitSegmentsAtIntersections(rawSegments);
            List<List<XYZ>> loops = PolygonizeSegments(splitSegments);

            List<List<XYZ>> faces = new List<List<XYZ>>();

            foreach (List<XYZ> loop in loops)
            {
                if (loop == null || loop.Count < 3)
                    continue;

                List<XYZ> cleaned = KeyplanPolygonUtils.CleanPolygonStrict(loop);
                if (cleaned == null || cleaned.Count < 3)
                    continue;

                double signedArea = KeyplanPolygonUtils.ComputeSignedArea2D(cleaned);
                double area = Math.Abs(signedArea);

                BoundingBoxUV bb = KeyplanPolygonUtils.GetBoundingBox2D(outerLoop);
                double modelSize = Math.Min(bb.Max.U - bb.Min.U, bb.Max.V - bb.Min.V);
                double minAllowedArea = modelSize * modelSize * 1e-5;

                if (area < minAllowedArea)
                    continue;

                XYZ centroid = ComputeCentroidApprox(cleaned);
                if (!KeyplanPolygonUtils.IsPointInsideOrOnPolygon(outline, centroid))
                    continue;

                string reason;
                if (!KeyplanPolygonUtils.IsValidFilledRegionPolygon(cleaned, out reason))
                    continue;

                if (signedArea < 0.0)
                    continue; // Outer face of the planar arrangement (opposite winding) — discard.

                string sig = KeyplanGeometryKeyService.MakePolygonKey(cleaned);
                bool exists = faces.Any(x => KeyplanGeometryKeyService.MakePolygonKey(x) == sig);
                if (!exists)
                    faces.Add(cleaned);
            }

            return faces
                .OrderBy(KeyplanGeometryKeyService.MakePolygonKey, StringComparer.Ordinal)
                .ToList();
        }

        private static List<Segment2D> SplitSegmentsAtIntersections(IList<Segment2D> segments)
        {
            List<Segment2D> result = new List<Segment2D>();
            if (segments == null || segments.Count == 0)
                return result;

            for (int i = 0; i < segments.Count; i++)
            {
                Segment2D s = segments[i];
                List<XYZ> splitPts = new List<XYZ> { s.A, s.B };

                for (int j = 0; j < segments.Count; j++)
                {
                    if (i == j)
                        continue;

                    Segment2D o = segments[j];
                    CollectIntersectionPoints(s, o, splitPts);
                }

                List<XYZ> ordered = OrderPointsAlongSegment(s.A, s.B, splitPts);

                for (int k = 0; k < ordered.Count - 1; k++)
                {
                    XYZ a = ordered[k];
                    XYZ b = ordered[k + 1];

                    if (a.DistanceTo(b) > KeyplanGeometryTolerance.FaceSplitPoint)
                        result.Add(new Segment2D(a, b));
                }
            }

            return RemoveDuplicateSegments(result);
        }

        private static void CollectIntersectionPoints(Segment2D s, Segment2D o, List<XYZ> output)
        {
            if (s == null || o == null || output == null)
                return;

            if (TryIntersectSegments(s.A, s.B, o.A, o.B, out XYZ ip))
                AddUniquePoint(output, ip);

            if (AreCollinearAndOverlapping(s.A, s.B, o.A, o.B))
            {
                if (PointOnSegment(o.A, s.A, s.B)) AddUniquePoint(output, o.A);
                if (PointOnSegment(o.B, s.A, s.B)) AddUniquePoint(output, o.B);
                if (PointOnSegment(s.A, o.A, o.B)) AddUniquePoint(output, s.A);
                if (PointOnSegment(s.B, o.A, o.B)) AddUniquePoint(output, s.B);
            }
        }

        private static List<XYZ> OrderPointsAlongSegment(XYZ a, XYZ b, IEnumerable<XYZ> pts)
        {
            XYZ dir = b - a;
            double lenSq = dir.DotProduct(dir);

            return pts
                .Where(p => p != null)
                .Select(KeyplanPolygonUtils.FlattenPoint)
                .Distinct(new XyzEqualityComparer(KeyplanGeometryTolerance.FaceSplitPoint))
                .OrderBy(p => lenSq < KeyplanGeometryTolerance.Epsilon ? 0.0 : ((p - a).DotProduct(dir) / lenSq))
                .ToList();
        }

        private static List<Segment2D> RemoveDuplicateSegments(IEnumerable<Segment2D> segments)
        {
            Dictionary<string, Segment2D> unique = new Dictionary<string, Segment2D>(StringComparer.Ordinal);

            foreach (Segment2D seg in segments ?? Enumerable.Empty<Segment2D>())
            {
                if (seg == null || seg.Length < KeyplanGeometryTolerance.FaceSplitPoint)
                    continue;

                string key = KeyplanGeometryKeyService.MakeUndirectedLineKey(seg.A, seg.B);
                if (!unique.ContainsKey(key))
                    unique[key] = seg;
            }

            return unique.Values
                .OrderBy(x => KeyplanGeometryKeyService.MakeUndirectedLineKey(x.A, x.B), StringComparer.Ordinal)
                .ToList();
        }

        private static List<List<XYZ>> PolygonizeSegments(IList<Segment2D> segments)
        {
            List<List<XYZ>> result = new List<List<XYZ>>();
            if (segments == null || segments.Count == 0)
                return result;

            Dictionary<string, XYZ> vertices = new Dictionary<string, XYZ>(StringComparer.Ordinal);
            Dictionary<string, List<HalfEdge>> outgoing = new Dictionary<string, List<HalfEdge>>(StringComparer.Ordinal);

            foreach (Segment2D seg in segments)
            {
                string aKey = KeyplanGeometryKeyService.MakePointKey(seg.A);
                string bKey = KeyplanGeometryKeyService.MakePointKey(seg.B);

                if (aKey == bKey)
                    continue;

                if (!vertices.ContainsKey(aKey)) vertices[aKey] = seg.A;
                if (!vertices.ContainsKey(bKey)) vertices[bKey] = seg.B;

                AddHalfEdge(outgoing, aKey, bKey, seg.A, seg.B);
                AddHalfEdge(outgoing, bKey, aKey, seg.B, seg.A);
            }

            foreach (List<HalfEdge> list in outgoing.Values)
                list.Sort((x, y) => x.Angle.CompareTo(y.Angle));

            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, List<HalfEdge>> kvp in outgoing.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                foreach (HalfEdge start in kvp.Value)
                {
                    string startKey = MakeDirectedSegmentKey(start.FromKey, start.ToKey);
                    if (used.Contains(startKey))
                        continue;

                    List<XYZ> loop = TraceFace(start, outgoing, vertices, used);
                    if (loop == null || loop.Count < 3)
                        continue;

                    if (loop.Count > 1 && loop[0].DistanceTo(loop[loop.Count - 1]) < KeyplanGeometryTolerance.Point)
                        loop.RemoveAt(loop.Count - 1);

                    result.Add(loop);
                }
            }

            return result;
        }

        private static List<XYZ> TraceFace(
            HalfEdge startEdge,
            Dictionary<string, List<HalfEdge>> outgoing,
            Dictionary<string, XYZ> vertices,
            HashSet<string> usedDirected)
        {
            List<XYZ> loop = new List<XYZ>();
            List<string> addedThisTrace = new List<string>();
            HalfEdge current = startEdge;
            int guard = 0;

            while (guard++ < 10000)
            {
                string dirKey = MakeDirectedSegmentKey(current.FromKey, current.ToKey);
                if (usedDirected.Contains(dirKey))
                {
                    if (current.FromKey == startEdge.FromKey && current.ToKey == startEdge.ToKey)
                        break;

                    RollBack(usedDirected, addedThisTrace);
                    return null;
                }

                usedDirected.Add(dirKey);
                addedThisTrace.Add(dirKey);

                if (!vertices.TryGetValue(current.FromKey, out XYZ fromPt))
                {
                    RollBack(usedDirected, addedThisTrace);
                    return null;
                }

                loop.Add(fromPt);

                if (!outgoing.TryGetValue(current.ToKey, out List<HalfEdge> nextList) || nextList.Count == 0)
                {
                    RollBack(usedDirected, addedThisTrace);
                    return null;
                }

                int twinIndex = nextList.FindIndex(x => x.ToKey == current.FromKey);
                if (twinIndex < 0)
                {
                    RollBack(usedDirected, addedThisTrace);
                    return null;
                }

                int nextIndex = twinIndex - 1;
                if (nextIndex < 0)
                    nextIndex = nextList.Count - 1;

                current = nextList[nextIndex];

                if (current.FromKey == startEdge.FromKey && current.ToKey == startEdge.ToKey)
                    break;
            }

            return loop;
        }

        private static void RollBack(HashSet<string> usedDirected, List<string> addedThisTrace)
        {
            foreach (string key in addedThisTrace)
                usedDirected.Remove(key);
        }

        private static void AddHalfEdge(
            Dictionary<string, List<HalfEdge>> outgoing,
            string fromKey,
            string toKey,
            XYZ from,
            XYZ to)
        {
            if (!outgoing.TryGetValue(fromKey, out List<HalfEdge> list))
            {
                list = new List<HalfEdge>();
                outgoing[fromKey] = list;
            }

            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            list.Add(new HalfEdge(fromKey, toKey, angle));
        }

        private static XYZ ComputeCentroidApprox(IList<XYZ> pts)
        {
            double x = 0.0;
            double y = 0.0;

            foreach (XYZ p in pts)
            {
                x += p.X;
                y += p.Y;
            }

            return new XYZ(x / pts.Count, y / pts.Count, 0.0);
        }

        private static bool TryIntersectSegments(XYZ p1, XYZ p2, XYZ q1, XYZ q2, out XYZ intersection)
        {
            intersection = null;

            double dx1 = p2.X - p1.X;
            double dy1 = p2.Y - p1.Y;
            double dx2 = q2.X - q1.X;
            double dy2 = q2.Y - q1.Y;

            double denom = dx1 * dy2 - dy1 * dx2;
            if (Math.Abs(denom) < KeyplanGeometryTolerance.SegmentIntersection)
                return false;

            double t = ((q1.X - p1.X) * dy2 - (q1.Y - p1.Y) * dx2) / denom;
            double u = ((q1.X - p1.X) * dy1 - (q1.Y - p1.Y) * dx1) / denom;

            if (t < -KeyplanGeometryTolerance.Parameter || t > 1.0 + KeyplanGeometryTolerance.Parameter ||
                u < -KeyplanGeometryTolerance.Parameter || u > 1.0 + KeyplanGeometryTolerance.Parameter)
                return false;

            intersection = new XYZ(
                p1.X + t * dx1,
                p1.Y + t * dy1,
                0.0);

            return true;
        }

        private static bool AreCollinearAndOverlapping(XYZ a1, XYZ a2, XYZ b1, XYZ b2)
        {
            if (!KeyplanPolygonUtils.AreCollinear2D(a1, a2, b1)) return false;
            if (!KeyplanPolygonUtils.AreCollinear2D(a1, a2, b2)) return false;

            bool aVertical = Math.Abs(a1.X - a2.X) < KeyplanGeometryTolerance.FaceSplitPoint;
            if (aVertical)
            {
                double aMin = Math.Min(a1.Y, a2.Y);
                double aMax = Math.Max(a1.Y, a2.Y);
                double bMin = Math.Min(b1.Y, b2.Y);
                double bMax = Math.Max(b1.Y, b2.Y);
                return Math.Max(aMin, bMin) <= Math.Min(aMax, bMax) + KeyplanGeometryTolerance.FaceSplitPoint;
            }

            double aMinX = Math.Min(a1.X, a2.X);
            double aMaxX = Math.Max(a1.X, a2.X);
            double bMinX = Math.Min(b1.X, b2.X);
            double bMaxX = Math.Max(b1.X, b2.X);
            return Math.Max(aMinX, bMinX) <= Math.Min(aMaxX, bMaxX) + KeyplanGeometryTolerance.FaceSplitPoint;
        }

        private static bool PointOnSegment(XYZ p, XYZ a, XYZ b)
        {
            if (p == null || a == null || b == null)
                return false;

            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (Math.Abs(cross) > KeyplanGeometryTolerance.FaceSnap)
                return false;

            double dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
            if (dot < -KeyplanGeometryTolerance.FaceSnap)
                return false;

            double lenSq = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
            if (dot > lenSq + KeyplanGeometryTolerance.FaceSnap)
                return false;

            return true;
        }

        private static void AddUniquePoint(List<XYZ> pts, XYZ p)
        {
            if (p == null)
                return;

            foreach (XYZ q in pts)
            {
                if (q.DistanceTo(p) <= KeyplanGeometryTolerance.FaceSplitPoint)
                    return;
            }

            pts.Add(p);
        }

        private static string MakeDirectedSegmentKey(string from, string to)
        {
            return from + "->" + to;
        }

        private sealed class Segment2D
        {
            public XYZ A { get; }
            public XYZ B { get; }
            public double Length => A.DistanceTo(B);

            public Segment2D(XYZ a, XYZ b)
            {
                A = KeyplanPolygonUtils.FlattenPoint(a);
                B = KeyplanPolygonUtils.FlattenPoint(b);
            }
        }

        private sealed class HalfEdge
        {
            public string FromKey { get; }
            public string ToKey { get; }
            public double Angle { get; }

            public HalfEdge(string fromKey, string toKey, double angle)
            {
                FromKey = fromKey;
                ToKey = toKey;
                Angle = angle;
            }
        }

        private sealed class XyzEqualityComparer : IEqualityComparer<XYZ>
        {
            private readonly double _tol;

            public XyzEqualityComparer(double tol)
            {
                _tol = Math.Abs(tol);
            }

            public bool Equals(XYZ x, XYZ y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                return x.DistanceTo(y) <= _tol;
            }

            public int GetHashCode(XYZ obj)
            {
                if (obj == null) return 0;
                long x = (long)Math.Round(obj.X / _tol);
                long y = (long)Math.Round(obj.Y / _tol);
                return HashCode.Combine(x, y);
            }
        }
    }
}