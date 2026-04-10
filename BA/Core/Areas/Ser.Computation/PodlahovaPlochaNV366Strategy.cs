using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Core.Models;
using BA.Services.Geometry;
using Autodesk.Revit.DB.Architecture;  // Room je zde


namespace BA.Services.Computation
{
    /// <summary>
    /// Výpočet podlahové plochy dle NV č. 366/2013 Sb. (ve znění NV č. 432/2022 Sb.)
    ///
    /// Pravidla:
    /// • Měření k vnitřnímu líci svislých konstrukcí na úrovni podlahy
    ///   → SpatialElementBoundaryLocation.Finish
    /// • Výškové zóny §4 odst. 2:
    ///   >= 2000 mm → koef. 1.0
    ///   1200–1999 mm → koef. 0.5
    ///   < 1200 mm → koef. 0.0
    /// • Odečet svislých nosných konstrukcí uvnitř místnosti §4 odst. 3
    /// • Koeficienty prostoru §5: lodžie 1.0, balkon 0.5, terasa 0.25
    /// • Otvory (dveře, okna) v obvodových stěnách se nezapočítávají
    ///   jako odpočet — boundary prochází přes otvor rovně (Finish location).
    /// </summary>
    public sealed class PodlahovaPlochaNV366Strategy : StrategyBase, IAreaComputationStrategy
    {
        private readonly IGeometryEngine _geometryEngine;
        private readonly IHeightZoneAnalyzer _heightZoneAnalyzer;

        public AreaType SupportedAreaType => AreaType.PodlahovaPlochaNV366;

        public PodlahovaPlochaNV366Strategy(
            IGeometryEngine geometryEngine,
            IHeightZoneAnalyzer heightZoneAnalyzer,
            INormInfoProvider normInfoProvider)
            : base(normInfoProvider)
        {
            _geometryEngine = geometryEngine
                ?? throw new ArgumentNullException(nameof(geometryEngine));
            _heightZoneAnalyzer = heightZoneAnalyzer
                ?? throw new ArgumentNullException(nameof(heightZoneAnalyzer));
        }

        public async Task<IReadOnlyList<AreaComputationResult>> ComputeAsync(
            IReadOnlyList<ElementId> targetElementIds,
            Document document,
            ProjectContext context,
            CancellationToken cancellationToken)
        {
            var results = new List<AreaComputationResult>();

            var spatialCalcOptions = new SpatialElementGeometryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
                StoreFreeBoundaryFaces = true
            };

            var spatialCalc = new SpatialElementGeometryCalculator(
                document, spatialCalcOptions);

            foreach (var elementId in targetElementIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var element = document.GetElement(elementId);
                if (element is not Room room)
                    continue;

                // Přeskočení neumístěných místností
                if (room.Area < RevitConstants.MinRoomAreaThresholdFt2)
                {
                    results.Add(BuildSkippedResult(elementId, room.Name,
                        ComputationStatus.SkippedNotPlaced,
                        "Room.Area == 0 — místnost není umístěna nebo nemá uzavřenou hranici."));
                    continue;
                }

                SpatialElementGeometryResults spatialResult;
                try
                {
                    spatialResult = spatialCalc.CalculateSpatialElementGeometry(room);
                }
                catch (Exception ex)
                {
                    results.Add(BuildSkippedResult(elementId, room.Name,
                        ComputationStatus.SkippedInsufficientGeometry,
                        $"SpatialElementGeometryCalculator selhalo: {ex.Message}"));
                    continue;
                }

                var roomSolid = spatialResult.GetGeometry();

                if (roomSolid is null || roomSolid.Volume < RevitConstants.MinSolidVolumeFt3)
                {
                    results.Add(BuildSkippedResult(elementId, room.Name,
                        ComputationStatus.SkippedInsufficientGeometry,
                        "Room solid má nulový nebo záporný objem."));
                    continue;
                }

                // Krok 1: floor projection loops
                var floorLoops = _geometryEngine.ExtractFloorProjectionLoops(roomSolid);

                if (!floorLoops.Any())
                {
                    results.Add(BuildSkippedResult(elementId, room.Name,
                        ComputationStatus.SkippedInsufficientGeometry,
                        "Nepodařilo se extrahovat podlahové loops z room solidu."));
                    continue;
                }

                // Krok 2: výškové zóny (NV §4 odst. 2)
                var heightZones = await _heightZoneAnalyzer.AnalyzeAsync(
                    room, roomSolid, floorLoops, document);

                // Krok 3: odečet konstrukčních sloupů uvnitř místnosti (NV §4 odst. 3)
                var structuralDeductions = ComputeStructuralDeductions(
                    room, floorLoops, document);

                double structuralDeductionM2 = structuralDeductions.Sum(d => d.DeductedAreaM2);

                // Krok 4: aplikace výškových koeficientů
                double fullAreaM2 = heightZones.FullZoneAreaM2;
                double halfAreaM2 = heightZones.HalfZoneAreaM2 * 0.5;  // koef. 0.5
                // ZeroZone = 0.0

                double rawAreaM2 = fullAreaM2 + halfAreaM2 - structuralDeductionM2;
                rawAreaM2 = Math.Max(0.0, rawAreaM2);

                // Krok 5: koeficient prostoru (NV §5)
                double spaceMultiplier = GetSpaceMultiplier(room);
                double finalAreaM2 = rawAreaM2 * spaceMultiplier;

                // Sestav deductions list
                var deductions = new List<AreaDeduction>(structuralDeductions);

                if (heightZones.HalfZoneAreaM2 > 0)
                {
                    deductions.Add(new AreaDeduction
                    {
                        Type = DeductionType.HeightReductionHalf,
                        DeductedAreaM2 = heightZones.HalfZoneAreaM2 * 0.5,
                        LegalBasis = "NV č. 366/2013 Sb. §4 odst. 2 — výška 1 200–2 000 mm, koef. 0,5",
                        DeductedGeometry = heightZones.HalfZoneLoops
                    });
                }

                if (heightZones.ZeroZoneAreaM2 > 0)
                {
                    deductions.Add(new AreaDeduction
                    {
                        Type = DeductionType.HeightZeroExclusion,
                        DeductedAreaM2 = heightZones.ZeroZoneAreaM2,
                        LegalBasis = "NV č. 366/2013 Sb. §4 odst. 2 — výška pod 1 200 mm, nezapočítává se",
                        DeductedGeometry = heightZones.ZeroZoneLoops
                    });
                }

                if (Math.Abs(spaceMultiplier - 1.0) > 1e-6)
                {
                    deductions.Add(new AreaDeduction
                    {
                        Type = DeductionType.SpaceTypeMultiplier,
                        DeductedAreaM2 = rawAreaM2 * (1.0 - spaceMultiplier),
                        LegalBasis = $"NV č. 366/2013 Sb. §5 — typ prostoru: {GetSpaceTypeName(room)}, koef. {spaceMultiplier:G}",
                        DeductedGeometry = null
                    });
                }

                results.Add(new AreaComputationResult
                {
                    SourceElementId = elementId,
                    SourceElementName = room.Name,
                    AreaType = AreaType.PodlahovaPlochaNV366,
                    ComputedAreaM2 = Math.Round(finalAreaM2, 4),
                    Status = ComputationStatus.Success,
                    Audit = BuildAudit(
                        AreaType.PodlahovaPlochaNV366,
                        "SpatialGeometry_FinishFace + HeightZoneGrid_100mm",
                        1,
                        $"Místnost: {room.Name}, Koef. prostoru: {spaceMultiplier:G}"),
                    Deductions = deductions,
                    ComputedBoundary = floorLoops
                });
            }

