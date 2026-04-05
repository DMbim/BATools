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
            double canvasWidth,
            double canvasHeight,
            double padding,
            IReadOnlyDictionary<string, CellEditState> cellEdits,
            IReadOnlyCollection<string> selectedCellKeys)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));

            KeyplanGridPreviewData preview = new KeyplanGridPreviewData();

            List<XYZ> outlinePts = KeyplanPolygonUtils.CurveLoopToPolyline(outerLoop);
            if (outlinePts.Count == 0)
                return preview;

            double minX = outlinePts.Min(p => p.X);
            double minY = outlinePts.Min(p => p.Y);
            double maxX = outlinePts.Max(p => p.X);
            double maxY = outlinePts.Max(p => p.Y);

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

            if (drawOutline)
                preview.Outline = outlinePts.Select(p => ModelToCanvas(p, preview.Transform)).ToList();

            List<GridCellResult> cells = KeyplanGridBuilder.BuildCells(
                outerLoop,
                xBreaks,
                yBreaks,
                fillMode,
                minimumOccupancyRatio);

            ApplyCellEditStates(cells, cellEdits);

            HashSet<string> selectedKeys = selectedCellKeys != null
                ? new HashSet<string>(selectedCellKeys, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            if (drawFilledPolygons && useOutlineAsPrimaryFill)
            {
                List<List<XYZ>> faces = KeyplanFaceBuilder.BuildFaces(outerLoop, xBreaks, yBreaks);

                for (int i = 0; i < faces.Count; i++)
                {
                    List<XYZ> polygon = faces[i];
                    if (polygon == null || polygon.Count < 3)
                        continue;

                    preview.FilledPolygons.Add(new PreviewCellPolygon
                    {
                        CellKey = "face:" + i,
                        XIndex = -1,
                        YIndex = -1,
                        IsExcluded = false,
                        IsSelected = false,
                        Points = polygon.Select(p => ModelToCanvas(p, preview.Transform)).ToList()
                    });
                }
            }
            else if (drawFilledPolygons)
            {
                foreach (GridCellResult cell in cells)
                {
                    if (cell == null)
                        continue;

                    foreach (List<XYZ> polygon in cell.GetPreviewPolygons(fillMode))
                    {
                        if (polygon == null || polygon.Count < 3)
                            continue;

                        preview.FilledPolygons.Add(new PreviewCellPolygon
                        {
                            CellKey = cell.CellKey,
                            XIndex = cell.XIndex,
                            YIndex = cell.YIndex,
                            IsExcluded = cell.IsExcluded,
                            IsSelected = selectedKeys.Contains(cell.CellKey),
                            Points = polygon.Select(p => ModelToCanvas(p, preview.Transform)).ToList()
                        });
                    }
                }
            }

            if (drawGridLines)
            {
                foreach ((XYZ A, XYZ B) line in KeyplanGridBuilder.BuildGridLines(outerLoop, xBreaks, yBreaks))
                {
                    List<(XYZ A, XYZ B)> segments =
                        KeyplanPolygonUtils.ClipLineByPolygon(outerLoop, line.A, line.B);

                    foreach ((XYZ A, XYZ B) seg in segments)
                    {
                        Point p0 = ModelToCanvas(seg.A, preview.Transform);
                        Point p1 = ModelToCanvas(seg.B, preview.Transform);
                        preview.GridLines.Add((p0, p1));
                    }
                }
            }

            for (int i = 1; i < xBreaks.Length - 1; i++)
            {
                double modelX = minX + (maxX - minX) * xBreaks[i];
                Point pt = ModelToCanvas(new XYZ(modelX, minY, 0.0), preview.Transform);

                preview.VerticalAxes.Add(new AxisPreviewInfo
                {
                    Orientation = AxisOrientation.Vertical,
                    InteriorIndex = i - 1,
                    Normalized = xBreaks[i],
                    CanvasPosition = pt.X
                });
            }

            for (int i = 1; i < yBreaks.Length - 1; i++)
            {
                double modelY = minY + (maxY - minY) * yBreaks[i];
                Point pt = ModelToCanvas(new XYZ(minX, modelY, 0.0), preview.Transform);

                preview.HorizontalAxes.Add(new AxisPreviewInfo
                {
                    Orientation = AxisOrientation.Horizontal,
                    InteriorIndex = i - 1,
                    Normalized = yBreaks[i],
                    CanvasPosition = pt.Y
                });
            }

            return preview;
        }

        private static void ApplyCellEditStates(
            IEnumerable<GridCellResult> cells,
            IReadOnlyDictionary<string, CellEditState> cellEdits)
        {
            if (cells == null)
                return;

            foreach (GridCellResult cell in cells)
            {
                if (cell == null)
                    continue;

                if (cellEdits != null && cellEdits.TryGetValue(cell.CellKey, out CellEditState edit) && edit != null)
                {
                    cell.IsExcluded = edit.IsExcluded;
                    cell.MergeGroupId = edit.MergeGroupId ?? string.Empty;
                }
                else
                {
                    cell.IsExcluded = false;
                    cell.MergeGroupId = string.Empty;
                }
            }
        }

        public static Point ModelToCanvas(XYZ p, PreviewTransformInfo t)
        {
            double x = t.Padding + (p.X - t.ModelMinX) * t.Scale;
            double y = t.CanvasHeight - t.Padding - (p.Y - t.ModelMinY) * t.Scale;
            return new Point(x, y);
        }
    }
}