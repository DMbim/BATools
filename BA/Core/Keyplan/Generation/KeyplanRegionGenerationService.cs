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
            IReadOnlyCollection<KeyplanSplitLineItem> verticalSplits,
            IReadOnlyCollection<KeyplanSplitLineItem> horizontalSplits,
            IReadOnlyDictionary<string, CellEditState> cellEdits)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));
            if (options == null) throw new ArgumentNullException(nameof(options));

            // 🔹 Build graphic model using splits (NOT break arrays)
            KeyplanGraphicModel graphicModel = KeyplanGraphicBuilder.Build(
                outerLoop,
                options,
                verticalSplits,
                horizontalSplits,
                cellEdits);

            // 🔹 Then reuse your existing pipeline
            return GenerateFromGraphicModel(doc, options, graphicModel);
        }

        // -------------------------------------------------------------------------
        // Core generation
        // -------------------------------------------------------------------------

        private static GenerationResult GenerateFromGraphicModel(
            Document doc,
            KeyplanGridOptions options,
            KeyplanGraphicModel graphicModel)
        {
            GenerationResult result = new GenerationResult();

            FilledRegionType frType = null;
            if (options.CreateFilledRegions)
            {
                frType = KeyplanFilledRegionUtils.FindFilledRegionTypeByName(
                    doc, options.FilledRegionTypeName);

                if (frType == null)
                    throw new InvalidOperationException(
                        $"Filled region type '{options.FilledRegionTypeName}' was not found.");
            }

            // Scale the graphic model before generation.
            graphicModel = KeyplanGraphicScaleService.ScaleModel(graphicModel, options.GlobalScaleFactor);

            result.TotalPolygonsFromBuilder = graphicModel.FilledRegions.Count;
            result.TotalGridSegmentsFromBuilder = graphicModel.GridLines.Count;
            result.TotalOutlineSegmentsFromBuilder = graphicModel.OutlineLines.Count;

            using (Transaction tx = new Transaction(doc, "Generate Keyplan Grid"))
            {
                tx.Start();

                ViewDrafting targetView =
                    KeyplanViewService.FindOrCreateDraftingView(doc, options.TargetDraftingViewName);
                result.TargetViewId = targetView.Id;
                result.TargetViewName = targetView.Name;

                // ---- Optional clear ------------------------------------------------
                if (options.ClearTargetViewFirst)
                {
                    List<ElementId> idsToDelete = new FilteredElementCollector(doc, targetView.Id)
                        .WhereElementIsNotElementType()
                        .WherePasses(new LogicalOrFilter(
                            new ElementClassFilter(typeof(FilledRegion)),
                            new ElementClassFilter(typeof(CurveElement))))
                        .Select(x => x.Id)
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
                            result.RegionRejectReasons.Add(
                                $"Delete failed for {id.Value}: {ex.Message}");
                        }
                    }
                }

                // ---- Filled regions -----------------------------------------------
                foreach (KeyplanPolygonGraphicItem fill in graphicModel.FilledRegions)
                {
                    try
                    {
                        // Compute centroid in PRE-SCALE model space so zone label
                        // direction vectors are consistent regardless of scale factor.
                        XYZ centroid = KeyplanScaleUtils.ComputeCentroid(fill.Polygon);

                        FilledRegion region = TryCreateFilledRegionFromPolygon(
                            doc, targetView, frType, fill.Polygon, result);

                        if (region != null)
                        {
                            result.CreatedItems.Add(new GeneratedElementRecord
                            {
                                StableKey = fill.StableKey,
                                Role = "FilledRegion",
                                ElementId = region.Id,
                                UniqueId = region.UniqueId ?? string.Empty,
                                Centroid = centroid
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Skipped++;
                        result.RegionRejectReasons.Add(
                            $"Filled region {fill.StableKey}: {ex.Message}");
                    }
                }

                // ---- Grid lines ---------------------------------------------------
                if (options.DrawGridLines)
                {
                    List<ElementId> createdGridLineIds = new List<ElementId>();

                    foreach (KeyplanLineGraphicItem line in graphicModel.GridLines)
                    {
                        try
                        {
                            if (line.A == null || line.B == null ||
                                line.A.DistanceTo(line.B) < KeyplanGeometryTolerance.MinModelSegment)
                            {
                                result.Skipped++;
                                result.GridLineRejectReasons.Add(
                                    $"Grid line {line.StableKey}: segment too short.");
                                continue;
                            }

                            DetailCurve dc = doc.Create.NewDetailCurve(
                                targetView, Line.CreateBound(line.A, line.B));

                            if (dc != null)
                            {
                                createdGridLineIds.Add(dc.Id);
                                result.CreatedGridLines++;
                                result.CreatedItems.Add(new GeneratedElementRecord
                                {
                                    StableKey = line.StableKey,
                                    Role = "GridLine",
                                    ElementId = dc.Id,
                                    UniqueId = dc.UniqueId ?? string.Empty
                                });
                            }
                            else
                            {
                                result.Skipped++;
                                result.GridLineRejectReasons.Add(
                                    $"Grid line {line.StableKey}: NewDetailCurve returned null.");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Skipped++;
                            result.GridLineRejectReasons.Add(
                                $"Grid line {line.StableKey}: {ex.Message}");
                        }
                    }

                    if (createdGridLineIds.Count > 0)
                    {
                        try
                        {
                            DetailElementOrderUtils.BringToFront(
                                doc, targetView, createdGridLineIds);
                        }
                        catch
                        {
                        }
                    }
                }

                // ---- Outline curves ----------------------------------------------
                if (options.DrawOutline)
                {
                    foreach (KeyplanLineGraphicItem line in graphicModel.OutlineLines)
                    {
                        try
                        {
                            DetailCurve dc = doc.Create.NewDetailCurve(
                                targetView, Line.CreateBound(line.A, line.B));

                            if (dc != null)
                            {
                                result.CreatedOutlineCurves++;
                                result.CreatedItems.Add(new GeneratedElementRecord
                                {
                                    StableKey = line.StableKey,
                                    Role = "OutlineLine",
                                    ElementId = dc.Id,
                                    UniqueId = dc.UniqueId ?? string.Empty
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Skipped++;
                            result.RegionRejectReasons.Add(
                                $"Outline {line.StableKey}: {ex.Message}");
                        }
                    }
                }

  

                tx.Commit();
            }

            result.TotalCellsFromBuilder = result.CreatedItems
                .Count(r => r.Role == "FilledRegion");

            return result;
        }

        // -------------------------------------------------------------------------
        // Polygon → FilledRegion
        // -------------------------------------------------------------------------

        private static FilledRegion TryCreateFilledRegionFromPolygon(
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
                    result.RegionRejectReasons.Add(
                        "Face fill: polygon null or fewer than 3 points.");
                    return null;
                }

                List<XYZ> pts = KeyplanPolygonUtils.CleanPolygonStrict(polygon);
                if (pts == null || pts.Count < 3)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add(
                        "Face fill: cleaned polygon fewer than 3 points.");
                    return null;
                }

                string reason;
                if (!KeyplanPolygonUtils.IsValidFilledRegionPolygon(pts, out reason))
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add("Face fill invalid: " + reason);
                    return null;
                }

                if (!KeyplanPolygonUtils.TryCreateCurveLoopFromPolygon(pts, out CurveLoop loop) ||
                    loop == null)
                {
                    result.Skipped++;
                    result.RegionRejectReasons.Add(
                        "Face fill: failed to create CurveLoop.");
                    return null;
                }

                FilledRegion region = FilledRegion.Create(
                    doc, frType.Id, targetView.Id, new List<CurveLoop> { loop });

                result.CreatedFilledRegions++;
                return region;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.RegionRejectReasons.Add("Face fill exception: " + ex.Message);
                return null;
            }
        }
    }

    // -------------------------------------------------------------------------
    // GenerationResult
    // -------------------------------------------------------------------------

    public sealed class GenerationResult
    {
        public ElementId TargetViewId { get; set; } = ElementId.InvalidElementId;
        public string TargetViewName { get; set; } = string.Empty;

        public int DeletedCount { get; set; }
        public int TotalCellsFromBuilder { get; set; }
        public int TotalPolygonsFromBuilder { get; set; }
        public int TotalGridSegmentsFromBuilder { get; set; }
        public int TotalOutlineSegmentsFromBuilder { get; set; }

        public int CreatedFilledRegions { get; set; }
        public int CreatedGridLines { get; set; }
        public int CreatedOutlineCurves { get; set; }
        public int Skipped { get; set; }

        public List<GeneratedElementRecord> CreatedItems { get; } = new List<GeneratedElementRecord>();
        public List<string> RegionRejectReasons { get; } = new List<string>();
        public List<string> GridLineRejectReasons { get; } = new List<string>();
    }
}
