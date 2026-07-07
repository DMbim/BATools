using System.Collections.Generic;
using System.Linq;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Dashboard
{
    /// <summary>
    /// Represents one family's version state across all enabled buildings in the
    /// catalog. Used as a row in the coordination dashboard grid. The per-building
    /// version values are stored in a dictionary keyed by BuildingId so the dynamic
    /// column generation code can look up any building's version for any row without
    /// needing a fixed schema.
    /// </summary>
    public sealed class FamilyDashboardRow
    {
        public int FamilyId { get; }
        public string FamilyName { get; }
        public string CategoryName { get; }
        public string CanonicalVersion { get; }

        /// <summary>
        /// Version loaded in each building, keyed by BuildingId. Null value means
        /// this building has no record of ever loading this family (never detected,
        /// never confirmed). Displayed as "-" in the grid.
        /// </summary>
        public Dictionary<int, string?> VersionPerBuilding { get; }

        /// <summary>
        /// Whether any two enabled buildings have different non-null versions for
        /// this family, excluding buildings that have an active exception for it.
        /// Null entries (family not yet seen in a building) count as a mismatch
        /// against buildings that do have a version, since a missing family in one
        /// building while others have it loaded is itself a consistency problem.
        /// </summary>
        public bool HasMismatch { get; }

        /// <summary>
        /// BuildingIds that have an active exception for this family. These are
        /// excluded from mismatch calculation and shown with a distinct indicator
        /// in the grid rather than highlighted red.
        /// </summary>
        public HashSet<int> ExceptionBuildingIds { get; }

        public FamilyDashboardRow(
            int familyId,
            string familyName,
            string categoryName,
            string canonicalVersion,
            Dictionary<int, string?> versionPerBuilding,
            HashSet<int> exceptionBuildingIds)
        {
            FamilyId = familyId;
            FamilyName = familyName;
            CategoryName = categoryName;
            CanonicalVersion = canonicalVersion;
            VersionPerBuilding = versionPerBuilding;
            ExceptionBuildingIds = exceptionBuildingIds;

            // Compute mismatch: compare versions across buildings that are NOT in
            // the exception set. If any two non-excepted buildings differ (including
            // one having null and another having a value), flag as mismatch.
            var nonExceptedVersions = versionPerBuilding
                .Where(kvp => !exceptionBuildingIds.Contains(kvp.Key))
                .Select(kvp => kvp.Value)
                .ToList();

            if (nonExceptedVersions.Count <= 1)
            {
                HasMismatch = false;
            }
            else
            {
                var distinctVersions = nonExceptedVersions.Distinct().ToList();
                HasMismatch = distinctVersions.Count > 1;
            }
        }

        /// <summary>
        /// Returns the display string for a specific building's version cell.
        /// Handles null (never loaded), exception status, and normal version display.
        /// </summary>
        public string GetCellDisplay(int buildingId)
        {
            if (!VersionPerBuilding.TryGetValue(buildingId, out var version))
            {
                return "-";
            }

            if (version == null)
            {
                return "-";
            }

            if (ExceptionBuildingIds.Contains(buildingId))
            {
                return $"{version} *";
            }

            return version;
        }

        /// <summary>
        /// Returns true if this specific building's cell should be highlighted as
        /// mismatched. A cell is mismatched if the family has an overall mismatch
        /// AND this building is not in the exception set AND its version differs
        /// from at least one other non-excepted building.
        /// </summary>
        public bool IsCellMismatched(int buildingId)
        {
            if (!HasMismatch) return false;
            if (ExceptionBuildingIds.Contains(buildingId)) return false;

            var thisVersion = VersionPerBuilding.TryGetValue(buildingId, out var v) ? v : null;

            var otherNonExceptedVersions = VersionPerBuilding
                .Where(kvp => kvp.Key != buildingId && !ExceptionBuildingIds.Contains(kvp.Key))
                .Select(kvp => kvp.Value)
                .ToList();

            return otherNonExceptedVersions.Any(other => other != thisVersion);
        }
    }
}
