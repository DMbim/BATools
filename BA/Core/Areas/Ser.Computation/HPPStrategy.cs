using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Core.Models;

namespace BA.Services.Computation
{
    /// <summary>
    /// Výpočet hrubé podlažní plochy (HPP) dle PSP §2 písm. c) a §2 písm. g).
    ///
    /// HPP = součet ploch všech podlaží měřeno k vnějšímu líci obvodových konstrukcí.
    /// Zahrnuje: nosné stěny, sloupy, schodiště, šachty, technické místnosti.
    ///
    /// PSP §2 g): Podzemní podlaží = podlaha níže než 800 mm pod průměrným UT.
    ///
    /// AreaType parameter určuje, zda zpracováváme nadzemní nebo podzemní.
    /// Orchestrator volá tuto strategii dvakrát s různým AreaType.
    /// </summary>
    public sealed class HPPStrategy : StrategyBase, IAreaComputationStrategy
    {
        private readonly IGeometryEngine _geometryEngine;
        private readonly HPPClassifier _classifier;

        private readonly AreaType _supportedType;

        public AreaType SupportedAreaType => _supportedType;

        public HPPStrategy(
            AreaType supportedType,
            IGeometryEngine geometryEngine,
            HPPClassifier classifier,
            INormInfoProvider normInfoProvider)
            : base(normInfoProvider)
        {
            if (supportedType != AreaType.HPPNadzemni && supportedType != AreaType.HPPPodzemni)
                throw new ArgumentException(
                    "HPPStrategy podporuje pouze HPPNadzemni nebo HPPPodzemni.",
                    nameof(supportedType));

            _supportedType = supportedType;
            _geometryEngine = geometryEngine
                ?? throw new ArgumentNullException(nameof(geometryEngine));
            _classifier = classifier
                ?? throw new ArgumentNullException(nameof(classifier));
        }

        public async Task<IReadOnlyList<AreaComputationResult>> ComputeAsync(
            IReadOnlyList<ElementId> targetElementIds,
            Document document,
            ProjectContext context,
            CancellationToken cancellationToken)
        {
            var results = new List<AreaComputationResult>();

            foreach (var levelId in targetElementIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var level = document.GetElement(levelId) as Level;
                if (level is null)
                    continue;

                // Klasifikace podlaží dle PSP §2 g)
                var classification = _classifier.Classify(
                    level, context.AverageTerenElevationMm);

                // Tato strategie zpracovává pouze svůj typ
                if (classification == FloorClassification.Nadzemni &&
                    _supportedType == AreaType.HPPPodzemni)
                    continue;

                if (classification == FloorClassification.Podzemni &&
                    _supportedType == AreaType.HPPNadzemni)
                    continue;

                // Sběr obvodových stěn na tomto podlaží
                var walls = CollectPerimeterWalls(level, document);

                if (!walls.Any())
                {
                    results.Add(BuildSkippedResult(
                        levelId,
                        level.Name,
                        ComputationStatus.SkippedInsufficientGeometry,
                        "Na podlaží nebyly nalezeny žádné obvodové stěny."));
                    continue;
                }

                // Outer shell footprint měřený k vnějšímu líci (PSP §2 c)
                var outerLoops = await _geometryEngine.BuildOuterShellFootprintAsync(
                    walls, level, document);

                if (!outerLoops.Any())
                {
                    results.Add(BuildSkippedResult(
                        levelId,
                        level.Name,
                        ComputationStatus.SkippedInsufficientGeometry,
                        "Nepodařilo se sestavit uzavřený outer shell footprint."));
                    continue;
                }

                double totalAreaM2 = outerLoops.Sum(l => _geometryEngine.ComputeLoopAreaM2(l));

                results.Add(new AreaComputationResult
                {
                    SourceElementId = levelId,
                    SourceElementName = level.Name,
                    AreaType = _supportedType,
                    ComputedAreaM2 = Math.Round(totalAreaM2, 4),
                    Status = ComputationStatus.Success,
                    FloorClassification = classification,
                    Audit = BuildAudit(
                        _supportedType,
                        "OuterShellFootprint_ExteriorFace_PSP",
                        walls.Count,
                        $"Podlaží: {level.Name}, Klasifikace: {classification}, " +
                        $"Průměrný UT: {context.AverageTerenElevationMm:F0} mm"),
                    Deductions = Array.Empty<AreaDeduction>(),
                    ComputedBoundary = outerLoops
                });
            }

            return results;
        }

        private static List<Wall> CollectPerimeterWalls(Level level, Document document)
        {
            // Výška hledání stěn: od podlaží do +6000 mm (20 ft)
            double searchHeightFt = 20.0;

            var levelBBFilter = new BoundingBoxIntersectsFilter(
                new Outline(
                    new XYZ(-1e6, -1e6, level.Elevation),
                    new XYZ(1e6, 1e6, level.Elevation + searchHeightFt)));

            return new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .WherePasses(levelBBFilter)
                .Cast<Wall>()
                .Where(w =>
                    w.WallType.Kind == WallKind.Basic &&
                    IsExteriorOrUnknownFunction(w))
                .ToList();
        }

        private static bool IsExteriorOrUnknownFunction(Wall wall)
        {
            var function = (WallFunction)wall.get_Parameter(
                BuiltInParameter.FUNCTION_PARAM)?.AsInteger();

            // HPP zahrnuje obvodové + konstrukční stěny
            return function == WallFunction.Exterior ||
                   function == WallFunction.Foundation ||
                   function == WallFunction.Retaining;
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
                AreaType = _supportedType,
                ComputedAreaM2 = 0.0,
                Status = status,
                Audit = BuildAudit(_supportedType, "Skipped", 0, reason),
                Deductions = Array.Empty<AreaDeduction>(),
                ComputedBoundary = Array.Empty<CurveLoop>(),
                ErrorMessage = reason
            };
        }
    }
}

