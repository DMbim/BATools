
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanSplitSnapService
    {
        private const double DefaultSnapThresholdNormalized = 0.015;
        private const double DistinctTol = 1e-6;

        public static double GetSnappedNormalized(
            CurveLoop outerLoop,
            IEnumerable<KeyplanSplitLineItem> verticalSplits,
            IEnumerable<KeyplanSplitLineItem> horizontalSplits,
            AxisOrientation orientation,
            string movingSplitId,
            double normalized)
        {
            normalized = Clamp01(normalized);

            if (outerLoop == null)
                return normalized;

            List<double> targets = BuildSnapTargets(
                outerLoop,
                verticalSplits,
                horizontalSplits,
                orientation,
                movingSplitId);

            if (targets.Count == 0)
                return normalized;

            double best = normalized;
            double bestDist = double.MaxValue;

            foreach (double t in targets)
            {
                double d = Math.Abs(t - normalized);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }

            if (bestDist <= DefaultSnapThresholdNormalized)
                return Clamp01(best);

            return normalized;
        }

        private static List<double> BuildSnapTargets(
            CurveLoop outerLoop,
            IEnumerable<KeyplanSplitLineItem> verticalSplits,
            IEnumerable<KeyplanSplitLineItem> horizontalSplits,
            AxisOrientation orientation,
            string movingSplitId)
        {
            List<double> targets = new List<double> { 0.0, 1.0 };

            List<XYZ> outline = KeyplanPolygonUtils.CurveLoopToPolyline(outerLoop);
            outline = KeyplanPolygonUtils.CleanPolygonStrict(outline);

            if (outline != null && outline.Count >= 2)
            {
                BoundingBoxUV bb = KeyplanPolygonUtils.GetBoundingBox2D(outerLoop);

                double minX = bb.Min.U;
                double minY = bb.Min.V;
                double maxX = bb.Max.U;
                double maxY = bb.Max.V;

                double dx = Math.Max(1e-12, maxX - minX);
                double dy = Math.Max(1e-12, maxY - minY);

                foreach (XYZ p in outline)
                {
                    if (orientation == AxisOrientation.Vertical)
                    {
                        double nx = (p.X - minX) / dx;
                        targets.Add(Clamp01(nx));
                    }
                    else
                    {
                        double ny = (p.Y - minY) / dy;
                        targets.Add(Clamp01(ny));
                    }
                }
            }

            IEnumerable<KeyplanSplitLineItem> sameOrientationSplits =
                orientation == AxisOrientation.Vertical
                    ? (verticalSplits ?? Enumerable.Empty<KeyplanSplitLineItem>())
                    : (horizontalSplits ?? Enumerable.Empty<KeyplanSplitLineItem>());

            foreach (KeyplanSplitLineItem split in sameOrientationSplits)
            {
                if (split == null || !split.IsEnabled)
                    continue;

                if (!string.IsNullOrWhiteSpace(movingSplitId) &&
                    string.Equals(split.Id, movingSplitId, StringComparison.Ordinal))
                    continue;

                targets.Add(Clamp01(split.Normalized));
            }

            return targets
                .Distinct(new DoubleToleranceComparer(DistinctTol))
                .OrderBy(x => x)
                .ToList();
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
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