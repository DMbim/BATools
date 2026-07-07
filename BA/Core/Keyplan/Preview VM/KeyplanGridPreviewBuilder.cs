using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Point = System.Windows.Point;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanGridPreviewBuilder
    {
        public static KeyplanGridPreviewData BuildPreview(
            CurveLoop outerLoop,
            double[] xBreaks,
            double[] yBreaks,
            KeyplanCellFillMode fillMode,
            double minimumOccupancyRatio,
            bool drawOutline,
            bool useOutlineAsPrimaryFill,
            bool drawGridLines,
            bool drawFilledPolygons,
            double globalScaleFactor,
            double canvasWidth,
            double canvasHeight,
            double padding,
            IReadOnlyDictionary<string, CellEditState> cellEdits,
            IReadOnlyCollection<string> selectedCellKeys,
            IReadOnlyCollection<KeyplanSplitLineItem> verticalSplits,
            IReadOnlyCollection<KeyplanSplitLineItem> horizontalSplits,
            IReadOnlyDictionary<string, string> committedZoneLabels = null)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));

            KeyplanGridPreviewData preview = new KeyplanGridPreviewData();

            KeyplanGridOptions options = new KeyplanGridOptions
            {
                DrawOutline = drawOutline,
                UseOutlineAsPrimaryFill = useOutlineAsPrimaryFill,
                DrawGridLines = drawGridLines,
                CreateFilledRegions = drawFilledPolygons,
                FillMode = fillMode,
                MinimumOccupancyRatio = minimumOccupancyRatio,
                GlobalScaleFactor = globalScaleFactor
            };

            KeyplanGraphicModel graphicModel = KeyplanGraphicBuilder.Build(
                outerLoop,
                options,
                verticalSplits,
                horizontalSplits,
                cellEdits);

            graphicModel = KeyplanGraphicScaleService.ScaleModel(graphicModel, options.GlobalScaleFactor);

            List<XYZ> allPoints = CollectAllPoints(graphicModel);
            if (allPoints.Count == 0)
                return preview;

            double minX = allPoints.Min(p => p.X);
            double minY = allPoints.Min(p => p.Y);
            double maxX = allPoints.Max(p => p.X);
            double maxY = allPoints.Max(p => p.Y);

            double dx = Math.Max(1e-6, maxX - minX);
            double dy = Math.Max(1e-6, maxY - minY);

            double sx = (canvasWidth - 2.0 * padding) / dx;
            double sy = (canvasHeight - 2.0 * padding) / dy;
            double scale = Math.Min(sx, sy);

            preview.Transform = new PreviewTransformInfo
            {
                ModelMinX = minX,
                ModelMinY = minY,
                ModelMaxX = maxX,
                ModelMaxY = maxY,
                CanvasWidth = canvasWidth,
                CanvasHeight = canvasHeight,
                Padding = padding,
                Scale = scale
            };

            HashSet<string> selectedKeys = selectedCellKeys != null
                ? new HashSet<string>(selectedCellKeys, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            if (drawOutline && graphicModel.OutlineLines.Count > 0)
            {
                List<XYZ> outlinePolyline = BuildOutlinePolylineFromConnectivity(graphicModel.OutlineLines);
                if (outlinePolyline.Count > 0)
                    preview.Outline = outlinePolyline.Select(p => ModelToCanvas(p, preview.Transform)).ToList();
            }
            if (drawFilledPolygons)
            {
                foreach (KeyplanPolygonGraphicItem polygon in graphicModel.FilledRegions
                    .OrderBy(x => x.StableKey, StringComparer.Ordinal))
                {
                    if (polygon?.Polygon == null || polygon.Polygon.Count < 3)
                        continue;
                    string previewKey = polygon.StableKey;

                    string committedLabel = string.Empty;
                    if (committedZoneLabels != null &&
                        committedZoneLabels.TryGetValue(previewKey, out string foundLabel))
                    {
                        committedLabel = foundLabel ?? string.Empty;
                    }

                    preview.FilledPolygons.Add(new PreviewCellPolygon
                    {
                        CellKey = previewKey,
                        XIndex = -1,
                        YIndex = -1,
                        IsExcluded = false,
                        IsSelected = selectedKeys.Contains(previewKey),
                        Points = polygon.Polygon.Select(p => ModelToCanvas(p, preview.Transform)).ToList(),
                        ZoneLabel = committedLabel
                    });
                }
            }

            if (drawGridLines)
            {
                foreach (KeyplanLineGraphicItem line in graphicModel.GridLines
                    .OrderBy(x => x.StableKey, StringComparer.Ordinal))
                {
                    Point p0 = ModelToCanvas(line.A, preview.Transform);
                    Point p1 = ModelToCanvas(line.B, preview.Transform);
                    preview.GridLines.Add((p0, p1));
                }
            }

            foreach (KeyplanSplitLineItem split in (verticalSplits ?? Array.Empty<KeyplanSplitLineItem>())
                .Where(x => x != null)
                .OrderBy(x => x.Normalized))
            {
                double modelX = minX + (maxX - minX) * split.Normalized;
                Point pt = ModelToCanvas(new XYZ(modelX, minY, 0.0), preview.Transform);

                preview.VerticalAxes.Add(new AxisPreviewInfo
                {
                    SplitId = split.Id,
                    Orientation = AxisOrientation.Vertical,
                    InteriorIndex = -1,
                    Normalized = split.Normalized,
                    CanvasPosition = pt.X,
                    IsSelected = split.IsSelected,
                    IsEnabled = split.IsEnabled,
                    DisplayName = split.Name
                });
            }

            foreach (KeyplanSplitLineItem split in (horizontalSplits ?? Array.Empty<KeyplanSplitLineItem>())
                .Where(x => x != null)
                .OrderBy(x => x.Normalized))
            {
                double modelY = minY + (maxY - minY) * split.Normalized;
                Point pt = ModelToCanvas(new XYZ(minX, modelY, 0.0), preview.Transform);

                preview.HorizontalAxes.Add(new AxisPreviewInfo
                {
                    SplitId = split.Id,
                    Orientation = AxisOrientation.Horizontal,
                    InteriorIndex = -1,
                    Normalized = split.Normalized,
                    CanvasPosition = pt.Y,
                    IsSelected = split.IsSelected,
                    IsEnabled = split.IsEnabled,
                    DisplayName = split.Name
                });
            }

            return preview;
        }

        public static Point ModelToCanvas(XYZ p, PreviewTransformInfo t)
        {
            double x = t.Padding + (p.X - t.ModelMinX) * t.Scale;
            double y = t.CanvasHeight - t.Padding - (p.Y - t.ModelMinY) * t.Scale;
            return new Point(x, y);
        }

        private static List<XYZ> CollectAllPoints(KeyplanGraphicModel model)
        {
            List<XYZ> pts = new List<XYZ>();

            foreach (KeyplanPolygonGraphicItem poly in model.FilledRegions ?? Enumerable.Empty<KeyplanPolygonGraphicItem>())
            {
                if (poly?.Polygon == null)
                    continue;

                pts.AddRange(poly.Polygon.Where(p => p != null).Select(KeyplanPolygonUtils.FlattenPoint));
            }

            foreach (KeyplanLineGraphicItem line in model.GridLines ?? Enumerable.Empty<KeyplanLineGraphicItem>())
            {
                if (line == null)
                    continue;

                pts.Add(KeyplanPolygonUtils.FlattenPoint(line.A));
                pts.Add(KeyplanPolygonUtils.FlattenPoint(line.B));
            }

            foreach (KeyplanLineGraphicItem line in model.OutlineLines ?? Enumerable.Empty<KeyplanLineGraphicItem>())
            {
                if (line == null)
                    continue;

                pts.Add(KeyplanPolygonUtils.FlattenPoint(line.A));
                pts.Add(KeyplanPolygonUtils.FlattenPoint(line.B));
            }

            return pts.Where(p => p != null).ToList();
        }

        private static List<XYZ> BuildOutlinePolylineFromConnectivity(IList<KeyplanLineGraphicItem> outlineLines)
        {
            List<XYZ> result = new List<XYZ>();
            if (outlineLines == null || outlineLines.Count == 0)
                return result;

            List<Segment2D> remaining = outlineLines
                .Where(x => x != null && x.A != null && x.B != null)
                .Select(x => new Segment2D(
                    KeyplanPolygonUtils.FlattenPoint(x.A),
                    KeyplanPolygonUtils.FlattenPoint(x.B)))
                .Where(x => x.A.DistanceTo(x.B) > KeyplanGeometryTolerance.MinModelSegment)
                .ToList();

            if (remaining.Count == 0)
                return result;

            Segment2D first = remaining[0];
            remaining.RemoveAt(0);

            result.Add(first.A);
            result.Add(first.B);

            while (remaining.Count > 0)
            {
                XYZ tail = result[result.Count - 1];
                int nextIndex = -1;
                bool reverse = false;

                for (int i = 0; i < remaining.Count; i++)
                {
                    Segment2D seg = remaining[i];

                    if (tail.DistanceTo(seg.A) <= KeyplanGeometryTolerance.Point)
                    {
                        nextIndex = i;
                        reverse = false;
                        break;
                    }

                    if (tail.DistanceTo(seg.B) <= KeyplanGeometryTolerance.Point)
                    {
                        nextIndex = i;
                        reverse = true;
                        break;
                    }
                }

                if (nextIndex < 0)
                    break;

                Segment2D next = remaining[nextIndex];
                remaining.RemoveAt(nextIndex);

                result.Add(reverse ? next.A : next.B);
            }

            if (result.Count > 1 && result.First().DistanceTo(result.Last()) <= KeyplanGeometryTolerance.Point)
                result.RemoveAt(result.Count - 1);

            return KeyplanPolygonUtils.CleanPolygonStrict(result);
        }

        private sealed class Segment2D
        {
            public XYZ A { get; }
            public XYZ B { get; }

            public Segment2D(XYZ a, XYZ b)
            {
                A = a;
                B = b;
            }
        }
    }
}