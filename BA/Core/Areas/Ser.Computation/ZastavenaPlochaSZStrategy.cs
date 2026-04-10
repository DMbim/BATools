using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Core.Models;
using BA.Services.Geometry;

namespace BA.Services.Computation
{
    /// <summary>
    /// Výpočet zastavěné plochy dle SZ č. 283/2021 Sb. §13 písm. o).
    ///
    /// SZ §13 o): Zastavěná plocha = plocha průmětu vnějšího obvodu nadzemní části
    /// stavby do vodorovné roviny.
    ///
    /// Implementace:
    /// • Sběr všech obvodových stěn v ±200 mm od projektové nuly (= terén)
    /// • Projekce outer shell na Z=0
    /// • Přesahy > 0 se zahrnují (§13 o) říká "vnější obvod nadzemní části")
    /// • Výsledek = jeden polygon = celková zastavěná plocha budovy
    ///
    /// Pozn.: Tato definice je totožná s tím, co MPP používá pro IZP výpočty.
    /// </summary>
    public sealed class ZastavenaPlochaSZStrategy : StrategyBase, IAreaComputationStrategy
    {
        private readonly IGeometryEngine _geometryEngine;

        public AreaType SupportedAreaType => AreaType.ZastavenaPlochaSZ;

        public ZastavenaPlochaSZStrategy(
            IGeometryEngine geometryEngine,
            INormInfoProvider normInfoProvider)
            : base(normInfoProvider)
        {
            _geometryEngine = geometryEngine
                ?? throw new ArgumentNullException(nameof(geometryEngine));
        }

        public async Task<IReadOnlyList<AreaComputationResult>> ComputeAsync(
            IReadOnlyList<ElementId> targetElementIds,
            Document document,
            ProjectContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Zastavěná plocha je výsledek pro celou budovu — nezávislý na výběru
            // targetElementIds je zde ignorován (výpočet vždy pro celý model)
            var groundElements = CollectGroundFloorElements(document, context);

            if (!groundElements.Any())
            {
                return new[]
                {
                    new AreaComputationResult
                    {
                        SourceElementId   = ElementId.InvalidElementId,
                        SourceElementName = "Budova",
                        AreaType          = AreaType.ZastavenaPlochaSZ,
                        ComputedAreaM2    = 0.0,
                        Status            = ComputationStatus.SkippedInsufficientGeometry,
                        Audit             = BuildAudit(AreaType.ZastavenaPlochaSZ,
                                               "Skipped", 0,
                                               "Nebyly nalezeny obvodové prvky u terénu."),
                        Deductions        = Array.Empty<AreaDeduction>(),
                        ComputedBoundary  = Array.Empty<CurveLoop>(),
                        ErrorMessage      = "Nebyly nalezeny obvodové prvky u terénu."
                    }
                };
            }

            var footprintLoops = await _geometryEngine.BuildBuildingFootprintAsync(
                groundElements,
                overhangThresholdM: 0.0, // SZ §13 o) — celý vnější obvod
                document);

            double totalAreaM2 = footprintLoops.Sum(l => _geometryEngine.ComputeLoopAreaM2(l));

            return new[]
            {
                new AreaComputationResult
                {
                    SourceElementId   = ElementId.InvalidElementId,
                    SourceElementName = "Budova — zastavěná plocha",
                    AreaType          = AreaType.ZastavenaPlochaSZ,
                    ComputedAreaM2    = Math.Round(totalAreaM2, 4),
                    Status            = ComputationStatus.Success,
                    Audit             = BuildAudit(
                        AreaType.ZastavenaPlochaSZ,
                        "BuildingFootprintProjection_GroundLevel_SZ",
                        groundElements.Count,
                        $"Průměrný UT: {context.AverageTerenElevationMm:F0} mm"),
                    Deductions        = Array.Empty<AreaDeduction>(),
                    ComputedBoundary  = footprintLoops
                }
            };
        }

        private static List<Element> CollectGroundFloorElements(
            Document document,
            ProjectContext context)
        {
            // Hledáme stěny v ±500 mm od průměrného UT
            double terenFt = UnitUtils.ConvertToInternalUnits(
                context.AverageTerenElevationMm / 1000.0, UnitTypeId.Meters);

            double searchRangeFt = 500.0 / 304.8;

            var bbFilter = new BoundingBoxIntersectsFilter(
                new Outline(
                    new XYZ(-1e6, -1e6, terenFt - searchRangeFt),
                    new XYZ(1e6, 1e6, terenFt + searchRangeFt)));

            var walls = new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .WherePasses(bbFilter)
                .Cast<Wall>()
                .Where(w => w.WallType.Kind == WallKind.Basic)
                .Cast<Element>()
                .ToList();

            return walls;
        }
    }
}
