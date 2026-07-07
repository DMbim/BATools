using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Data
{
    /// <summary>
    /// Data access for the Families table, the canonical ("should be") record for each
    /// tracked family. A Family in this table is identified by (FamilyName, CategoryName),
    /// matching how Revit itself disambiguates families that share a name across
    /// categories. CanonicalVersion/CanonicalHash represent the office standard;
    /// per-building actual state lives in FamilyBuildingState, not here.
    /// </summary>
    public sealed class FamilyRepository
    {
        private readonly CatalogConnectionFactory _connectionFactory;

        public FamilyRepository(CatalogConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Returns the existing Family row for (familyName, categoryName), or creates
        /// one with default canonical state (version 0.0.0, empty hash) if it does not
        /// yet exist. This is the entry point the DocumentChanged hook calls on first
        /// observing a family it has not seen before in this catalog, get-or-create
        /// avoids a separate existence check plus insert race in calling code.
        /// </summary>
        public TrackedFamily GetOrCreate(string familyName, string categoryName)
        {
            if (string.IsNullOrWhiteSpace(familyName))
            {
                throw new ArgumentException("Family name must not be empty.", nameof(familyName));
            }

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                throw new ArgumentException("Category name must not be empty.", nameof(categoryName));
            }

            var existing = FindByNameAndCategory(familyName, categoryName);
            if (existing != null)
            {
                return existing;
            }

            var nowUtc = DateTime.UtcNow;

            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    // INSERT OR IGNORE handles the race where two building sessions
                    // observe the same brand-new family for the first time near
                    // simultaneously, the UNIQUE constraint on (FamilyName, CategoryName)
                    // makes the second insert a no-op rather than an exception.
                    command.CommandText = @"
                        INSERT OR IGNORE INTO Families
                            (FamilyName, CategoryName, CanonicalVersion, CanonicalHash, CreatedUtc, ModifiedUtc)
                        VALUES
                            ($name, $category, '0.0.0', '', $created, $modified);";

                    command.Parameters.AddWithValue("$name", familyName.Trim());
                    command.Parameters.AddWithValue("$category", categoryName.Trim());
                    command.Parameters.AddWithValue("$created", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$modified", nowUtc.ToString("o"));
                    command.ExecuteNonQuery();
                }

                using (var selectCommand = connection.CreateCommand())
                {
                    selectCommand.CommandText = @"
                        SELECT FamilyId, FamilyName, CategoryName, CanonicalVersion, CanonicalHash,
                               CanonicalSourcePath, CreatedUtc, ModifiedUtc
                        FROM Families
                        WHERE FamilyName = $name AND CategoryName = $category;";

                    selectCommand.Parameters.AddWithValue("$name", familyName.Trim());
                    selectCommand.Parameters.AddWithValue("$category", categoryName.Trim());

                    using (var reader = selectCommand.ExecuteReader())
                    {
                        reader.Read();
                        return MapFamily(reader);
                    }
                }
            });
        }

        public TrackedFamily? FindByNameAndCategory(string familyName, string categoryName)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT FamilyId, FamilyName, CategoryName, CanonicalVersion, CanonicalHash,
                               CanonicalSourcePath, CreatedUtc, ModifiedUtc
                        FROM Families
                        WHERE FamilyName = $name AND CategoryName = $category;";

                    command.Parameters.AddWithValue("$name", familyName.Trim());
                    command.Parameters.AddWithValue("$category", categoryName.Trim());

                    using (var reader = command.ExecuteReader())
                    {
                        return reader.Read() ? MapFamily(reader) : null;
                    }
                }
            });
        }

        public TrackedFamily? GetById(int familyId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT FamilyId, FamilyName, CategoryName, CanonicalVersion, CanonicalHash,
                               CanonicalSourcePath, CreatedUtc, ModifiedUtc
                        FROM Families
                        WHERE FamilyId = $id;";

                    command.Parameters.AddWithValue("$id", familyId);

                    using (var reader = command.ExecuteReader())
                    {
                        return reader.Read() ? MapFamily(reader) : null;
                    }
                }
            });
        }

        /// <summary>
        /// Updates the canonical (office-standard) version and hash for a family.
        /// This should only be called when a human has explicitly designated a given
        /// state as the new standard, typically via the confirm dialog's "this is now
        /// the canonical version" action, never silently from automatic detection alone.
        /// </summary>
        public void UpdateCanonicalState(int familyId, string newVersion, string newHash, string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(newVersion))
            {
                throw new ArgumentException("Version must not be empty.", nameof(newVersion));
            }

            var nowUtc = DateTime.UtcNow;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE Families
                        SET CanonicalVersion = $version,
                            CanonicalHash = $hash,
                            CanonicalSourcePath = $sourcePath,
                            ModifiedUtc = $modified
                        WHERE FamilyId = $id;";

                    command.Parameters.AddWithValue("$version", newVersion.Trim());
                    command.Parameters.AddWithValue("$hash", newHash ?? string.Empty);
                    command.Parameters.AddWithValue("$sourcePath", (object?)sourcePath ?? DBNull.Value);
                    command.Parameters.AddWithValue("$modified", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$id", familyId);

                    var rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected == 0)
                    {
                        throw new InvalidOperationException($"No family found with FamilyId {familyId}.");
                    }
                }
            });
        }

        /// <summary>
        /// Returns every tracked family. Intended for the coordination dashboard
        /// (Phase 2), which joins this against FamilyBuildingState per building to
        /// build the comparison view. For large libraries this could later need
        /// paging, not implemented here, flagging as a known scaling limit rather
        /// than guessing at a page size with no usage data yet.
        /// </summary>
        public List<TrackedFamily> GetAll()
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<TrackedFamily>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT FamilyId, FamilyName, CategoryName, CanonicalVersion, CanonicalHash,
                               CanonicalSourcePath, CreatedUtc, ModifiedUtc
                        FROM Families
                        ORDER BY CategoryName, FamilyName;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapFamily(reader));
                        }
                    }
                }

                return results;
            });
        }

        private static TrackedFamily MapFamily(SqliteDataReader reader)
        {
            var sourcePathOrdinal = reader.GetOrdinal("CanonicalSourcePath");

            return new TrackedFamily
            {
                FamilyId = reader.GetInt32(reader.GetOrdinal("FamilyId")),
                FamilyName = reader.GetString(reader.GetOrdinal("FamilyName")),
                CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
                CanonicalVersion = reader.GetString(reader.GetOrdinal("CanonicalVersion")),
                CanonicalHash = reader.GetString(reader.GetOrdinal("CanonicalHash")),
                CanonicalSourcePath = reader.IsDBNull(sourcePathOrdinal) ? null : reader.GetString(sourcePathOrdinal),
                CreatedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedUtc"))).ToUniversalTime(),
                ModifiedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("ModifiedUtc"))).ToUniversalTime()
            };
        }
    }
}