            return results;
        }

        private List<AreaDeduction> ComputeStructuralDeductions(
            Room room,
            IReadOnlyList<CurveLoop> floorLoops,
            Document document)
        {
            var deductions = new List<AreaDeduction>();

            // Sběr konstrukčních sloupů v bounding boxu místnosti
            var roomBB = room.get_BoundingBox(null);
            if (roomBB is null)
                return deductions;

            var outline = new Outline(roomBB.Min, roomBB.Max);
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            var columns = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WherePasses(bbFilter)
                .UnionWith(
                    new FilteredElementCollector(document)
                        .OfCategory(BuiltInCategory.OST_Columns)
                        .WherePasses(bbFilter))
                .ToElements();

            double floorElevationFt = floorLoops.Any()
                ? floorLoops.First().First().GetEndPoint(0).Z
                : 0.0;

            foreach (var column in columns)
            {
                var columnSolid = GeometryEngine.GetLargestSolid(column);
                if (columnSolid is null)
                    continue;

                // Průmět sloupu na podlahu
                var columnLoops = _geometryEngine.ExtractFloorProjectionLoops(columnSolid);
                if (!columnLoops.Any())
                    continue;

                // Odečteme průnik sloupu s plochou místnosti pomocí Clipper2
                foreach (var roomLoop in floorLoops)
                {
                    foreach (var columnLoop in columnLoops)
                    {
                        // Pokud je sloup uvnitř místnosti — odečteme
                        // Pokud je sloup stěnou — není v místnosti, přeskočíme
                        double columnAreaM2 = _geometryEngine.ComputeLoopAreaM2(columnLoop);

                        if (columnAreaM2 < 1e-4) // < 1 cm² — ignorujeme
                            continue;

                        deductions.Add(new AreaDeduction
                        {
                            Type = DeductionType.StructuralColumn,
                            DeductedAreaM2 = columnAreaM2,
                            LegalBasis = "NV č. 366/2013 Sb. §4 odst. 3 — svislé nosné konstrukce",
                            DeductedGeometry = columnLoops
                        });
                    }
                }
            }

            return deductions;
        }

        private static double GetSpaceMultiplier(Room room)
        {
            var param = room.LookupParameter(SharedParameterConstants.SpaceTypeCzech);
            if (param is null || param.StorageType != StorageType.String)
                return 1.0;

            return param.AsString()?.ToUpperInvariant() switch
            {
                "LODŽIE" => 1.0,
                "BALKON" => 0.5,
                "TERASA" => 0.25,
                _ => 1.0
            };
        }

        private static string GetSpaceTypeName(Room room)
        {
            var param = room.LookupParameter(SharedParameterConstants.SpaceTypeCzech);
            return param?.AsString() ?? "STANDARD";
        }

        private AreaComputationResult BuildSkippedResult(
            ElementId id,
            string name,
            ComputationStatus status,
            string reason)
        {
            return new AreaComputationResult
            {
                SourceElementId = id,
                SourceElementName = name,
                AreaType = AreaType.PodlahovaPlochaNV366,
                ComputedAreaM2 = 0.0,
                Status = status,
                Audit = BuildAudit(AreaType.PodlahovaPlochaNV366,
                                       "Skipped", 0, reason),
                Deductions = Array.Empty<AreaDeduction>(),
                ComputedBoundary = Array.Empty<CurveLoop>(),
                ErrorMessage = reason
            };
        }
    }
}

