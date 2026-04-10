using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Core.Models;
using RevitLevel = Autodesk.Revit.DB.Level;
using RevitColor = Autodesk.Revit.DB.Color;
using ClipperFillRule = Clipper2Lib.FillRule;
using ClipperClipType = Clipper2Lib.ClipType;

namespace BA.Services.Visualization
{
    /// <summary>
    /// Vykresluje výsledky výpočtu jako FilledRegion elementy v aktivním pohledu.
    ///
    /// Barevné kódování dle AreaType:
    ///   PodlahovaPlochaNV366 → zelená (0, 180, 60)
    ///   HPPNadzemni          → modrá (30, 120, 255)
    ///   HPPPodzemni          → tmavě modrá (0, 60, 180)
    ///   PodlahovaPlochaSZ    → tyrkysová (0, 180, 180)
    ///   ZastavenaPlochaSZ    → oranžová (255, 140, 0)
    ///
    /// Výškové zóny NV 366/2013 (pouze pro PodlahovaPlochaNV366):
    ///   FullZone  → plná barva
    ///   HalfZone  → šrafura (50% transparency)
    ///   ZeroZone  → červená (pro debug)
    ///
    /// Všechny vytvořené FilledRegion elementy mají komentář s prefixem "CZA|"
    /// pro jednoznačnou identifikaci při mazání.
    ///
    /// DŮLEŽITÉ: Volat výhradně z main Revit threadu (ExternalEventHandler).
    /// Transakce musí být otevřena volajícím.
    /// </summary>
    public sealed class VisualizationEngine : IVisualizationEngine
    {
        private const string CzaCommentPrefix = "CZA|";

        private static readonly IReadOnlyDictionary<AreaType, Color> AreaTypeColors =
            new Dictionary<AreaType, Color>
            {
                { AreaType.PodlahovaPlochaNV366, new RevitColor(0,   180, 60) },
                { AreaType.HPPNadzemni,          new RevitColor(30,  120, 255) },
                { AreaType.HPPPodzemni,          new RevitColor(0,   60,  180) },
                { AreaType.PodlahovaPlochaSZ,    new RevitColor(0,   180, 180) },
                { AreaType.ZastavenaPlochaSZ,    new RevitColor(255, 140, 0)   }
            };

        public void VisualizeResults(
            IReadOnlyList<AreaComputationResult> results,
            View activeView,
            Document document)
        {
            if (activeView.ViewType != ViewType.FloorPlan &&
                activeView.ViewType != ViewType.CeilingPlan &&
                activeView.ViewType != ViewType.AreaPlan)
            {
                // FilledRegion je podporován pouze v plan views
                return;
            }

            foreach (var result in results)
            {
                if (result.Status != ComputationStatus.Success)
                    continue;

                if (!result.ComputedBoundary.Any())
                    continue;

                var color = AreaTypeColors.TryGetValue(result.AreaType, out var c)
                    ? c : new Color(128, 128, 128);

                var regionTypeName = $"CZA_{result.AreaType}";
                var regionType = GetOrCreateFilledRegionType(
                    regionTypeName, color, document);

                var projectedLoops = ProjectLoopsToViewElevation(
                    result.ComputedBoundary, activeView);

                if (!projectedLoops.Any())
                    continue;

                try
                {
                    var region = FilledRegion.Create(
                        document,
                        regionType.Id,
                        activeView.Id,
                        projectedLoops.ToList());

                    // Komentář pro identifikaci při mazání
                    var commentParam = region.get_Parameter(
                        
                        Parameter.ALL_MODEL_INSTANCE_COMMENTS);

                    commentParam?.Set(
                        $"{CzaCommentPrefix}{result.AreaType}|" +
                        $"{result.ComputedAreaM2:F3}|" +
                        $"{result.SourceElementId.Value}");
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Nevalidní loops pro FilledRegion — přeskočíme
                }
            }
        }

        public void ClearVisualizations(AreaType? filterByType, Document document)
        {
            // Sbíráme všechny FilledRegion s CZA| prefixem
            var toDelete = new FilteredElementCollector(document)
                .OfClass(typeof(FilledRegion))
                .Cast<FilledRegion>()
                .Where(r =>
                {
                    var comment = r.get_Parameter(
                        BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();

                    if (string.IsNullOrEmpty(comment) ||
                        !comment.StartsWith(CzaCommentPrefix, StringComparison.Ordinal))
                        return false;

                    if (filterByType is null)
                        return true;

                    // Parsujeme AreaType z komentáře: "CZA|HPPNadzemni|..."
                    var parts = comment.Split('|');
                    return parts.Length >= 2 &&
                           Enum.TryParse<AreaType>(parts[1], out var parsedType) &&
                           parsedType == filterByType;
                })
                .Select(r => r.Id)
                .ToList();

            foreach (var id in toDelete)
            {
                try { document.Delete(id); }
                catch { /* Element mohl být smazán jinak */ }
            }
        }

        // --------------------------------------------------------
        // Private helpers
        // --------------------------------------------------------

        private static FilledRegionType GetOrCreateFilledRegionType(
            string name,
            RevitColor color,
            Document document)
        {
            // Hledáme existující CZA type
            var existing = new FilteredElementCollector(document)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(t => t.Name == name);

            if (existing is not null)
                return existing;

            // Duplikujeme první dostupný type jako základ
            var sourceType = new FilteredElementCollector(document)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .First();

            var newType = sourceType.Duplicate(name) as FilledRegionType
                ?? throw new InvalidOperationException(
                    $"Nepodařilo se duplikovat FilledRegionType '{name}'.");

            // Revit 2026: barva se nastavuje přes ForegroundPatternColor


 

            return newType;
        }

        private static IReadOnlyList<CurveLoop> ProjectLoopsToViewElevation(
            IReadOnlyList<CurveLoop> loops,
            View view)
        {
            double viewElevationFt = 0.0;

            if (view is ViewPlan viewPlan)
                viewElevationFt = viewPlan.GenLevel?.Elevation ?? 0.0;

            var projected = new List<CurveLoop>();

            foreach (var loop in loops)
            {
                var projectedCurves = new List<Curve>();
                bool loopValid = true;

                foreach (Curve curve in loop)
                {
                    var p0 = curve.GetEndPoint(0);
                    var p1 = curve.GetEndPoint(1);

                    var proj0 = new XYZ(p0.X, p0.Y, viewElevationFt);
                    var proj1 = new XYZ(p1.X, p1.Y, viewElevationFt);

                    if (proj0.DistanceTo(proj1) < 1.0 / 304.8) // < 1mm
                    {
                        loopValid = false;
                        break;
                    }

                    try
                    {
                        projectedCurves.Add(Line.CreateBound(proj0, proj1));
                    }
                    catch
                    {
                        loopValid = false;
                        break;
                    }
                }

                if (!loopValid || projectedCurves.Count < 3)
                    continue;

                try
                {
                    projected.Add(CurveLoop.Create(projectedCurves));
                }
                catch
                {
                    // Nevalidní loop po projekci — přeskočíme
                }
            }

            return projected;
        }
    }
}