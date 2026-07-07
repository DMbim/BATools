using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanGridBuilder
    {
        private const double MinimumAcceptedArea = 1e-6;

        public static List<GridCellResult> BuildCells(
            CurveLoop outerLoop,
            double[] xBreaks,
            double[] yBreaks,
            KeyplanCellFillMode fillMode,
            double minimumOccupancyRatio)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));
            if (xBreaks == null || xBreaks.Length < 2) throw new ArgumentException("Invalid X breaks.", nameof(xBreaks));
            if (yBreaks == null || yBreaks.Length < 2) throw new ArgumentException("Invalid Y breaks.", nameof(yBreaks));

            minimumOccupancyRatio = Clamp01(minimumOccupancyRatio);

            List<GridCellResult> result = new List<GridCellResult>();

            List<XYZ> outlinePts = KeyplanPolygonUtils.CurveLoopToPolyline(outerLoop);
            if (outlinePts == null || outlinePts.Count < 3)
                return result;

            BoundingBoxUV bb = KeyplanPolygonUtils.GetBoundingBox2D(outerLoop);

            double minX = bb.Min.U;
            double minY = bb.Min.V;
            double maxX = bb.Max.U;
            double maxY = bb.Max.V;

            double[] xs = NormalizeBreaks(xBreaks);
            double[] ys = NormalizeBreaks(yBreaks);

            for (int ix = 0; ix < xs.Length - 1; ix++)
            {
                for (int iy = 0; iy < ys.Length - 1; iy++)
                {
                    double x0 = minX + (maxX - minX) * xs[ix];
                    double x1 = minX + (maxX - minX) * xs[ix + 1];
                    double y0 = minY + (maxY - minY) * ys[iy];
                    double y1 = minY + (maxY - minY) * ys[iy + 1];

                    if (x1 <= x0 || y1 <= y0)
                        continue;

                    double cellArea = Math.Abs((x1 - x0) * (y1 - y0));
                    if (cellArea < MinimumAcceptedArea)
                        continue;

                    List<XYZ> rectangle = KeyplanPolygonUtils.CreateRectanglePolygon(x0, x1, y0, y1);

                    double occupancyRatio = KeyplanPolygonUtils.EstimatePolygonOccupancyInRectangle(
                        outlinePts, x0, x1, y0, y1, 13, 13);

                    XYZ center = new XYZ((x0 + x1) * 0.5, (y0 + y1) * 0.5, 0.0);
                    bool hasCellCenterInside = KeyplanPolygonUtils.IsPointInsideOrOnPolygon(outlinePts, center);

                    bool containsPolygonVertex = outlinePts.Any(p => IsPointInsideRectangleInclusive(p, x0, x1, y0, y1, 1e-6));
                    bool hasMeaningfulBoundaryCrossing = HasMeaningfulBoundaryCrossing(outlinePts, x0, x1, y0, y1);

                    bool touchesPolygon =
                        occupancyRatio > 0.0 ||
                        containsPolygonVertex ||
                        hasMeaningfulBoundaryCrossing ||
                        hasCellCenterInside;

                    List<List<XYZ>> exactPolygons = new List<List<XYZ>>();
                    if (fillMode == KeyplanCellFillMode.ExactClipped)
                    {
                        List<List<XYZ>> clipped =
                            KeyplanPolygonUtils.RectangleIntersectionWithPolygonAsPolygons(outlinePts, x0, x1, y0, y1);

                        foreach (List<XYZ> polygon in clipped ?? Enumerable.Empty<List<XYZ>>())
                        {
                            if (polygon == null || polygon.Count < 3)
                                continue;

                            List<XYZ> pts = KeyplanPolygonUtils.CleanPolygonStrict(polygon);
                            if (pts == null || pts.Count < 3)
                                continue;

                            double area = Math.Abs(KeyplanPolygonUtils.ComputeSignedArea2D(pts));
                            if (area < MinimumAcceptedArea)
                                continue;

                            exactPolygons.Add(pts);
                        }

                        if (exactPolygons.Count == 0)
                            continue;
                    }

                    bool shouldFill = ShouldFillCell(
                        fillMode,
                        occupancyRatio,
                        minimumOccupancyRatio,
                        touchesPolygon,
                        hasCellCenterInside,
                        containsPolygonVertex,
                        hasMeaningfulBoundaryCrossing,
                        exactPolygons);

                    if (!shouldFill)
                        continue;

                    result.Add(new GridCellResult
                    {
                        XIndex = ix,
                        YIndex = iy,
                        X0 = x0,
                        X1 = x1,
                        Y0 = y0,
                        Y1 = y1,
                        CellArea = cellArea,
                        OccupancyRatio = occupancyRatio,
                        RectanglePolygon = rectangle,
                        ExactPolygons = exactPolygons
                    });
                }
            }

            return result;
        }

        public static List<GridCellResult> BuildCells(
            CurveLoop outerLoop,
            IReadOnlyCollection<KeyplanSplitLineItem> verticalSplits,
            IReadOnlyCollection<KeyplanSplitLineItem> horizontalSplits,
            KeyplanCellFillMode fillMode,
            double minimumOccupancyRatio)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));

            double[] xBreaks = KeyplanSplitConversionService.ToBreakArray(verticalSplits, AxisOrientation.Vertical);
            double[] yBreaks = KeyplanSplitConversionService.ToBreakArray(horizontalSplits, AxisOrientation.Horizontal);

            return BuildCells(outerLoop, xBreaks, yBreaks, fillMode, minimumOccupancyRatio);
        }

        public static List<(XYZ A, XYZ B)> BuildGridLines(
            CurveLoop outerLoop,
            double[] xBreaks,
            double[] yBreaks)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));
            if (xBreaks == null) throw new ArgumentNullException(nameof(xBreaks));
            if (yBreaks == null) throw new ArgumentNullException(nameof(yBreaks));

            List<(XYZ A, XYZ B)> lines = new List<(XYZ A, XYZ B)>();

            BoundingBoxUV bb = KeyplanPolygonUtils.GetBoundingBox2D(outerLoop);
            double minX = bb.Min.U;
            double minY = bb.Min.V;
            double maxX = bb.Max.U;
            double maxY = bb.Max.V;

            double[] xs = NormalizeBreaks(xBreaks);
            double[] ys = NormalizeBreaks(yBreaks);

            foreach (double xb in xs)
            {
                double x = minX + (maxX - minX) * xb;
                lines.Add((new XYZ(x, minY, 0.0), new XYZ(x, maxY, 0.0)));
            }

            foreach (double yb in ys)
            {
                double y = minY + (maxY - minY) * yb;
                lines.Add((new XYZ(minX, y, 0.0), new XYZ(maxX, y, 0.0)));
            }

            return lines;
        }

        public static List<(XYZ A, XYZ B)> BuildGridLines(
            CurveLoop outerLoop,
            IReadOnlyCollection<KeyplanSplitLineItem> verticalSplits,
            IReadOnlyCollection<KeyplanSplitLineItem> horizontalSplits)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));

            List<(XYZ A, XYZ B)> lines = new List<(XYZ A, XYZ B)>();

            BoundingBoxUV bb = KeyplanPolygonUtils.GetBoundingBox2D(outerLoop);
            double minX = bb.Min.U;
            double minY = bb.Min.V;
            double maxX = bb.Max.U;
            double maxY = bb.Max.V;

            List<KeyplanSplitLineItem> vSplits = KeyplanSplitConversionService.CloneEnabledOrdered(verticalSplits, AxisOrientation.Vertical);
            List<KeyplanSplitLineItem> hSplits = KeyplanSplitConversionService.CloneEnabledOrdered(horizontalSplits, AxisOrientation.Horizontal);

            foreach (KeyplanSplitLineItem split in vSplits)
            {
                double x = minX + (maxX - minX) * split.Normalized;
                lines.Add((new XYZ(x, minY, 0.0), new XYZ(x, maxY, 0.0)));
            }

            foreach (KeyplanSplitLineItem split in hSplits)
            {
                double y = minY + (maxY - minY) * split.Normalized;
                lines.Add((new XYZ(minX, y, 0.0), new XYZ(maxX, y, 0.0)));
            }

            return lines;
        }

        private static bool ShouldFillCell(
            KeyplanCellFillMode fillMode,
            double occupancyRatio,
            double minimumOccupancyRatio,
            bool touchesPolygon,
            bool hasCellCenterInside,
            bool containsPolygonVertex,
            bool hasMeaningfulBoundaryCrossing,
            List<List<XYZ>> exactPolygons)
        {
            switch (fillMode)
            {
                case KeyplanCellFillMode.ExactClipped:
                    return exactPolygons != null && exactPolygons.Count > 0;

                case KeyplanCellFillMode.FullCellIfTouched:
                    return touchesPolygon;

                case KeyplanCellFillMode.FullCellIfOccupied:
                default:
                    if (occupancyRatio >= minimumOccupancyRatio)
                        return true;

                    if (hasCellCenterInside)
                        return true;

                    if (containsPolygonVertex)
                        return true;

                    if (hasMeaningfulBoundaryCrossing)
                        return true;

                    return false;
            }
        }

        private static bool IsPointInsideRectangleInclusive(
            XYZ p,
            double x0,
            double x1,
            double y0,
            double y1,
            double tol)
        {
            if (p == null)
                return false;

            return p.X >= x0 - tol &&
                   p.X <= x1 + tol &&
                   p.Y >= y0 - tol &&
                   p.Y <= y1 + tol;
        }

        private static bool HasMeaningfulBoundaryCrossing(
            IList<XYZ> polygon,
            double x0,
            double x1,
            double y0,
            double y1)
        {
            if (polygon == null || polygon.Count < 2)
                return false;

            double minMeaningfulLength = 0.10 * Math.Min(Math.Abs(x1 - x0), Math.Abs(y1 - y0));

            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ a = polygon[i];
                XYZ b = polygon[(i + 1) % polygon.Count];

                if (a == null || b == null)
                    continue;

                if (TryClipSegmentToRectangle(a, b, x0, x1, y0, y1, out XYZ c0, out XYZ c1))
                {
                    if (c0 != null && c1 != null && c0.DistanceTo(c1) >= minMeaningfulLength)
                        return true;
                }
            }

            return false;
        }

        private static bool TryClipSegmentToRectangle(
            XYZ a,
            XYZ b,
            double xMin,
            double xMax,
            double yMin,
            double yMax,
            out XYZ clippedStart,
            out XYZ clippedEnd)
        {
            clippedStart = null;
            clippedEnd = null;

            double x0 = a.X;
            double y0 = a.Y;
            double x1 = b.X;
            double y1 = b.Y;

            double dx = x1 - x0;
            double dy = y1 - y0;

            double t0 = 0.0;
            double t1 = 1.0;

            if (!ClipTest(-dx, x0 - xMin, ref t0, ref t1)) return false;
            if (!ClipTest(dx, xMax - x0, ref t0, ref t1)) return false;
            if (!ClipTest(-dy, y0 - yMin, ref t0, ref t1)) return false;
            if (!ClipTest(dy, yMax - y0, ref t0, ref t1)) return false;

            if (t1 < t0)
                return false;

            clippedStart = new XYZ(x0 + t0 * dx, y0 + t0 * dy, 0.0);
            clippedEnd = new XYZ(x0 + t1 * dx, y0 + t1 * dy, 0.0);

            return clippedStart.DistanceTo(clippedEnd) > 1e-9;
        }

        private static bool ClipTest(double p, double q, ref double t0, ref double t1)
        {
            const double eps = 1e-12;

            if (Math.Abs(p) < eps)
                return q >= 0.0;

            double r = q / p;

            if (p < 0.0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }

            return true;
        }

        private static double[] NormalizeBreaks(double[] breaks)
        {
            if (breaks == null || breaks.Length == 0)
                return new[] { 0.0, 1.0 };

            double[] copy = breaks
                .Select(v =>
                {
                    if (v < 0.0) return 0.0;
                    if (v > 1.0) return 1.0;
                    return v;
                })
                .ToArray();

            Array.Sort(copy);

            copy[0] = 0.0;
            copy[copy.Length - 1] = 1.0;

            const double minGap = 1e-6;
            for (int i = 1; i < copy.Length; i++)
            {
                if (copy[i] < copy[i - 1] + minGap)
                    copy[i] = copy[i - 1] + minGap;
            }

            copy[copy.Length - 1] = 1.0;
            return copy;
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }
    }

    public sealed class GridCellResult
    {
        public int XIndex { get; set; }
        public int YIndex { get; set; }

        public double X0 { get; set; }
        public double X1 { get; set; }
        public double Y0 { get; set; }
        public double Y1 { get; set; }

        public double CellArea { get; set; }
        public double OccupancyRatio { get; set; }

        public string CellKey => $"{XIndex}:{YIndex}";

        public bool IsExcluded { get; set; }

        public string MergeGroupId { get; set; } = string.Empty;

        public List<XYZ> RectanglePolygon { get; set; } = new List<XYZ>();

        public List<List<XYZ>> ExactPolygons { get; set; } = new List<List<XYZ>>();

        public IEnumerable<List<XYZ>> GetPreviewPolygons(KeyplanCellFillMode fillMode)
        {
            if (IsExcluded)
                return Enumerable.Empty<List<XYZ>>();

            if (fillMode == KeyplanCellFillMode.ExactClipped)
                return ExactPolygons ?? Enumerable.Empty<List<XYZ>>();

            return RectanglePolygon != null && RectanglePolygon.Count >= 3
                ? new[] { RectanglePolygon }
                : Enumerable.Empty<List<XYZ>>();
        }

        public IEnumerable<List<XYZ>> GetGenerationPolygons(KeyplanCellFillMode fillMode)
        {
            if (IsExcluded)
                return Enumerable.Empty<List<XYZ>>();

            return GetPreviewPolygons(fillMode);
        }
    }
}