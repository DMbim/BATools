using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Point = System.Windows.Point;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanPolygonUtils
    {
        public static XYZ FlattenPoint(XYZ p)
        {
            if (p == null) return XYZ.Zero;
            return new XYZ(p.X, p.Y, 0.0);
        }

        public static BoundingBoxUV GetBoundingBox2D(CurveLoop loop)
        {
            List<XYZ> pts = CurveLoopToPolyline(loop);
            if (pts == null || pts.Count == 0)
                return new BoundingBoxUV(0.0, 0.0, 1.0, 1.0);

            double minX = pts.Min(p => p.X);
            double minY = pts.Min(p => p.Y);
            double maxX = pts.Max(p => p.X);
            double maxY = pts.Max(p => p.Y);

            if (Math.Abs(maxX - minX) < KeyplanGeometryTolerance.Epsilon)
                maxX = minX + 1.0;

            if (Math.Abs(maxY - minY) < KeyplanGeometryTolerance.Epsilon)
                maxY = minY + 1.0;

            return new BoundingBoxUV(minX, minY, maxX, maxY);
        }

        public static List<XYZ> CurveLoopToPolyline(CurveLoop loop)
        {
            List<XYZ> pts = new List<XYZ>();
            if (loop == null)
                return pts;

            foreach (Curve c in loop)
            {
                if (c == null)
                    continue;

                IList<XYZ> segPts = c.Tessellate().Select(FlattenPoint).ToList();
                if (segPts.Count == 0)
                    continue;

                if (pts.Count > 0 && pts.Last().DistanceTo(segPts.First()) < KeyplanGeometryTolerance.Point)
                    segPts = segPts.Skip(1).ToList();

                pts.AddRange(segPts);
            }

            if (pts.Count > 1 && pts.First().DistanceTo(pts.Last()) < KeyplanGeometryTolerance.Point)
                pts.RemoveAt(pts.Count - 1);

            return CleanPolygon(pts);
        }

        public static double ComputeSignedArea2D(IList<XYZ> pts)
        {
            double area = 0.0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                XYZ a = pts[i];
                XYZ b = pts[(i + 1) % n];
                area += (a.X * b.Y) - (b.X * a.Y);
            }
            return area * 0.5;
        }

        public static List<XYZ> EnsureCounterClockwise(List<XYZ> pts)
        {
            if (pts == null || pts.Count < 3) return pts;
            if (ComputeSignedArea2D(pts) < 0.0)
            {
                List<XYZ> copy = new List<XYZ>(pts);
                copy.Reverse();
                return copy;
            }
            return pts;
        }

        public static bool IsPointInsidePolygon(IList<XYZ> polygon, XYZ point)
        {
            bool inside = false;
            int n = polygon?.Count ?? 0;
            if (n < 3) return false;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                XYZ pi = polygon[i];
                XYZ pj = polygon[j];

                bool intersect =
                    ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                    (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + KeyplanGeometryTolerance.Epsilon) + pi.X);

                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        public static List<List<XYZ>> RectangleIntersectionWithPolygonAsPolygons(
            IList<XYZ> subjectPolygon,
            double x0,
            double x1,
            double y0,
            double y1)
        {
            List<List<XYZ>> result = new List<List<XYZ>>();

            List<XYZ> subject = CleanPolygon(subjectPolygon);
            if (subject == null || subject.Count < 3)
                return result;

            List<XYZ> rect = CreateRectanglePolygon(x0, x1, y0, y1);

            List<Segment2D> boundarySegments = new List<Segment2D>();

            AddPolygonBoundarySegmentsInsideOtherPolygon(subject, rect, boundarySegments);
            AddPolygonBoundarySegmentsInsideOtherPolygon(rect, subject, boundarySegments);

            boundarySegments = RemoveDuplicateSegments(boundarySegments);

            if (boundarySegments.Count == 0)
                return result;

            List<List<XYZ>> loops = PolygonizeBoundarySegments(boundarySegments);

            foreach (List<XYZ> loop in loops)
            {
                List<XYZ> cleaned = CleanPolygonStrict(loop);

                if (cleaned == null || cleaned.Count < 3)
                    continue;

                double area = Math.Abs(ComputeSignedArea2D(cleaned));
                if (area < KeyplanGeometryTolerance.PolygonArea)
                    continue;

                string reason;
                if (!IsValidFilledRegionPolygon(cleaned, out reason))
                    continue;

                if (ComputeSignedArea2D(cleaned) < 0.0)
                    cleaned.Reverse();

                result.Add(cleaned);
            }

            return result
                .OrderBy(KeyplanGeometryKeyService.MakePolygonKey, StringComparer.Ordinal)
                .ToList();
        }

        public static List<XYZ> ClipPolygonToRectangle(IList<XYZ> polygon, double xMin, double xMax, double yMin, double yMax)
        {
            List<XYZ> output = polygon?.Select(FlattenPoint).ToList() ?? new List<XYZ>();
            if (output.Count < 3)
                return output;

            output = ClipAgainstBoundary(output, p => p.X >= xMin, (s, e) => IntersectVertical(s, e, xMin));
            output = ClipAgainstBoundary(output, p => p.X <= xMax, (s, e) => IntersectVertical(s, e, xMax));
            output = ClipAgainstBoundary(output, p => p.Y >= yMin, (s, e) => IntersectHorizontal(s, e, yMin));
            output = ClipAgainstBoundary(output, p => p.Y <= yMax, (s, e) => IntersectHorizontal(s, e, yMax));

            return CleanPolygon(output);
        }

        public static bool TryCreateCurveLoopFromPolygon(IList<XYZ> polygon, out CurveLoop loop)
        {
            loop = null;

            try
            {
                List<XYZ> pts = CleanPolygonStrict(polygon);
                if (pts == null || pts.Count < 3)
                    return false;

                double signedArea = ComputeSignedArea2D(pts);
                double area = Math.Abs(signedArea);
                if (area < KeyplanGeometryTolerance.PolygonArea)
                    return false;

                if (signedArea < 0.0)
                    pts.Reverse();

                List<Curve> curves = new List<Curve>();

                for (int i = 0; i < pts.Count; i++)
                {
                    XYZ p0 = FlattenPoint(pts[i]);
                    XYZ p1 = FlattenPoint(pts[(i + 1) % pts.Count]);

                    if (p0.DistanceTo(p1) < KeyplanGeometryTolerance.Edge)
                        continue;

                    curves.Add(Line.CreateBound(p0, p1));
                }

                if (curves.Count < 3)
                    return false;

                loop = CurveLoop.Create(curves);
                return true;
            }
            catch
            {
                loop = null;
                return false;
            }
        }

        public static List<XYZ> CleanPolygonStrict(IList<XYZ> polygon)
        {
            List<XYZ> pts = CleanPolygon(polygon);

            pts = RemoveSequentialDuplicates(pts);
            pts = RemoveTinyEdgesWithTolerance(pts, KeyplanGeometryTolerance.TinyEdgeStrict);
            pts = RemoveCollinearVertices(pts);
            pts = RemoveSequentialDuplicates(pts);

            if (pts.Count > 1 && pts.First().DistanceTo(pts.Last()) < KeyplanGeometryTolerance.Point)
                pts.RemoveAt(pts.Count - 1);

            return pts;
        }

        public static List<XYZ> RemoveTinyEdgesWithTolerance(IList<XYZ> pts, double tol)
        {
            List<XYZ> cleaned = new List<XYZ>();
            if (pts == null || pts.Count == 0)
                return cleaned;

            for (int i = 0; i < pts.Count; i++)
            {
                XYZ curr = pts[i];
                XYZ next = pts[(i + 1) % pts.Count];

                if (curr.DistanceTo(next) >= tol)
                    cleaned.Add(curr);
            }

            return cleaned;
        }

        public static List<(XYZ A, XYZ B)> ClipLineByPolygon(
            CurveLoop loop,
            XYZ lineStart,
            XYZ lineEnd)
        {
            if (loop == null)
                return new List<(XYZ A, XYZ B)>();

            List<XYZ> poly = CurveLoopToPolyline(loop);
            return ClipLineByPolygon(poly, lineStart, lineEnd);
        }

        public static List<(XYZ A, XYZ B)> ClipLineByPolygon(
            IList<XYZ> polygon,
            XYZ lineStart,
            XYZ lineEnd)
        {
            List<(XYZ A, XYZ B)> result = new List<(XYZ A, XYZ B)>();

            List<XYZ> poly = CleanPolygonStrict(polygon);
            if (poly == null || poly.Count < 3)
                return result;

            XYZ a = FlattenPoint(lineStart);
            XYZ b = FlattenPoint(lineEnd);

            if (a == null || b == null)
                return result;

            if (a.DistanceTo(b) < KeyplanGeometryTolerance.MinModelSegment)
                return result;

            List<double> parameters = new List<double> { 0.0, 1.0 };

            for (int i = 0; i < poly.Count; i++)
            {
                XYZ c = poly[i];
                XYZ d = poly[(i + 1) % poly.Count];
                CollectSplitParametersForSegmentAgainstEdge(a, b, c, d, parameters);
            }

            List<double> ts = parameters
                .Select(Clamp01)
                .Distinct(new DoubleToleranceComparer(KeyplanGeometryTolerance.Parameter))
                .OrderBy(x => x)
                .ToList();

            for (int i = 0; i < ts.Count - 1; i++)
            {
                double t0 = ts[i];
                double t1 = ts[i + 1];

                if (t1 - t0 < KeyplanGeometryTolerance.Parameter)
                    continue;

                double tm = 0.5 * (t0 + t1);

                XYZ p0 = Lerp(a, b, t0);
                XYZ p1 = Lerp(a, b, t1);
                XYZ pm = Lerp(a, b, tm);

                if (p0.DistanceTo(p1) < KeyplanGeometryTolerance.MinModelSegment)
                    continue;

                if (!IsPointInsideOrOnPolygon(poly, pm))
                    continue;

                p0 = SnapPointToPolygonBoundaryIfNear(p0, poly, KeyplanGeometryTolerance.FaceSnap);
                p1 = SnapPointToPolygonBoundaryIfNear(p1, poly, KeyplanGeometryTolerance.FaceSnap);

                if (p0.DistanceTo(p1) < KeyplanGeometryTolerance.MinModelSegment)
                    continue;

                result.Add((p0, p1));
            }

            return MergeTouchingCollinearSegments(result)
                .OrderBy(x => KeyplanGeometryKeyService.MakeUndirectedLineKey(x.A, x.B), StringComparer.Ordinal)
                .ToList();
        }

        private static XYZ SnapPointToPolygonBoundaryIfNear(XYZ p, IList<XYZ> polygon, double snapTol)
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

            if (lenSq < KeyplanGeometryTolerance.Epsilon)
                return new XYZ(a.X, a.Y, 0.0);

            double t = ((p - a).DotProduct(ab)) / lenSq;

            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;

            XYZ q = a + (ab * t);
            return new XYZ(q.X, q.Y, 0.0);
        }

        private static List<(XYZ A, XYZ B)> MergeTouchingCollinearSegments(IList<(XYZ A, XYZ B)> segments)
        {
            List<(XYZ A, XYZ B)> input = segments?.ToList() ?? new List<(XYZ A, XYZ B)>();
            if (input.Count <= 1)
                return input;

            List<(XYZ A, XYZ B)> ordered = input
                .Where(s => s.A != null && s.B != null && s.A.DistanceTo(s.B) > KeyplanGeometryTolerance.MinModelSegment)
                .OrderBy(s => Math.Min(s.A.X, s.B.X))
                .ThenBy(s => Math.Min(s.A.Y, s.B.Y))
                .ToList();

            List<(XYZ A, XYZ B)> merged = new List<(XYZ A, XYZ B)>();

            foreach ((XYZ A, XYZ B) seg in ordered)
            {
                if (merged.Count == 0)
                {
                    merged.Add(seg);
                    continue;
                }

                (XYZ A, XYZ B) last = merged[merged.Count - 1];

                if (AreSegmentsCollinearAndTouching(last.A, last.B, seg.A, seg.B, KeyplanGeometryTolerance.FaceSnap))
                {
                    List<XYZ> pts = new List<XYZ> { last.A, last.B, seg.A, seg.B };
                    XYZ start = pts.OrderBy(p => ParameterOnSegment(last.A, last.B, p)).First();
                    XYZ end = pts.OrderBy(p => ParameterOnSegment(last.A, last.B, p)).Last();
                    merged[merged.Count - 1] = (start, end);
                }
                else
                {
                    merged.Add(seg);
                }
            }

            return merged;
        }

        private static bool AreSegmentsCollinearAndTouching(XYZ a0, XYZ a1, XYZ b0, XYZ b1, double tol)
        {
            if (!AreCollinear2D(a0, a1, b0) || !AreCollinear2D(a0, a1, b1))
                return false;

            double aMinX = Math.Min(a0.X, a1.X);
            double aMaxX = Math.Max(a0.X, a1.X);
            double aMinY = Math.Min(a0.Y, a1.Y);
            double aMaxY = Math.Max(a0.Y, a1.Y);

            double bMinX = Math.Min(b0.X, b1.X);
            double bMaxX = Math.Max(b0.X, b1.X);
            double bMinY = Math.Min(b0.Y, b1.Y);
            double bMaxY = Math.Max(b0.Y, b1.Y);

            bool overlapX = Math.Max(aMinX, bMinX) <= Math.Min(aMaxX, bMaxX) + tol;
            bool overlapY = Math.Max(aMinY, bMinY) <= Math.Min(aMaxY, bMaxY) + tol;

            return overlapX && overlapY;
        }

        public static bool IsSimplePolygon(IList<XYZ> pts)
        {
            if (pts == null || pts.Count < 3)
                return false;

            for (int i = 0; i < pts.Count; i++)
            {
                XYZ a0 = pts[i];
                XYZ a1 = pts[(i + 1) % pts.Count];

                for (int j = i + 1; j < pts.Count; j++)
                {
                    if (j == i) continue;
                    if ((i + 1) % pts.Count == j) continue;
                    if ((j + 1) % pts.Count == i) continue;

                    XYZ b0 = pts[j];
                    XYZ b1 = pts[(j + 1) % pts.Count];

                    if (SegmentsIntersect2DInternal(a0, a1, b0, b1))
                        return false;
                }
            }

            return true;
        }

        private static XYZ Lerp(XYZ a, XYZ b, double t)
        {
            return new XYZ(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                0.0);
        }

        private static bool TryIntersectSegments(
            XYZ p1, XYZ p2,
            XYZ q1, XYZ q2,
            out XYZ intersection)
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

            t = Clamp01(t);

            intersection = new XYZ(
                p1.X + t * dx1,
                p1.Y + t * dy1,
                0.0);

            return true;
        }

        private static bool SegmentsIntersect2DInternal(XYZ p1, XYZ p2, XYZ q1, XYZ q2)
        {
            double o1 = Orientation2DInternal(p1, p2, q1);
            double o2 = Orientation2DInternal(p1, p2, q2);
            double o3 = Orientation2DInternal(q1, q2, p1);
            double o4 = Orientation2DInternal(q1, q2, p2);

            return ((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) &&
                   ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0));
        }

        private static double Orientation2DInternal(XYZ a, XYZ b, XYZ c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        public static bool IsValidFilledRegionPolygon(IList<XYZ> polygon, out string reason)
        {
            reason = string.Empty;

            List<XYZ> pts = CleanPolygonStrict(polygon);
            if (pts == null || pts.Count < 3)
            {
                reason = "Less than 3 vertices after cleanup.";
                return false;
            }

            double area = Math.Abs(ComputeSignedArea2D(pts));
            if (area < KeyplanGeometryTolerance.FilledRegionArea)
            {
                reason = "Polygon area too small.";
                return false;
            }

            for (int i = 0; i < pts.Count; i++)
            {
                XYZ a0 = pts[i];
                XYZ a1 = pts[(i + 1) % pts.Count];

                if (a0.DistanceTo(a1) < KeyplanGeometryTolerance.TinyEdgeStrict)
                {
                    reason = $"Tiny edge at {i}.";
                    return false;
                }
            }

            for (int i = 0; i < pts.Count; i++)
            {
                XYZ a0 = pts[i];
                XYZ a1 = pts[(i + 1) % pts.Count];

                for (int j = i + 1; j < pts.Count; j++)
                {
                    if (j == i) continue;
                    if ((j + 1) % pts.Count == i) continue;
                    if ((i + 1) % pts.Count == j) continue;

                    XYZ b0 = pts[j];
                    XYZ b1 = pts[(j + 1) % pts.Count];

                    if (SegmentsIntersect2D(a0, a1, b0, b1))
                    {
                        reason = $"Self-intersection between edges {i} and {j}.";
                        return false;
                    }
                }
            }

            reason = "OK";
            return true;
        }

        public static List<XYZ> CleanPolygon(IList<XYZ> polygon)
        {
            List<XYZ> pts = RemoveSequentialDuplicates(polygon);

            if (pts.Count > 1 && pts.First().DistanceTo(pts.Last()) < KeyplanGeometryTolerance.Point)
                pts.RemoveAt(pts.Count - 1);

            pts = RemoveSequentialDuplicates(pts);
            pts = RemoveTinyEdges(pts);
            pts = RemoveCollinearVertices(pts);

            if (pts.Count > 1 && pts.First().DistanceTo(pts.Last()) < KeyplanGeometryTolerance.Point)
                pts.RemoveAt(pts.Count - 1);

            return RemoveSequentialDuplicates(pts);
        }

        public static List<XYZ> RemoveSequentialDuplicates(IList<XYZ> pts)
        {
            List<XYZ> cleaned = new List<XYZ>();
            XYZ last = null;

            foreach (XYZ p in pts ?? Enumerable.Empty<XYZ>())
            {
                if (p == null)
                    continue;

                XYZ fp = FlattenPoint(p);

                if (last == null || last.DistanceTo(fp) > KeyplanGeometryTolerance.Point)
                {
                    cleaned.Add(fp);
                    last = fp;
                }
            }

            return cleaned;
        }

        public static List<XYZ> RemoveTinyEdges(IList<XYZ> pts)
        {
            List<XYZ> cleaned = new List<XYZ>();
            if (pts == null || pts.Count == 0)
                return cleaned;

            for (int i = 0; i < pts.Count; i++)
            {
                XYZ curr = pts[i];
                XYZ next = pts[(i + 1) % pts.Count];

                if (curr.DistanceTo(next) >= KeyplanGeometryTolerance.Edge)
                    cleaned.Add(curr);
            }

            return cleaned;
        }

        public static List<XYZ> RemoveCollinearVertices(IList<XYZ> pts)
        {
            List<XYZ> cleaned = new List<XYZ>();
            if (pts == null || pts.Count < 3)
                return pts?.ToList() ?? new List<XYZ>();

            for (int i = 0; i < pts.Count; i++)
            {
                XYZ prev = pts[(i - 1 + pts.Count) % pts.Count];
                XYZ curr = pts[i];
                XYZ next = pts[(i + 1) % pts.Count];

                if (!AreCollinear2D(prev, curr, next))
                    cleaned.Add(curr);
            }

            return cleaned;
        }

        public static bool AreCollinear2D(XYZ a, XYZ b, XYZ c)
        {
            double area2 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return Math.Abs(area2) < KeyplanGeometryTolerance.PolygonArea;
        }

        private static List<XYZ> ClipAgainstBoundary(
            List<XYZ> input,
            Func<XYZ, bool> inside,
            Func<XYZ, XYZ, XYZ> intersect)
        {
            List<XYZ> output = new List<XYZ>();
            if (input == null || input.Count == 0)
                return output;

            XYZ s = input[input.Count - 1];

            foreach (XYZ e in input)
            {
                bool sInside = inside(s);
                bool eInside = inside(e);

                if (eInside)
                {
                    if (!sInside)
                        output.Add(intersect(s, e));

                    output.Add(e);
                }
                else if (sInside)
                {
                    output.Add(intersect(s, e));
                }

                s = e;
            }

            return output;
        }

        private static XYZ IntersectVertical(XYZ s, XYZ e, double x)
        {
            double dx = e.X - s.X;
            if (Math.Abs(dx) < KeyplanGeometryTolerance.Epsilon)
                return new XYZ(x, s.Y, 0.0);

            double t = (x - s.X) / dx;
            return new XYZ(x, s.Y + t * (e.Y - s.Y), 0.0);
        }

        private static XYZ IntersectHorizontal(XYZ s, XYZ e, double y)
        {
            double dy = e.Y - s.Y;
            if (Math.Abs(dy) < KeyplanGeometryTolerance.Epsilon)
                return new XYZ(s.X, y, 0.0);

            double t = (y - s.Y) / dy;
            return new XYZ(s.X + t * (e.X - s.X), y, 0.0);
        }

        private static bool SegmentsIntersect2D(XYZ p1, XYZ p2, XYZ q1, XYZ q2)
        {
            double o1 = Orientation2D(p1, p2, q1);
            double o2 = Orientation2D(p1, p2, q2);
            double o3 = Orientation2D(q1, q2, p1);
            double o4 = Orientation2D(q1, q2, p2);

            return ((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) &&
                   ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0));
        }

        private static double Orientation2D(XYZ a, XYZ b, XYZ c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        public static List<Point> ToCanvasPoints(IList<XYZ> pts, double width, double height, double padding)
        {
            List<Point> result = new List<Point>();
            if (pts == null || pts.Count == 0)
                return result;

            double minX = pts.Min(p => p.X);
            double minY = pts.Min(p => p.Y);
            double maxX = pts.Max(p => p.X);
            double maxY = pts.Max(p => p.Y);

            double dx = Math.Max(KeyplanGeometryTolerance.MinModelSegment, maxX - minX);
            double dy = Math.Max(KeyplanGeometryTolerance.MinModelSegment, maxY - minY);

            double sx = (width - 2.0 * padding) / dx;
            double sy = (height - 2.0 * padding) / dy;
            double s = Math.Min(sx, sy);

            foreach (XYZ p in pts)
            {
                double x = padding + (p.X - minX) * s;
                double y = height - padding - (p.Y - minY) * s;
                result.Add(new Point(x, y));
            }

            return result;
        }

        private static void AddPolygonBoundarySegmentsInsideOtherPolygon(
            IList<XYZ> sourcePolygon,
            IList<XYZ> clipPolygon,
            List<Segment2D> output)
        {
            if (sourcePolygon == null || clipPolygon == null || output == null)
                return;

            for (int i = 0; i < sourcePolygon.Count; i++)
            {
                XYZ a = sourcePolygon[i];
                XYZ b = sourcePolygon[(i + 1) % sourcePolygon.Count];

                foreach (Segment2D seg in ClipSegmentToPolygon(a, b, clipPolygon))
                {
                    if (seg != null && seg.Length > KeyplanGeometryTolerance.MinModelSegment)
                        output.Add(seg);
                }
            }
        }

        private static List<Segment2D> ClipSegmentToPolygon(XYZ a, XYZ b, IList<XYZ> clipPolygon)
        {
            List<double> parameters = new List<double> { 0.0, 1.0 };

            for (int i = 0; i < clipPolygon.Count; i++)
            {
                XYZ c = clipPolygon[i];
                XYZ d = clipPolygon[(i + 1) % clipPolygon.Count];
                CollectSplitParametersForSegmentAgainstEdge(a, b, c, d, parameters);
            }

            List<double> ts = parameters
                .Select(Clamp01)
                .Distinct(new DoubleToleranceComparer(KeyplanGeometryTolerance.Parameter))
                .OrderBy(x => x)
                .ToList();

            List<Segment2D> result = new List<Segment2D>();

            for (int i = 0; i < ts.Count - 1; i++)
            {
                double t0 = ts[i];
                double t1 = ts[i + 1];

                if (t1 - t0 < KeyplanGeometryTolerance.Parameter)
                    continue;

                double tm = 0.5 * (t0 + t1);

                XYZ p0 = Lerp(a, b, t0);
                XYZ p1 = Lerp(a, b, t1);
                XYZ pm = Lerp(a, b, tm);

                if (p0.DistanceTo(p1) < KeyplanGeometryTolerance.MinModelSegment)
                    continue;

                if (IsPointInsideOrOnPolygon(clipPolygon, pm))
                    result.Add(new Segment2D(p0, p1));
            }

            return result;
        }

        private static void CollectSplitParametersForSegmentAgainstEdge(
            XYZ a,
            XYZ b,
            XYZ c,
            XYZ d,
            List<double> parameters)
        {
            if (a == null || b == null || c == null || d == null || parameters == null)
                return;

            if (TryIntersectSegments(a, b, c, d, out XYZ intersection))
            {
                double t = ParameterOnSegment(a, b, intersection);
                if (t >= -KeyplanGeometryTolerance.Parameter && t <= 1.0 + KeyplanGeometryTolerance.Parameter)
                    parameters.Add(Clamp01(t));
            }

            if (AreCollinear2D(a, b, c) && AreCollinear2D(a, b, d))
            {
                double tc = ParameterOnSegment(a, b, c);
                double td = ParameterOnSegment(a, b, d);

                if (tc >= -KeyplanGeometryTolerance.Parameter && tc <= 1.0 + KeyplanGeometryTolerance.Parameter)
                    parameters.Add(Clamp01(tc));

                if (td >= -KeyplanGeometryTolerance.Parameter && td <= 1.0 + KeyplanGeometryTolerance.Parameter)
                    parameters.Add(Clamp01(td));
            }
        }

        private static double ParameterOnSegment(XYZ a, XYZ b, XYZ p)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                if (Math.Abs(dx) < KeyplanGeometryTolerance.Epsilon) return 0.0;
                return (p.X - a.X) / dx;
            }

            if (Math.Abs(dy) < KeyplanGeometryTolerance.Epsilon) return 0.0;
            return (p.Y - a.Y) / dy;
        }

        private static double Clamp01(double t)
        {
            if (t < 0.0) return 0.0;
            if (t > 1.0) return 1.0;
            return t;
        }

        private static List<Segment2D> RemoveDuplicateSegments(IList<Segment2D> segments)
        {
            Dictionary<string, Segment2D> unique = new Dictionary<string, Segment2D>(StringComparer.Ordinal);

            foreach (Segment2D seg in segments ?? Enumerable.Empty<Segment2D>())
            {
                if (seg == null || seg.Length < KeyplanGeometryTolerance.MinModelSegment)
                    continue;

                XYZ a = FlattenPoint(seg.A);
                XYZ b = FlattenPoint(seg.B);

                if (a.DistanceTo(b) < KeyplanGeometryTolerance.MinModelSegment)
                    continue;

                string key = KeyplanGeometryKeyService.MakeUndirectedLineKey(a, b);

                if (!unique.ContainsKey(key))
                    unique[key] = new Segment2D(a, b);
            }

            return unique.Values.ToList();
        }

        private static string MakeDirectedSegmentKey(string from, string to)
        {
            return from + "->" + to;
        }

        private static List<List<XYZ>> PolygonizeBoundarySegments(IList<Segment2D> segments)
        {
            List<List<XYZ>> result = new List<List<XYZ>>();
            if (segments == null || segments.Count == 0)
                return result;

            Dictionary<string, XYZ> vertexPoints = new Dictionary<string, XYZ>(StringComparer.Ordinal);
            Dictionary<string, List<HalfEdge>> outgoing = new Dictionary<string, List<HalfEdge>>(StringComparer.Ordinal);

            foreach (Segment2D seg in segments)
            {
                string aKey = KeyplanGeometryKeyService.MakePointKey(seg.A);
                string bKey = KeyplanGeometryKeyService.MakePointKey(seg.B);

                if (aKey == bKey)
                    continue;

                if (!vertexPoints.ContainsKey(aKey))
                    vertexPoints[aKey] = seg.A;

                if (!vertexPoints.ContainsKey(bKey))
                    vertexPoints[bKey] = seg.B;

                AddHalfEdge(outgoing, aKey, bKey, vertexPoints[aKey], vertexPoints[bKey]);
                AddHalfEdge(outgoing, bKey, aKey, vertexPoints[bKey], vertexPoints[aKey]);
            }

            foreach (List<HalfEdge> list in outgoing.Values)
                list.Sort((x, y) => x.Angle.CompareTo(y.Angle));

            HashSet<string> usedDirected = new HashSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, List<HalfEdge>> kvp in outgoing.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                foreach (HalfEdge startEdge in kvp.Value)
                {
                    string startDirKey = MakeDirectedSegmentKey(startEdge.FromKey, startEdge.ToKey);
                    if (usedDirected.Contains(startDirKey))
                        continue;

                    List<XYZ> loop = TraceFace(startEdge, outgoing, vertexPoints, usedDirected);
                    if (loop == null || loop.Count < 3)
                        continue;

                    List<XYZ> cleaned = CleanPolygonStrict(loop);
                    if (cleaned == null || cleaned.Count < 3)
                        continue;

                    double area = ComputeSignedArea2D(cleaned);
                    if (Math.Abs(area) < KeyplanGeometryTolerance.PolygonArea)
                        continue;

                    if (area < 0.0)
                        cleaned.Reverse();

                    string signature = KeyplanGeometryKeyService.MakePolygonKey(cleaned);
                    bool exists = result.Any(r => KeyplanGeometryKeyService.MakePolygonKey(r) == signature);
                    if (!exists)
                        result.Add(cleaned);
                }
            }

            return result;
        }

        private static List<XYZ> TraceFace(
            HalfEdge startEdge,
            Dictionary<string, List<HalfEdge>> outgoing,
            Dictionary<string, XYZ> vertexPoints,
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

                if (!vertexPoints.TryGetValue(current.FromKey, out XYZ fromPt))
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

            if (loop.Count > 1 && loop.First().DistanceTo(loop.Last()) < KeyplanGeometryTolerance.Point)
                loop.RemoveAt(loop.Count - 1);

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

        public static List<XYZ> CreateRectanglePolygon(double x0, double x1, double y0, double y1)
        {
            return new List<XYZ>
            {
                new XYZ(x0, y0, 0.0),
                new XYZ(x1, y0, 0.0),
                new XYZ(x1, y1, 0.0),
                new XYZ(x0, y1, 0.0)
            };
        }

        public static double EstimatePolygonOccupancyInRectangle(
            IList<XYZ> polygon,
            double x0,
            double x1,
            double y0,
            double y1,
            int samplesX,
            int samplesY)
        {
            if (polygon == null || polygon.Count < 3)
                return 0.0;

            if (x1 <= x0 || y1 <= y0)
                return 0.0;

            samplesX = Math.Max(1, samplesX);
            samplesY = Math.Max(1, samplesY);

            int insideCount = 0;
            int totalCount = samplesX * samplesY;

            double dx = (x1 - x0) / samplesX;
            double dy = (y1 - y0) / samplesY;

            for (int ix = 0; ix < samplesX; ix++)
            {
                for (int iy = 0; iy < samplesY; iy++)
                {
                    double px = x0 + (ix + 0.5) * dx;
                    double py = y0 + (iy + 0.5) * dy;

                    XYZ sample = new XYZ(px, py, 0.0);

                    if (IsPointInsideOrOnPolygon(polygon, sample))
                        insideCount++;
                }
            }

            return totalCount > 0 ? (double)insideCount / totalCount : 0.0;
        }

        public static bool IsPointInsideOrOnPolygon(IList<XYZ> polygon, XYZ point)
        {
            if (polygon == null || polygon.Count < 3 || point == null)
                return false;

            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ a = polygon[i];
                XYZ b = polygon[(i + 1) % polygon.Count];

                if (PointOnSegment2D(point, a, b, KeyplanGeometryTolerance.PointOnSegment))
                    return true;
            }

            return IsPointInsidePolygon(polygon, point);
        }

        private static bool PointOnSegment2D(XYZ p, XYZ a, XYZ b, double tol)
        {
            if (p == null || a == null || b == null)
                return false;

            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (Math.Abs(cross) > tol)
                return false;

            double dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
            if (dot < -tol)
                return false;

            double lenSq = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
            if (dot > lenSq + tol)
                return false;

            return true;
        }

        private sealed class Segment2D
        {
            public XYZ A { get; }
            public XYZ B { get; }
            public double Length => A.DistanceTo(B);

            public Segment2D(XYZ a, XYZ b)
            {
                A = FlattenPoint(a);
                B = FlattenPoint(b);
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

        private sealed class DoubleToleranceComparer : IEqualityComparer<double>
        {
            private readonly double _tol;

            public DoubleToleranceComparer(double tol)
            {
                _tol = Math.Abs(tol);
            }

            public bool Equals(double x, double y)
            {
                return Math.Abs(x - y) <= _tol;
            }

            public int GetHashCode(double obj)
            {
                return Math.Round(obj / _tol).GetHashCode();
            }
        }
    }
}