using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanFaceBuilder
    {
        private const double Tol = 1e-5;
        private const double SnapTol = 5e-3;
        private const double AreaTol = 1e-5;

        public static List<List<XYZ>> BuildFaces(
            CurveLoop outerLoop,
            double[] xBreaks,
            double[] yBreaks)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));
            if (xBreaks == null) throw new ArgumentNullException(nameof(xBreaks));
            if (yBreaks == null) throw new ArgumentNullException(nameof(yBreaks));

            List<XYZ> outline = KeyplanPolygonUtils.CurveLoopToPolyline(outerLoop);
            outline = KeyplanPolygonUtils.CleanPolygonStrict(outline);

            if (outline == null || outline.Count < 3)
                return new List<List<XYZ>>();

            List<Segment2D> rawSegments = new List<Segment2D>();

            for (int i = 0; i < outline.Count; i++)
            {
                XYZ a = KeyplanPolygonUtils.FlattenPoint(outline[i]);
                XYZ b = KeyplanPolygonUtils.FlattenPoint(outline[(i + 1) % outline.Count]);

                if (a.DistanceTo(b) > Tol)
                    rawSegments.Add(new Segment2D(a, b));
            }

            foreach ((XYZ A, XYZ B) line in KeyplanGridBuilder.BuildGridLines(outerLoop, xBreaks, yBreaks))
            {
                foreach ((XYZ A, XYZ B) seg in KeyplanPolygonUtils.ClipLineByPolygon(outline, line.A, line.B))
                {
                    XYZ a = KeyplanPolygonUtils.FlattenPoint(seg.A);
                    XYZ b = KeyplanPolygonUtils.FlattenPoint(seg.B);

                    if (a != null && b != null && a.DistanceTo(b) > Tol)
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
                    cleaned.Reverse();

                string sig = MakePolygonSignature(cleaned);
                bool exists = faces.Any(x => MakePolygonSignature(x) == sig);
                if (!exists)
                    faces.Add(cleaned);
            }

            return faces;
        }
        private static XYZ SnapPointToPolygonBoundary(XYZ p, IList<XYZ> polygon, double snapTol)
        {
            if (p == null || polygon == null || polygon.Count < 2)
                return p;

            XYZ bestPoint = p;
            double bestDist = double.MaxValue;

            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ a = polygon[i];
                XYZ b = polygon[(i + 1) % polygon.Count];

                XYZ projected = ProjectPointToSegment2D(p, a, b);
                if (projected == null)
                    continue;

                double d = projected.DistanceTo(p);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestPoint = projected;
                }
            }

            if (bestDist <= snapTol)
                return new XYZ(bestPoint.X, bestPoint.Y, 0.0);

            return p;
        }

        private static XYZ ProjectPointToSegment2D(XYZ p, XYZ a, XYZ b)
        {
            if (p == null || a == null || b == null)
                return null;

            XYZ ab = b - a;
            double lenSq = ab.DotProduct(ab);
            if (lenSq < 1e-12)
                return a;

            double t = ((p - a).DotProduct(ab)) / lenSq;

            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;

            XYZ q = a + (ab * t);
            return new XYZ(q.X, q.Y, 0.0);
        }
        private static List<(XYZ A, XYZ B)> ClipLineByPolygonExact(IList<XYZ> polygon, XYZ lineStart, XYZ lineEnd)
        {
            return KeyplanPolygonUtils.ClipLineByPolygon(polygon, lineStart, lineEnd);
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

                    if (a.DistanceTo(b) > Tol)
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
            {
                AddUniquePoint(output, ip);
            }

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
                .Distinct(new XyzEqualityComparer(Tol))
                .OrderBy(p => lenSq < Tol ? 0.0 : ((p - a).DotProduct(dir) / lenSq))
                .ToList();
        }

        private static List<Segment2D> RemoveDuplicateSegments(IEnumerable<Segment2D> segments)
        {
            Dictionary<string, Segment2D> unique = new Dictionary<string, Segment2D>(StringComparer.Ordinal);

            foreach (Segment2D seg in segments ?? Enumerable.Empty<Segment2D>())
            {
                if (seg == null || seg.Length < Tol)
                    continue;

                string key = MakeUndirectedSegmentKey(seg.A, seg.B);
                if (!unique.ContainsKey(key))
                    unique[key] = seg;
            }

            return unique.Values.ToList();
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
                string aKey = MakePointKey(seg.A);
                string bKey = MakePointKey(seg.B);

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

            foreach (KeyValuePair<string, List<HalfEdge>> kvp in outgoing)
            {
                foreach (HalfEdge start in kvp.Value)
                {
                    string startKey = MakeDirectedSegmentKey(start.FromKey, start.ToKey);
                    if (used.Contains(startKey))
                        continue;

                    List<XYZ> loop = TraceFace(start, outgoing, vertices, used);
                    if (loop == null || loop.Count < 3)
                        continue;

                    if (loop.Count > 1 && loop[0].DistanceTo(loop[loop.Count - 1]) < Tol)
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
            HalfEdge current = startEdge;
            int guard = 0;

            while (guard++ < 10000)
            {
                string dirKey = MakeDirectedSegmentKey(current.FromKey, current.ToKey);
                if (usedDirected.Contains(dirKey))
                {
                    if (current.FromKey == startEdge.FromKey && current.ToKey == startEdge.ToKey)
                        break;

                    return null;
                }

                usedDirected.Add(dirKey);

                if (!vertices.TryGetValue(current.FromKey, out XYZ fromPt))
                    return null;

                loop.Add(fromPt);

                if (!outgoing.TryGetValue(current.ToKey, out List<HalfEdge> nextList) || nextList.Count == 0)
                    return null;

                int twinIndex = nextList.FindIndex(x => x.ToKey == current.FromKey);
                if (twinIndex < 0)
                    return null;

                int nextIndex = twinIndex - 1;
                if (nextIndex < 0)
                    nextIndex = nextList.Count - 1;

                current = nextList[nextIndex];

                if (current.FromKey == startEdge.FromKey && current.ToKey == startEdge.ToKey)
                    break;
            }

            return loop;
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

        private static XYZ Lerp(XYZ a, XYZ b, double t)
        {
            return new XYZ(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                0.0);
        }

        private static double RefineBoundary(
            XYZ a,
            XYZ b,
            IList<XYZ> poly,
            double t0,
            double t1,
            bool entering)
        {
            for (int i = 0; i < 10; i++)
            {
                double tm = 0.5 * (t0 + t1);
                XYZ p = Lerp(a, b, tm);

                bool inside = KeyplanPolygonUtils.IsPointInsideOrOnPolygon(poly, p);

                if (inside == entering)
                    t1 = tm;
                else
                    t0 = tm;
            }

            return 0.5 * (t0 + t1);
        }

        private static bool TryIntersectSegments(XYZ p1, XYZ p2, XYZ q1, XYZ q2, out XYZ intersection)
        {
            intersection = null;

            double dx1 = p2.X - p1.X;
            double dy1 = p2.Y - p1.Y;
            double dx2 = q2.X - q1.X;
            double dy2 = q2.Y - q1.Y;

            double denom = dx1 * dy2 - dy1 * dx2;
            if (Math.Abs(denom) < 1e-9)
                return false;

            double t = ((q1.X - p1.X) * dy2 - (q1.Y - p1.Y) * dx2) / denom;
            double u = ((q1.X - p1.X) * dy1 - (q1.Y - p1.Y) * dx1) / denom;

            if (t < -1e-9 || t > 1.0 + 1e-9 || u < -1e-9 || u > 1.0 + 1e-9)
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

            bool aVertical = Math.Abs(a1.X - a2.X) < Tol;
            if (aVertical)
            {
                double aMin = Math.Min(a1.Y, a2.Y);
                double aMax = Math.Max(a1.Y, a2.Y);
                double bMin = Math.Min(b1.Y, b2.Y);
                double bMax = Math.Max(b1.Y, b2.Y);
                return Math.Max(aMin, bMin) <= Math.Min(aMax, bMax) + Tol;
            }
            else
            {
                double aMin = Math.Min(a1.X, a2.X);
                double aMax = Math.Max(a1.X, a2.X);
                double bMin = Math.Min(b1.X, b2.X);
                double bMax = Math.Max(b1.X, b2.X);
                return Math.Max(aMin, bMin) <= Math.Min(aMax, bMax) + Tol;
            }
        }

        private static bool PointOnSegment(XYZ p, XYZ a, XYZ b)
        {
            if (p == null || a == null || b == null)
                return false;

            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (Math.Abs(cross) > SnapTol)
                return false;

            double dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
            if (dot < -SnapTol)
                return false;

            double lenSq = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
            if (dot > lenSq + SnapTol)
                return false;

            return true;
        }

        private static void AddUniquePoint(List<XYZ> pts, XYZ p)
        {
            if (p == null)
                return;

            foreach (XYZ q in pts)
            {
                if (q.DistanceTo(p) <= SnapTol)
                    return;
            }

            pts.Add(p);
        }

        private static string MakePointKey(XYZ p)
        {
            long x = (long)Math.Round(p.X / Tol);
            long y = (long)Math.Round(p.Y / Tol);
            return x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
        }

        private static string MakeUndirectedSegmentKey(XYZ a, XYZ b)
        {
            string ka = MakePointKey(a);
            string kb = MakePointKey(b);

            return string.CompareOrdinal(ka, kb) <= 0
                ? ka + "|" + kb
                : kb + "|" + ka;
        }

        private static string MakeDirectedSegmentKey(string from, string to)
        {
            return from + "->" + to;
        }

        private static string MakePolygonSignature(IList<XYZ> polygon)
        {
            if (polygon == null || polygon.Count == 0)
                return string.Empty;

            List<string> keys = polygon.Select(MakePointKey).ToList();
            int minIndex = 0;

            for (int i = 1; i < keys.Count; i++)
            {
                if (string.CompareOrdinal(keys[i], keys[minIndex]) < 0)
                    minIndex = i;
            }

            List<string> rotated = new List<string>();
            for (int i = 0; i < keys.Count; i++)
                rotated.Add(keys[(minIndex + i) % keys.Count]);

            return string.Join(";", rotated);
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