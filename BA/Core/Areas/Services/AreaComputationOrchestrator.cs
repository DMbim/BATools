using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Core.Models;

namespace BA.Services
{
    /// <summary>
    /// Orchestruje výpočet všech vybraných typů ploch.
    /// Volá příslušné IAreaComputationStrategy implementace.
    /// Spouštěno z ExternalEventHandler na main Revit threadu.
    /// </summary>
    public sealed class AreaComputationOrchestrator : IAreaComputationOrchestrator
    {
        private readonly IReadOnlyDictionary<AreaType, IAreaComputationStrategy> _strategies;

        public AreaComputationOrchestrator(
            IReadOnlyDictionary<AreaType, IAreaComputationStrategy> strategies)
        {
            _strategies = strategies
                ?? throw new ArgumentNullException(nameof(strategies));
        }

        public async Task<IReadOnlyList<AreaComputationResult>> ComputeAsync(
            IReadOnlyList<AreaType> areaTypes,
            IReadOnlyList<ElementId> targetElementIds,
            UIApplication uiApplication,
            CancellationToken cancellationToken)
        {
            if (areaTypes is null)
                throw new ArgumentNullException(nameof(areaTypes));
            if (targetElementIds is null)
                throw new ArgumentNullException(nameof(targetElementIds));

            var document = uiApplication.ActiveUIDocument.Document;

            // Sestavení ProjectContext z dokumentu
            var context = BuildProjectContext(document);

            var allResults = new List<AreaComputationResult>();

            foreach (var areaType in areaTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_strategies.TryGetValue(areaType, out var strategy))
                    continue;

                // HPP strategie dostává Level IDs, ostatní Room IDs
                var elementIds = IsHPPType(areaType)
                    ? ResolveHPPTargets(targetElementIds, document)
                    : targetElementIds;

                var results = await strategy.ComputeAsync(
                    elementIds, document, context, cancellationToken);

                allResults.AddRange(results);
            }

            return allResults;
        }

        private static ProjectContext BuildProjectContext(Document document)
        {
            double terenMm = 0.0;

            // Pokus o čtení z TopographySurface
            var topoCollector = new FilteredElementCollector(document)
                .OfClass(typeof(TopographySurface))
                .Cast<TopographySurface>()
                .FirstOrDefault();

            if (topoCollector is not null)
            {
                var topoPoints = topoCollector.GetPoints();
                if (topoPoints.Count > 0)
                {
                    double avgZFt = topoPoints.Average(p => p.Z);
                    terenMm = UnitUtils.ConvertFromInternalUnits(avgZFt, UnitTypeId.Millimeters);
                }
            }
            else
            {
                // Fallback: sdílený parametr CZA_UpravenyTeren_mmNN na ProjectInfo
                var projectInfo = document.ProjectInformation;
                var terenParam = projectInfo?.LookupParameter(
                    BA.Services.Computation.SharedParameterConstants.UpravenyTerenMmNN);

                if (terenParam != null && terenParam.StorageType == StorageType.Double)
                    terenMm = terenParam.AsDouble();
            }

            return new ProjectContext
            {
                AverageTerenElevationMm = terenMm,
                Municipality = document.ProjectInformation?.Address
            };
        }

        private static bool IsHPPType(AreaType areaType) =>
            areaType == AreaType.HPPNadzemni || areaType == AreaType.HPPPodzemni;

        /// <summary>
        /// Pro HPP: pokud jsou vybrány Room IDs, rozlož je na Level IDs.
        /// Pokud jsou již Level IDs, použij přímo.
        /// </summary>
        private static IReadOnlyList<ElementId> ResolveHPPTargets(
            IReadOnlyList<ElementId> targetIds,
            Document document)
        {
            var levelIds = new HashSet<ElementId>();

            foreach (var id in targetIds)
            {
                var element = document.GetElement(id);
                if (element is Level)
                {
                    levelIds.Add(id);
                }
                else if (element is Room room)
                {
                    if (room.Level?.Id is not null)
                        levelIds.Add(room.Level.Id);
                }
                else
                {
                    // Pokud není vybráno nic konkrétního, vezmi všechna podlaží
                    var allLevels = new FilteredElementCollector(document)
                        .OfClass(typeof(Level))
                        .Select(e => e.Id);

                    foreach (var lid in allLevels)
                        levelIds.Add(lid);

                    break;
                }
            }

            // Pokud je targetIds prázdný, vezmi všechna podlaží
            if (!levelIds.Any())
            {
                var allLevels = new FilteredElementCollector(document)
                    .OfClass(typeof(Level))
                    .Select(e => e.Id);

                foreach (var lid in allLevels)
                    levelIds.Add(lid);
            }

            return levelIds.ToList();
        }
    }
}