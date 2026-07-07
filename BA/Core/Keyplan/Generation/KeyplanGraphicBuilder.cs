using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanGraphicBuilder
    {
        public static KeyplanGraphicModel Build(
            CurveLoop outerLoop,
            KeyplanGridOptions options,
            double[] xBreaks,
            double[] yBreaks,
            IReadOnlyDictionary<string, CellEditState> cellEdits)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (xBreaks == null || xBreaks.Length < 2) throw new ArgumentException("Invalid X breaks.", nameof(xBreaks));
            if (yBreaks == null || yBreaks.Length < 2) throw new ArgumentException("Invalid Y breaks.", nameof(yBreaks));

            return BuildInternal(
                outerLoop,
                options,
                () => KeyplanFaceBuilder.BuildFaces(outerLoop, xBreaks, yBreaks),
                () => KeyplanGridBuilder.BuildCells(outerLoop, xBreaks, yBreaks, options.FillMode, options.MinimumOccupancyRatio),
                () => KeyplanGridBuilder.BuildGridLines(outerLoop, xBreaks, yBreaks),
                cellEdits);
        }

        public static KeyplanGraphicModel Build(
            CurveLoop outerLoop,
            KeyplanGridOptions options,
            IReadOnlyCollection<KeyplanSplitLineItem> verticalSplits,
            IReadOnlyCollection<KeyplanSplitLineItem> horizontalSplits,
            IReadOnlyDictionary<string, CellEditState> cellEdits)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));
            if (options == null) throw new ArgumentNullException(nameof(options));

            return BuildInternal(
                outerLoop,
                options,
                () => KeyplanFaceBuilder.BuildFaces(outerLoop, verticalSplits, horizontalSplits),
                () => KeyplanGridBuilder.BuildCells(outerLoop, verticalSplits, horizontalSplits, options.FillMode, options.MinimumOccupancyRatio),
                () => KeyplanGridBuilder.BuildGridLines(outerLoop, verticalSplits, horizontalSplits),
                cellEdits);
        }

        private static KeyplanGraphicModel BuildInternal(
            CurveLoop outerLoop,
            KeyplanGridOptions options,
            Func<List<List<XYZ>>> buildFaces,
            Func<List<GridCellResult>> buildCells,
            Func<List<(XYZ A, XYZ B)>> buildGridLines,
            IReadOnlyDictionary<string, CellEditState> cellEdits)
        {
            KeyplanGraphicModel model = new KeyplanGraphicModel();

            List<XYZ> outline = KeyplanPolygonUtils.CurveLoopToPolyline(outerLoop);
            outline = KeyplanPolygonUtils.CleanPolygonStrict(outline);

            if (outline == null || outline.Count < 3)
                return model;

            if (options.CreateFilledRegions)
            {
                if (options.UseOutlineAsPrimaryFill)
                {
                    List<List<XYZ>> faces = buildFaces();

                    foreach (List<XYZ> face in faces
                        .Select(KeyplanPolygonUtils.CleanPolygonStrict)
                        .Where(x => x != null && x.Count >= 3)
                        .OrderBy(KeyplanGeometryKeyService.MakePolygonKey, StringComparer.Ordinal))
                    {
                        model.FilledRegions.Add(new KeyplanPolygonGraphicItem
                        {
                            StableKey = "fill:" + KeyplanGeometryKeyService.MakePolygonKey(face),
                            Polygon = face
                        });
                    }
                }
                else
                {
                    List<GridCellResult> cells = buildCells() ?? new List<GridCellResult>();

                    ApplyCellEditStates(cells, cellEdits);

                    List<List<XYZ>> polygons = new List<List<XYZ>>();

                    foreach (GridCellResult cell in cells.OrderBy(c => c.XIndex).ThenBy(c => c.YIndex))
                    {
                        foreach (List<XYZ> polygon in cell.GetGenerationPolygons(options.FillMode))
                        {
                            List<XYZ> cleaned = KeyplanPolygonUtils.CleanPolygonStrict(polygon);
                            if (cleaned != null && cleaned.Count >= 3)
                                polygons.Add(cleaned);
                        }
                    }

                    foreach (List<XYZ> polygon in polygons.OrderBy(KeyplanGeometryKeyService.MakePolygonKey, StringComparer.Ordinal))
                    {
                        model.FilledRegions.Add(new KeyplanPolygonGraphicItem
                        {
                            StableKey = "fill:" + KeyplanGeometryKeyService.MakePolygonKey(polygon),
                            Polygon = polygon
                        });
                    }
                }
            }

            if (options.DrawGridLines)
            {
                List<KeyplanLineGraphicItem> gridItems = new List<KeyplanLineGraphicItem>();

                foreach ((XYZ A, XYZ B) line in buildGridLines())
                {
                    List<(XYZ A, XYZ B)> segments = KeyplanPolygonUtils.ClipLineByPolygon(outerLoop, line.A, line.B);

                    foreach ((XYZ A, XYZ B) seg in segments)
                    {
                        XYZ a = KeyplanPolygonUtils.FlattenPoint(seg.A);
                        XYZ b = KeyplanPolygonUtils.FlattenPoint(seg.B);

                        if (a == null || b == null || a.DistanceTo(b) < KeyplanGeometryTolerance.MinModelSegment)
                            continue;

                        gridItems.Add(new KeyplanLineGraphicItem
                        {
                            StableKey = "grid:" + KeyplanGeometryKeyService.MakeUndirectedLineKey(a, b),
                            A = a,
                            B = b
                        });
                    }
                }

                foreach (KeyplanLineGraphicItem item in gridItems
                    .GroupBy(x => x.StableKey, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderBy(x => x.StableKey, StringComparer.Ordinal))
                {
                    model.GridLines.Add(item);
                }
            }

            if (options.DrawOutline)
            {
                // Preserve true boundary order.
                for (int i = 0; i < outline.Count; i++)
                {
                    XYZ a = KeyplanPolygonUtils.FlattenPoint(outline[i]);
                    XYZ b = KeyplanPolygonUtils.FlattenPoint(outline[(i + 1) % outline.Count]);

                    if (a.DistanceTo(b) < KeyplanGeometryTolerance.MinModelSegment)
                        continue;

                    model.OutlineLines.Add(new KeyplanLineGraphicItem
                    {
                        // zero-padded to remain lexicographically sortable if ever sorted later
                        StableKey = $"outline:{i:D6}",
                        A = a,
                        B = b
                    });
                }
            }

            return model;
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

                if (cellEdits != null &&
                    cellEdits.TryGetValue(cell.CellKey, out CellEditState edit) &&
                    edit != null)
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
    }
}