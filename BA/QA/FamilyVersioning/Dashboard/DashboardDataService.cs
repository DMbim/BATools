using System.Collections.Generic;
using System.Linq;
using BA.QA.FamilyVersioning.Data;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Dashboard
{
    /// <summary>
    /// Reads the catalog and assembles FamilyDashboardRow instances for the
    /// coordination dashboard. Kept as a separate service class so the ViewModel
    /// stays thin and this logic is independently testable without a WPF context.
    /// All methods are synchronous since SQLite reads are fast enough for this
    /// dataset size (one project's family catalog) and async would complicate the
    /// Revit/WPF threading model unnecessarily.
    /// </summary>
    public sealed class DashboardDataService
    {
        private readonly CatalogConnectionFactory _factory;

        public DashboardDataService(CatalogConnectionFactory factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Returns all enabled buildings for column header generation. Called once
        /// on dashboard open and on manual refresh so column definitions stay in
        /// sync with whatever buildings are currently configured.
        /// </summary>
        public List<Building> GetEnabledBuildings()
        {
            var repo = new BuildingRepository(_factory);
            return repo.GetAllBuildings().Where(b => b.Enabled).ToList();
        }

        /// <summary>
        /// Builds the full set of dashboard rows. Each row represents one tracked
        /// family with its per-building version state and mismatch status.
        /// </summary>
        public List<FamilyDashboardRow> BuildRows(List<Building> enabledBuildings)
        {
            var familyRepo = new FamilyRepository(_factory);
            var stateRepo = new FamilyBuildingStateRepository(_factory);
            var exceptionRepo = new ExceptionRepository(_factory);

            var allFamilies = familyRepo.GetAll();
            var allActiveExceptions = exceptionRepo.GetAllActive();

            // Index exceptions by (FamilyId, BuildingId) for O(1) lookup per cell.
            var exceptionLookup = new HashSet<(int FamilyId, int BuildingId)>(
                allActiveExceptions.Select(e => (e.FamilyId, e.BuildingId)));

            var rows = new List<FamilyDashboardRow>();

            foreach (var family in allFamilies)
            {
                var versionPerBuilding = new Dictionary<int, string?>();
                var exceptionBuildingIds = new HashSet<int>();

                foreach (var building in enabledBuildings)
                {
                    var state = stateRepo.Get(family.FamilyId, building.BuildingId);
                    versionPerBuilding[building.BuildingId] = state?.LoadedVersion;

                    if (exceptionLookup.Contains((family.FamilyId, building.BuildingId)))
                    {
                        exceptionBuildingIds.Add(building.BuildingId);
                    }
                }

                rows.Add(new FamilyDashboardRow(
                    family.FamilyId,
                    family.FamilyName,
                    family.CategoryName,
                    family.CanonicalVersion,
                    versionPerBuilding,
                    exceptionBuildingIds));
            }

            return rows.OrderBy(r => r.CategoryName)
                       .ThenBy(r => r.FamilyName)
                       .ToList();
        }
    }
}
