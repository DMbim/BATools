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

namespace BA.Services.Computation
{
    /// <summary>
    /// Výpočet podlahové plochy dle SZ č. 283/2021 Sb. §13 písm. n).
    ///
    /// SZ §13 n) definuje podlahovou plochu jako plochu půdorysného průřezu
    /// místnosti nebo prostoru měřenou v úrovni podlahy.
    ///
    /// Implementační rozdíl oproti NV 366/2013:
    /// • Bez výškových zón (žádná redukce dle výšky)
    /// • Bez koeficientů prostoru (lodžie/balkon/terasa)
    /// • Měření k vnitřnímu líci (Finish face) — shodné s NV 366
    /// • Bez odečtu konstrukčních prvků (SZ §13 n) toto explicitně nespecifikuje)
    /// </summary>
    public sealed class PodlahovaPlochaSZStrategy : StrategyBase, IAreaComputationStrategy
    {
        private readonly IGeometryEngine _geometryEngine;

        public AreaType SupportedAreaType => AreaType.PodlahovaPlochaSZ;

        public PodlahovaPlochaSZStrategy(
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
            var results = new List<AreaComputationResult>();

            var spatialCalcOptions = new SpatialElementGeometryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
                StoreFreeBoundaryFaces = false
            };

            var spatialCalc = new SpatialElementGeometryCalculator(
                document, spatialCalcOptions);

            foreach (var elementId in targetElementIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var element = document.GetElement(elementId);
                if (element is not Room room)
                    continue;

                if (room.Area < RevitConstants.MinRoomAreaThresholdFt2)
                {
                    results.Add(BuildSkippedResult(elementId, room.Name,
                        ComputationStatus.SkippedNotPlaced,
                        "Místnost není umístěna."));
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
                        $"Chyba geometrie: {ex.Message}"));
                    continue;
                }

                var roomSolid = spatialResult.GetGeometry();
                if (roomSolid is null || roomSolid.Volume < RevitConstants.MinSolidVolumeFt3)
                {
                    results.Add(BuildSkippedResult(elementId, room.Name,
                        ComputationStatus.SkippedInsufficientGeometry,
                        "Room solid má nulový objem."));
                    continue;
                }

                var floorLoops = _geometryEngine.ExtractFloorProjectionLoops(roomSolid);

                if (!floorLoops.Any())
                {
                    results.Add(BuildSkippedResult(elementId, room.Name,
                        ComputationStatus.SkippedInsufficientGeometry,
                        "Nelze extrahovat floor loops."));
                    continue;
                }

                // SZ §13 n): prostý průmět bez redukcí
                double totalAreaM2 = floorLoops.Sum(l => _geometryEngine.ComputeLoopAreaM2(l));

                results.Add(new AreaComputationResult
                {
                    SourceElementId = elementId,
                    SourceElementName = room.Name,
                    AreaType = AreaType.PodlahovaPlochaSZ,
                    ComputedAreaM2 = Math.Round(totalAreaM2, 4),
                    Status = ComputationStatus.Success,
                    Audit = BuildAudit(
                        AreaType.PodlahovaPlochaSZ,
                        "SpatialGeometry_FinishFace_NoReductions",
                        1,
                        $"Místnost: {room.Name}"),
                    Deductions = Array.Empty<AreaDeduction>(),
                    ComputedBoundary = floorLoops
                });
            }

            return results;
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
                AreaType = AreaType.PodlahovaPlochaSZ,
                ComputedAreaM2 = 0.0,
                Status = status,
                Audit = BuildAudit(AreaType.PodlahovaPlochaSZ,
                                       "Skipped", 0, reason),
                Deductions = Array.Empty<AreaDeduction>(),
                ComputedBoundary = Array.Empty<CurveLoop>(),
                ErrorMessage = reason
            };
        }
    }
}

