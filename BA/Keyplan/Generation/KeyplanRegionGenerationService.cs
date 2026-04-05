using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanRegionGenerationService
    {
        public static GenerationResult Generate(
            Document doc,
            CurveLoop outerLoop,
            KeyplanGridOptions options,
            double[] xBreaks,
            double[] yBreaks,
            IReadOnlyDictionary<string, CellEditState> cellEdits)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (xBreaks == null || xBreaks.Length < 2) throw new ArgumentException("Invalid X breaks.", nameof(xBreaks));
            if (yBreaks == null || yBreaks.Length < 2) throw new ArgumentException("Invalid Y breaks.", nameof(yBreaks));

            GenerationResult result = new GenerationResult();

            ViewDrafting targetView = KeyplanViewService.FindOrCreateDraftingView(doc, options.TargetDraftingViewName);
            result.TargetViewId = targetView.Id;
            result.TargetViewName = targetView.Name;

            FilledRegionType frType = null;
            if (options.CreateFilledRegions)
            {
                frType = KeyplanFilledRegionUtils.FindFilledRegionTypeByName(doc, options.FilledRegionTypeName);
                if (frType == null)
                    throw new InvalidOperationException(
                        $"Filled region type '{options.FilledRegionTypeName}' was not found.");
            }

            using (Transaction tx = new Transaction(doc, "Generate Keyplan Grid"))
            {
                tx.Start();

                if (options.ClearTargetViewFirst)
                {
                    List<ElementId> idsToDelete = new List<ElementId>();

                    // Filled regions
                    idsToDelete.AddRange(
                        new FilteredElementCollector(doc, targetView.Id)
                            .OfClass(typeof(FilledRegion))
                            .WhereElementIsNotElementType()
                            .Select(x => x.Id));

                    // Detail curves (detail lines, arcs, etc.)
                    idsToDelete.AddRange(
                        new FilteredElementCollector(doc, targetView.Id)
                            .OfClass(typeof(CurveElement))
                            .WhereElementIsNotElementType()
                            .Where(x => x is DetailCurve)
                            .Select(x => x.Id));

                    idsToDelete = idsToDelete
                        .Distinct()
                        .Where(id => id != null && id != ElementId.InvalidElementId)
                        .ToList();

                    foreach (ElementId id in idsToDelete)
                    {
                        try
                        {
                            doc.Delete(id);
                            result.DeletedCount++;
                        }
                        catch (Exception ex)
                        {
                            result.Skipped++;
                            result.RegionRejectReasons.Add($"Delete failed for {id.Value}: {ex.Message}");
                        }
                    }
                }

                List<GridCellResult> cells = new List<GridCellResult>();

                if (!options.UseOutlineAsPrimaryFill)
                {
                    cells = KeyplanGridBuilder.BuildCells(
                        outerLoop,
                        xBreaks,
                        yBreaks,
                        options.FillMode,
                        options.MinimumOccupancyRatio) ?? new List<GridCellResult>();

                    ApplyCellEditStates(cells, cellEdits);

                    result.TotalCellsFromBuilder = cells.Count;
                    result.TotalPolygonsFromBuilder = cells.Sum(c => c.GetGenerationPolygons(options.FillMode)?.Count() ?? 0);
                }
                else
                {
                    result.TotalCellsFromBuilder = 0;
                    result.TotalPolygonsFromBuilder = 0;
                }

                if (options.CreateFilledRegions)
                {
                    if (options.UseOutlineAsPrimaryFill)
                    {
                        List<List<XYZ>> faces = KeyplanFaceBuilder.BuildFaces(outerLoop, xBreaks, yBreaks);

                        result.TotalPolygonsFromBuilder = faces.Count;

                        foreach (List<XYZ> face in faces)
                        {
                            TryCreateFilledRegionFromPolygon(doc, targetView, frType, face, result);
                        }
                    }
                    else
                    {
                        foreach (GridCellResult cell in cells)
                        {
                            TryCreateFilledRegionsForCell(doc, targetView, frType, cell, options.FillMode, result);
                        }
                    }
                }

                if (options.DrawGridLines)
                {
                    List<ElementId> createdGridLineIds = new List<ElementId>();

                    foreach ((XYZ A, XYZ B) line in KeyplanGridBuilder.BuildGridLines(outerLoop, xBreaks, yBreaks))
                    {
                        List<(XYZ A, XYZ B)> segments =
                            KeyplanPolygonUtils.ClipLineByPolygon(outerLoop, line.A, line.B);

                        foreach ((XYZ A, XYZ B) seg in segments)
                        {
                            try
                            {
                                if (seg.A == null || seg.B == null || seg.A.DistanceTo(seg.B) < 1e-6)
                                {
                                    result.Skipped++;
                                    result.GridLineRejectReasons.Add("Grid line segment too short.");
                                    continue;
                                }

                                Line lineCurve = Line.CreateBound(seg.A, seg.B);
                                DetailCurve dc = doc.Create.NewDetailCurve(targetView, lineCurve);

                                if (dc != null)
                                {
                                    createdGridLineIds.Add(dc.Id);
                                    result.CreatedGridLines++;
                                }
                                else
                                {
                                    result.Skipped++;
                                    result.GridLineRejectReasons.Add("NewDetailCurve returned null.");
                                }
                            }
                            catch (Exception ex)
                            {
                                result.Skipped++;
                                result.GridLineRejectReasons.Add("Grid line exception: " + ex.Message);
                            }
                        }
                    }

                    if (createdGridLineIds.Count > 0)
                    {
                        try
                        {
                            DetailElementOrderUtils.BringToFront(doc, targetView, createdGridLineIds);
                        }
                        catch
                        {
                        }
                    }
                }

                if (options.DrawOutline)
                {
                    TryCreateOutline(doc, targetView, outerLoop, result);
                }

                if (!string.IsNullOrWhiteSpace(options.TargetViewTemplateName))
                {
                    try
                    {
                        KeyplanViewService.ApplyTemplateIfPossible(doc, targetView, options.TargetViewTemplateName);
                    }
                    catch (Exception ex)
                    {
                        result.Skipped++;
                        result.RegionRejectReasons.Add("Template exception: " + ex.Message);
                    }
                }

                tx.Commit();
            }

            return result;
        }
        private static void TryCreateFilledRegionFromPolygon(
                Document doc,
                ViewDrafting targetView,
                FilledRegionType frType,
                IList<XYZ> polygon,
                GenerationResult result)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (targetView == null) throw new ArgumentNullException(nameof(targetView));
            if (frType == null) throw new ArgumentNullException(nameof(frType));

            try
            {
                if (polygon == null || polygon.Count < 3)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Face fill: polygon null or fewer than 3 points.");
                    return;
                }

                List<XYZ> pts = KeyplanPolygonUtils.CleanPolygonStrict(polygon);
                if (pts == null || pts.Count < 3)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Face fill: cleaned polygon fewer than 3 points.");
                    return;
                }

                string reason;
                if (!KeyplanPolygonUtils.IsValidFilledRegionPolygon(pts, out reason))
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Face fill invalid: " + reason);
                    return;
                }

                if (!KeyplanPolygonUtils.TryCreateCurveLoopFromPolygon(pts, out CurveLoop loop) || loop == null)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Face fill: failed to create CurveLoop.");
                    return;
                }

                if (pts.Count < 4)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Face fill: fewer than 4 points after cleaning (Revit may reject simple triangles).");
                    return;
                }


                FilledRegion.Create(doc, frType.Id, targetView.Id, new List<CurveLoop> { loop });
                result.CreatedFilledRegions++;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.RegionRejectReasons.Add("Face fill exception: " + ex.Message);
            }
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

        private static void TryCreateOutlineFilledRegion(
            Document doc,
            ViewDrafting targetView,
            FilledRegionType frType,
            CurveLoop outerLoop,
            GenerationResult result)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (targetView == null) throw new ArgumentNullException(nameof(targetView));
            if (frType == null) throw new ArgumentNullException(nameof(frType));
            if (outerLoop == null)
            {
                result.Skipped++;
                result.RegionRejectReasons.Add("Primary outline fill: outerLoop is null.");
                return;
            }

            try
            {
                if (TryCreateFlattenedCurveLoopFromOriginal(outerLoop, out CurveLoop flattenedOriginalLoop))
                {
                    try
                    {
                        FilledRegion.Create(doc, frType.Id, targetView.Id, new List<CurveLoop> { flattenedOriginalLoop });
                        result.CreatedFilledRegions++;
                        return;
                    }
                    catch (Exception ex)
                    {
                        result.RegionRejectReasons.Add(
                            "Primary outline fill original-loop attempt failed: " + ex.Message);
                    }
                }
                else
                {
                    result.RegionRejectReasons.Add(
                        "Primary outline fill original-loop attempt failed: failed to flatten and connect original loop.");
                }
            }
            catch (Exception ex)
            {
                result.RegionRejectReasons.Add(
                    "Primary outline fill original-loop attempt exception: " + ex);
            }

            try
            {
                List<XYZ> outlinePolygon = KeyplanPolygonUtils.CurveLoopToPolyline(outerLoop);
                outlinePolygon = KeyplanPolygonUtils.CleanPolygonStrict(outlinePolygon);

                if (outlinePolygon == null || outlinePolygon.Count < 3)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Primary outline fill fallback: insufficient points.");
                    return;
                }

                string reason;
                if (!KeyplanPolygonUtils.IsValidFilledRegionPolygon(outlinePolygon, out reason))
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Primary outline fill invalid: " + reason);
                    return;
                }

                if (!KeyplanPolygonUtils.TryCreateCurveLoopFromPolygon(outlinePolygon, out CurveLoop outlineLoop) || outlineLoop == null)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Primary outline fill fallback: failed to create CurveLoop.");
                    return;
                }

                FilledRegion.Create(doc, frType.Id, targetView.Id, new List<CurveLoop> { outlineLoop });
                result.CreatedFilledRegions++;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.RegionRejectReasons.Add("Primary outline fill fallback exception: " + ex);
            }
        }
        private static bool TryCreateFlattenedCurveLoopFromOriginal(CurveLoop sourceLoop, out CurveLoop flattenedLoop)
        {
            flattenedLoop = null;

            if (sourceLoop == null)
                return false;

            try
            {
                List<Curve> rebuilt = new List<Curve>();

                foreach (Curve curve in sourceLoop)
                {
                    if (curve == null)
                        continue;

                    Curve flattened = FlattenCurveToXY(curve);
                    if (flattened != null)
                    {
                        rebuilt.Add(flattened);
                        continue;
                    }

                    // Tessellated fallback for unsupported curve types.
                    IList<XYZ> tess = curve.Tessellate();
                    if (tess == null || tess.Count < 2)
                        return false;

                    List<XYZ> flatPts = tess.Select(KeyplanPolygonUtils.FlattenPoint).ToList();
                    flatPts = KeyplanPolygonUtils.RemoveSequentialDuplicates(flatPts);

                    if (flatPts.Count < 2)
                        return false;

                    for (int i = 0; i < flatPts.Count - 1; i++)
                    {
                        XYZ a = flatPts[i];
                        XYZ b = flatPts[i + 1];

                        if (a.DistanceTo(b) < 1e-7)
                            continue;

                        rebuilt.Add(Line.CreateBound(a, b));
                    }
                }

                if (rebuilt.Count < 3)
                    return false;

                List<Curve> connected = ConnectCurveSequence(rebuilt);
                if (connected == null || connected.Count < 3)
                    return false;
                if (connected.Any(c => c == null))
                    return false;
                flattenedLoop = CurveLoop.Create(connected);
                return true;
            }
            catch
            {
                flattenedLoop = null;
                return false;
            }
        }

        private static Curve FlattenCurveToXY(Curve curve)
        {
            if (curve == null)
                return null;

            XYZ p0 = KeyplanPolygonUtils.FlattenPoint(curve.GetEndPoint(0));
            XYZ p1 = KeyplanPolygonUtils.FlattenPoint(curve.GetEndPoint(1));

            if (p0.DistanceTo(p1) < 1e-9)
                return null;

            if (curve is Line)
                return Line.CreateBound(p0, p1);

            if (curve is Arc arc)
            {
                XYZ mid = KeyplanPolygonUtils.FlattenPoint(arc.Evaluate(0.5, true));

                if (!KeyplanPolygonUtils.AreCollinear2D(p0, mid, p1))
                    return Arc.Create(p0, p1, mid);

                return Line.CreateBound(p0, p1);
            }

            // Fallback for spline/NURBS/etc.:
            // collapse to a line only if nearly straight, otherwise signal caller to tessellate explicitly.
            IList<XYZ> tess = curve.Tessellate();
            if (tess == null || tess.Count < 2)
                return null;

            List<XYZ> flat = tess.Select(KeyplanPolygonUtils.FlattenPoint).ToList();
            flat = KeyplanPolygonUtils.CleanPolygon(flat);

            if (flat.Count < 2)
                return null;

            bool nearlyStraight = true;
            XYZ a = flat.First();
            XYZ b = flat.Last();

            for (int i = 1; i < flat.Count - 1; i++)
            {
                if (!KeyplanPolygonUtils.AreCollinear2D(a, flat[i], b))
                {
                    nearlyStraight = false;
                    break;
                }
            }

            if (nearlyStraight)
                return Line.CreateBound(a, b);

            return null;
        }


        private static void TryCreateFilledRegionsForCell(
            Document doc,
            ViewDrafting targetView,
            FilledRegionType frType,
            GridCellResult cell,
            KeyplanCellFillMode fillMode,
            GenerationResult result)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (targetView == null) throw new ArgumentNullException(nameof(targetView));
            if (frType == null) throw new ArgumentNullException(nameof(frType));
            if (cell == null)
            {
                result.Skipped++;
                result.RegionRejectReasons.Add("Cell is null.");
                return;
            }

            if (cell.IsExcluded)
                return;

            IEnumerable<List<XYZ>> polygons = cell.GetGenerationPolygons(fillMode) ?? Enumerable.Empty<List<XYZ>>();
            int loopIndex = 0;

            foreach (List<XYZ> polygon in polygons)
            {
                try
                {
                    if (polygon == null || polygon.Count < 3)
                    {
                        result.Skipped++;
                        result.RegionRejectReasons.Add(
                            $"Cell X={cell.XIndex} Y={cell.YIndex} Loop={loopIndex}: polygon null or fewer than 3 points.");
                        loopIndex++;
                        continue;
                    }

                    List<XYZ> pts = KeyplanPolygonUtils.CleanPolygonStrict(polygon);
                    pts = KeyplanPolygonUtils.EnsureCounterClockwise(pts);
                    if (pts == null || pts.Count < 3)
                    {
                        result.Skipped++;
                        result.RegionRejectReasons.Add(
                            $"Cell X={cell.XIndex} Y={cell.YIndex} Loop={loopIndex}: cleaned polygon fewer than 3 points.");
                        loopIndex++;
                        continue;
                    }

                    string reason;
                    if (!KeyplanPolygonUtils.IsValidFilledRegionPolygon(pts, out reason))
                    {
                        result.Skipped++;
                        result.RegionRejectReasons.Add(
                            $"Cell X={cell.XIndex} Y={cell.YIndex} Loop={loopIndex}: invalid polygon. {reason}");
                        loopIndex++;
                        continue;
                    }

                    if (!KeyplanPolygonUtils.TryCreateCurveLoopFromPolygon(pts, out CurveLoop loop) || loop == null)
                    {
                        result.Skipped++;
                        result.RegionRejectReasons.Add(
                            $"Cell X={cell.XIndex} Y={cell.YIndex} Loop={loopIndex}: failed to create CurveLoop.");
                        loopIndex++;
                        continue;
                    }

                    FilledRegion.Create(doc, frType.Id, targetView.Id, new List<CurveLoop> { loop });
                    result.CreatedFilledRegions++;
                }
                catch (Exception ex)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add(
                        $"Cell X={cell.XIndex} Y={cell.YIndex} Loop={loopIndex}: {ex.Message}");
                }

                loopIndex++;
            }
        }

        private static void TryCreateOutline(
            Document doc,
            ViewDrafting targetView,
            CurveLoop outerLoop,
            GenerationResult result)
        {
            try
            {
                List<XYZ> outlinePolygon = KeyplanPolygonUtils.CurveLoopToPolyline(outerLoop);
                outlinePolygon = KeyplanPolygonUtils.EnsureCounterClockwise(outlinePolygon);

                if (!KeyplanPolygonUtils.TryCreateCurveLoopFromPolygon(outlinePolygon, out CurveLoop cleanOutline) ||
                    cleanOutline == null)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Outline: failed to create clean CurveLoop.");
                    return;
                }

                foreach (Curve c in cleanOutline)
                {
                    try
                    {
                        doc.Create.NewDetailCurve(targetView, c);
                        result.CreatedOutlineCurves++;
                    }
                    catch (Exception ex)
                    {
                        result.Skipped++;
                        result.RegionRejectReasons.Add("Outline curve exception: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.RegionRejectReasons.Add("Outline exception: " + ex.Message);
            }
        }
        private static List<Curve> ConnectCurveSequence(IList<Curve> curves)
        {
            if (curves == null || curves.Count == 0)
                return null;

            const double tol = 1e-4;

            List<Curve> result = new List<Curve>();

            Curve first = curves[0];
            if (first == null)
                return null;

            result.Add(first);
            XYZ currentEnd = KeyplanPolygonUtils.FlattenPoint(first.GetEndPoint(1));
            if (result.Any(c => c == null))
                return null;
            for (int i = 1; i < curves.Count; i++)
            {
                Curve c = curves[i];
                if (c == null)
                    return null;

                XYZ s = KeyplanPolygonUtils.FlattenPoint(c.GetEndPoint(0));
                XYZ e = KeyplanPolygonUtils.FlattenPoint(c.GetEndPoint(1));

                Curve accepted = null;

                if (currentEnd.DistanceTo(s) <= tol)
                {
                    accepted = RebuildCurveWithStartSnap(c, currentEnd);
                }
                else if (currentEnd.DistanceTo(e) <= tol)
                {
                    accepted = RebuildCurveWithStartSnap(c.CreateReversed(), currentEnd);
                }
                else
                {
                    return null;
                }

                if (accepted == null)
                    return null;

                result.Add(accepted);
                currentEnd = KeyplanPolygonUtils.FlattenPoint(accepted.GetEndPoint(1));
            }

            XYZ firstStart = KeyplanPolygonUtils.FlattenPoint(result[0].GetEndPoint(0));
            XYZ finalEnd = KeyplanPolygonUtils.FlattenPoint(result[result.Count - 1].GetEndPoint(1));

            if (finalEnd.DistanceTo(firstStart) > tol)
                return null;

            // Heal final closure by snapping the last curve endpoint to the first start.
            Curve last = result[result.Count - 1];
            Curve healedLast = RebuildCurveWithEndSnap(last, firstStart);
            if (healedLast == null)
                return null;

            result[result.Count - 1] = healedLast;
            return result;
        }
        private static Curve RebuildCurveWithStartSnap(Curve curve, XYZ snappedStart)
        {
            if (curve == null || snappedStart == null)
                return null;

            XYZ end = KeyplanPolygonUtils.FlattenPoint(curve.GetEndPoint(1));

            if (snappedStart.DistanceTo(end) < 1e-7)
                return null;

            return Line.CreateBound(snappedStart, end);
        }

        private static Curve RebuildCurveWithEndSnap(Curve curve, XYZ snappedEnd)
        {
            if (curve == null || snappedEnd == null)
                return null;

            XYZ start = KeyplanPolygonUtils.FlattenPoint(curve.GetEndPoint(0));

            if (start.DistanceTo(snappedEnd) < 1e-7)
                return null;

            return Line.CreateBound(start, snappedEnd);
        }

    }

    public sealed class GenerationResult
    {
        public ElementId TargetViewId { get; set; } = ElementId.InvalidElementId;
        public string TargetViewName { get; set; } = string.Empty;

        public int DeletedCount { get; set; }
        public int TotalCellsFromBuilder { get; set; }
        public int TotalPolygonsFromBuilder { get; set; }
        public int CreatedFilledRegions { get; set; }
        public int CreatedGridLines { get; set; }
        public int CreatedOutlineCurves { get; set; }
        public int Skipped { get; set; }

        public List<string> RegionRejectReasons { get; } = new List<string>();
        public List<string> GridLineRejectReasons { get; } = new List<string>();
    }
}