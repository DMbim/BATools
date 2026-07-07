using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Data
{
    /// <summary>
    /// Data access for FamilyBuildingState: the last-observed state of a tracked family
    /// within a specific building's central model. This is the highest-write-frequency
    /// table in the catalog, every confirmed family load/reload event in any building
    /// session writes here. Upsert is the primary write pattern since each
    /// (FamilyId, BuildingId) pair has exactly one current-state row (UNIQUE constraint
    /// in the schema), history of how that state changed over time lives in AuditLog,
    /// not here, this table only ever holds the latest snapshot.
    /// </summary>
    public sealed class FamilyBuildingStateRepository
    {
        private readonly CatalogConnectionFactory _connectionFactory;

        public FamilyBuildingStateRepository(CatalogConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Inserts or updates the state row for (familyId, buildingId). This is the
        /// write the post-hoc confirm dialog calls once the user has accepted (or
        /// overridden) a detected version/hash for a family load event. Uses SQLite's
        /// UPSERT syntax (INSERT ... ON CONFLICT ... DO UPDATE), available since
        /// SQLite 3.24, well within Microsoft.Data.Sqlite's bundled engine version.
        /// </summary>
        public void Upsert(
            int familyId,
            int buildingId,
            string loadedVersion,
            string loadedHash,
            string loadedByUser,
            FamilyBumpKind bumpKind,
            string? diffSummary)
        {
            if (string.IsNullOrWhiteSpace(loadedVersion))
            {
                throw new ArgumentException("Loaded version must not be empty.", nameof(loadedVersion));
            }

            var nowUtc = DateTime.UtcNow;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO FamilyBuildingState
                            (FamilyId, BuildingId, LoadedVersion, LoadedHash, LastLoadedByUser,
                             LastLoadedUtc, LastBumpKind, LastDiffSummary)
                        VALUES
                            ($familyId, $buildingId, $version, $hash, $user, $loadedUtc, $bumpKind, $diffSummary)
                        ON CONFLICT (FamilyId, BuildingId) DO UPDATE SET
                            LoadedVersion = excluded.LoadedVersion,
                            LoadedHash = excluded.LoadedHash,
                            LastLoadedByUser = excluded.LastLoadedByUser,
                            LastLoadedUtc = excluded.LastLoadedUtc,
                            LastBumpKind = excluded.LastBumpKind,
                            LastDiffSummary = excluded.LastDiffSummary;";

                    command.Parameters.AddWithValue("$familyId", familyId);
                    command.Parameters.AddWithValue("$buildingId", buildingId);
                    command.Parameters.AddWithValue("$version", loadedVersion.Trim());
                    command.Parameters.AddWithValue("$hash", loadedHash ?? string.Empty);
                    command.Parameters.AddWithValue("$user", loadedByUser ?? string.Empty);
                    command.Parameters.AddWithValue("$loadedUtc", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$bumpKind", bumpKind.ToString());
                    command.Parameters.AddWithValue("$diffSummary", (object?)diffSummary ?? DBNull.Value);

                    command.ExecuteNonQuery();
                }
            });
        }

        public FamilyBuildingState? Get(int familyId, int buildingId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT StateId, FamilyId, BuildingId, LoadedVersion, LoadedHash,
                               LastLoadedByUser, LastLoadedUtc, LastBumpKind, LastDiffSummary
                        FROM FamilyBuildingState
                        WHERE FamilyId = $familyId AND BuildingId = $buildingId;";

                    command.Parameters.AddWithValue("$familyId", familyId);
                    command.Parameters.AddWithValue("$buildingId", buildingId);

                    using (var reader = command.ExecuteReader())
                    {
                        return reader.Read() ? MapState(reader) : null;
                    }
                }
            });
        }

        /// <summary>
        /// Returns every per-building state row for a given family, used by the
        /// coordination dashboard to lay out one family's versions side by side
        /// across all buildings.
        /// </summary>
        public List<FamilyBuildingState> GetAllForFamily(int familyId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<FamilyBuildingState>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT StateId, FamilyId, BuildingId, LoadedVersion, LoadedHash,
                               LastLoadedByUser, LastLoadedUtc, LastBumpKind, LastDiffSummary
                        FROM FamilyBuildingState
                        WHERE FamilyId = $familyId;";

                    command.Parameters.AddWithValue("$familyId", familyId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapState(reader));
                        }
                    }
                }

                return results;
            });
        }

        /// <summary>
        /// Returns every state row recorded for a given building, used when a building
        /// session starts up and wants to know everything it has previously reported,
        /// e.g. to compare against current in-model state and detect changes made
        /// outside this add-in's awareness (a family edited in a prior session before
        /// this versioning module was installed, for example).
        /// </summary>
        public List<FamilyBuildingState> GetAllForBuilding(int buildingId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<FamilyBuildingState>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT StateId, FamilyId, BuildingId, LoadedVersion, LoadedHash,
                               LastLoadedByUser, LastLoadedUtc, LastBumpKind, LastDiffSummary
                        FROM FamilyBuildingState
                        WHERE BuildingId = $buildingId;";

                    command.Parameters.AddWithValue("$buildingId", buildingId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapState(reader));
                        }
                    }
                }

                return results;
            });
        }

        private static FamilyBuildingState MapState(SqliteDataReader reader)
        {
            var lastLoadedByOrdinal = reader.GetOrdinal("LastLoadedByUser");
            var lastLoadedUtcOrdinal = reader.GetOrdinal("LastLoadedUtc");
            var diffSummaryOrdinal = reader.GetOrdinal("LastDiffSummary");

            return new FamilyBuildingState
            {
                StateId = reader.GetInt32(reader.GetOrdinal("StateId")),
                FamilyId = reader.GetInt32(reader.GetOrdinal("FamilyId")),
                BuildingId = reader.GetInt32(reader.GetOrdinal("BuildingId")),
                LoadedVersion = reader.GetString(reader.GetOrdinal("LoadedVersion")),
                LoadedHash = reader.GetString(reader.GetOrdinal("LoadedHash")),
                LastLoadedByUser = reader.IsDBNull(lastLoadedByOrdinal) ? null : reader.GetString(lastLoadedByOrdinal),
                LastLoadedUtc = reader.IsDBNull(lastLoadedUtcOrdinal)
                    ? (DateTime?)null
                    : DateTime.Parse(reader.GetString(lastLoadedUtcOrdinal)).ToUniversalTime(),
                LastBumpKind = Enum.TryParse<FamilyBumpKind>(
                    reader.GetString(reader.GetOrdinal("LastBumpKind")), out var parsedBumpKind)
                    ? parsedBumpKind
                    : FamilyBumpKind.Unknown,
                LastDiffSummary = reader.IsDBNull(diffSummaryOrdinal) ? null : reader.GetString(diffSummaryOrdinal)
            };
        }
    }
}
