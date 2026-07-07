using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Data
{
    /// <summary>
    /// Data access for the Buildings table. A Building represents one central model
    /// within the project. Supports the "configurable per project, with central model
    /// path, and an enable/disable toggle" requirement, this table is meant to be
    /// edited through a setup UI (WPF, not built yet, see Phase 0.5 setup screen note
    /// at the bottom of this file) before any detection logic in Phase 1 can run,
    /// since FamilyBuildingState rows are foreign-keyed to BuildingId.
    /// </summary>
    public sealed class BuildingRepository
    {
        private readonly CatalogConnectionFactory _connectionFactory;

        public BuildingRepository(CatalogConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Adds a new building. Throws InvalidOperationException if a building with
        /// the same name already exists (the schema enforces this via UNIQUE, this
        /// wraps the SqliteException into a clearer message for the caller, typically
        /// the setup WPF dialog).
        /// </summary>
        public Building AddBuilding(string buildingName, string centralModelPath)
        {
            if (string.IsNullOrWhiteSpace(buildingName))
            {
                throw new ArgumentException("Building name must not be empty.", nameof(buildingName));
            }

            if (string.IsNullOrWhiteSpace(centralModelPath))
            {
                throw new ArgumentException("Central model path must not be empty.", nameof(centralModelPath));
            }

            var nowUtc = DateTime.UtcNow;

            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO Buildings (BuildingName, CentralModelPath, Enabled, CreatedUtc, ModifiedUtc)
                        VALUES ($name, $path, 1, $created, $modified);
                        SELECT last_insert_rowid();";

                    command.Parameters.AddWithValue("$name", buildingName.Trim());
                    command.Parameters.AddWithValue("$path", centralModelPath.Trim());
                    command.Parameters.AddWithValue("$created", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$modified", nowUtc.ToString("o"));

                    try
                    {
                        var newId = Convert.ToInt32(command.ExecuteScalar());
                        return new Building
                        {
                            BuildingId = newId,
                            BuildingName = buildingName.Trim(),
                            CentralModelPath = centralModelPath.Trim(),
                            Enabled = true,
                            CreatedUtc = nowUtc,
                            ModifiedUtc = nowUtc
                        };
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
                    {
                        throw new InvalidOperationException(
                            $"A building named '{buildingName}' already exists in this catalog. " +
                            "Building names must be unique within a project.", ex);
                    }
                }
            });
        }

        /// <summary>
        /// Renames an existing building. BuildingId, not BuildingName, is the stable
        /// identity used by every foreign key in this catalog (FamilyBuildingState,
        /// ExceptionTable, PendingRequests, AuditLog all reference BuildingId), so
        /// renaming is safe and does not require touching any other table. Throws
        /// InvalidOperationException if another building already has the requested
        /// name, BuildingName remains UNIQUE in the schema.
        /// </summary>
        public void UpdateBuildingName(int buildingId, string newBuildingName)
        {
            if (string.IsNullOrWhiteSpace(newBuildingName))
            {
                throw new ArgumentException("Building name must not be empty.", nameof(newBuildingName));
            }

            var nowUtc = DateTime.UtcNow;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE Buildings
                        SET BuildingName = $name, ModifiedUtc = $modified
                        WHERE BuildingId = $id;";

                    command.Parameters.AddWithValue("$name", newBuildingName.Trim());
                    command.Parameters.AddWithValue("$modified", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$id", buildingId);

                    try
                    {
                        var rowsAffected = command.ExecuteNonQuery();
                        if (rowsAffected == 0)
                        {
                            throw new InvalidOperationException($"No building found with BuildingId {buildingId}.");
                        }
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
                    {
                        throw new InvalidOperationException(
                            $"A building named '{newBuildingName}' already exists in this catalog. " +
                            "Building names must be unique within a project.", ex);
                    }
                }
            });
        }

        /// <summary>
        /// Updates the central model path for an existing building. Use this when a
        /// central is relocated on the network, BuildingId stays stable as the real
        /// identity, path is expected to change over the project lifetime.
        /// </summary>
        public void UpdateCentralModelPath(int buildingId, string newCentralModelPath)
        {
            if (string.IsNullOrWhiteSpace(newCentralModelPath))
            {
                throw new ArgumentException("Central model path must not be empty.", nameof(newCentralModelPath));
            }

            var nowUtc = DateTime.UtcNow;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE Buildings
                        SET CentralModelPath = $path, ModifiedUtc = $modified
                        WHERE BuildingId = $id;";

                    command.Parameters.AddWithValue("$path", newCentralModelPath.Trim());
                    command.Parameters.AddWithValue("$modified", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$id", buildingId);

                    var rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected == 0)
                    {
                        throw new InvalidOperationException($"No building found with BuildingId {buildingId}.");
                    }
                }
            });
        }

        /// <summary>
        /// Sets the Enabled flag for a building. Disabled buildings are excluded from
        /// active scanning (Phase 1 detection should check this before processing
        /// DocumentChanged events, and Phase 2's dashboard should filter disabled
        /// buildings out of mismatch queries by default). History (FamilyBuildingState,
        /// AuditLog rows) is preserved, this is a soft toggle, not a delete.
        /// Covers the "option to turn this off for current model" requirement.
        /// </summary>
        public void SetEnabled(int buildingId, bool enabled)
        {
            var nowUtc = DateTime.UtcNow;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE Buildings
                        SET Enabled = $enabled, ModifiedUtc = $modified
                        WHERE BuildingId = $id;";

                    command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
                    command.Parameters.AddWithValue("$modified", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$id", buildingId);

                    var rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected == 0)
                    {
                        throw new InvalidOperationException($"No building found with BuildingId {buildingId}.");
                    }
                }
            });
        }

        /// <summary>
        /// Returns all buildings in the catalog, including disabled ones. Callers that
        /// only want active buildings should filter on Enabled, kept explicit at the
        /// call site rather than hidden behind an "active only" overload, so it is
        /// always visually obvious in calling code whether disabled buildings are
        /// included.
        /// </summary>
        public List<Building> GetAllBuildings()
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<Building>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT BuildingId, BuildingName, CentralModelPath, Enabled, CreatedUtc, ModifiedUtc
                        FROM Buildings
                        ORDER BY BuildingName;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapBuilding(reader));
                        }
                    }
                }

                return results;
            });
        }

        /// <summary>
        /// Looks up a single building by its current central model path. Intended for
        /// use by the DocumentChanged hook running inside a given building's session:
        /// on add-in startup, resolve "which Building row am I" by matching the open
        /// Document's PathName against CentralModelPath, rather than requiring the
        /// user to manually select their building identity every session. Comparison
        /// is case-insensitive since Windows file paths are not case-sensitive on
        /// typical local/UNC server configurations.
        /// </summary>
        public Building? FindByCentralModelPath(string centralModelPath)
        {
            if (string.IsNullOrWhiteSpace(centralModelPath))
            {
                return null;
            }

            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT BuildingId, BuildingName, CentralModelPath, Enabled, CreatedUtc, ModifiedUtc
                        FROM Buildings
                        WHERE CentralModelPath = $path COLLATE NOCASE;";

                    command.Parameters.AddWithValue("$path", centralModelPath.Trim());

                    using (var reader = command.ExecuteReader())
                    {
                        return reader.Read() ? MapBuilding(reader) : null;
                    }
                }
            });
        }

        public Building? GetById(int buildingId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT BuildingId, BuildingName, CentralModelPath, Enabled, CreatedUtc, ModifiedUtc
                        FROM Buildings
                        WHERE BuildingId = $id;";

                    command.Parameters.AddWithValue("$id", buildingId);

                    using (var reader = command.ExecuteReader())
                    {
                        return reader.Read() ? MapBuilding(reader) : null;
                    }
                }
            });
        }

        private static Building MapBuilding(SqliteDataReader reader)
        {
            return new Building
            {
                BuildingId = reader.GetInt32(reader.GetOrdinal("BuildingId")),
                BuildingName = reader.GetString(reader.GetOrdinal("BuildingName")),
                CentralModelPath = reader.GetString(reader.GetOrdinal("CentralModelPath")),
                Enabled = reader.GetInt32(reader.GetOrdinal("Enabled")) == 1,
                CreatedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedUtc"))).ToUniversalTime(),
                ModifiedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("ModifiedUtc"))).ToUniversalTime()
            };
        }
    }

    // NOTE ON SCOPE: this repository assumes a setup screen exists somewhere for adding
    // buildings, setting their paths, and toggling Enabled, that screen is not part of
    // Phase 1 as scoped (catalog + DocumentChanged hook + per-building confirm dialog).
    // FindByCentralModelPath above is how Phase 1's hook will resolve building identity
    // automatically without needing that setup screen to be used interactively every
    // session, but at least one building row must exist with a path matching the open
    // Document for that resolution to succeed. Flagging this as a real Phase 1
    // dependency: a minimal "Add Building" capability (even a bare-bones one, full
    // setup UI can come later) needs to exist before the DocumentChanged hook can
    // identify which building it's running in.
}
