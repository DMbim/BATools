using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Data
{
    /// <summary>
    /// Data access for AuditLog: the append-only trail of every meaningful catalog
    /// event. Unlike FamilyBuildingState (which holds only the latest snapshot per
    /// family/building pair), this table never updates existing rows, only inserts.
    /// This is the source of truth for "what actually happened and when" if a dispute
    /// arises during a crunch about who loaded what and whether it was confirmed,
    /// overridden, or flagged as an exception.
    ///
    /// Every other repository's write operations should be paired with a call to
    /// LogEvent here by the calling code (the DocumentChanged hook, the confirm
    /// dialog's view model, the dashboard's request/resolve handlers). This repository
    /// deliberately does not call itself automatically from inside the other
    /// repositories, keeping audit logging an explicit, visible step at each call site
    /// rather than a hidden side effect, so it is always obvious from reading calling
    /// code whether a given action is being audited.
    /// </summary>
    public sealed class AuditLogRepository
    {
        private readonly CatalogConnectionFactory _connectionFactory;

        public AuditLogRepository(CatalogConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Appends a new audit entry. familyId may be null only for events with no
        /// natural family association, none of the currently defined event types
        /// (see AuditEventType) qualify, this parameter is nullable to match the
        /// schema's allowance for a future event type that is not family-specific,
        /// not because any current call site should pass null.
        /// </summary>
        public void LogEvent(
            int? familyId,
            int buildingId,
            string eventType,
            string eventUser,
            string? detail,
            string? diffSummary = null)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Event type must not be empty.", nameof(eventType));

            if (string.IsNullOrWhiteSpace(eventUser))
                throw new ArgumentException("Event user must not be empty.", nameof(eventUser));

            var nowUtc = DateTime.UtcNow;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO AuditLog
                            (FamilyId, BuildingId, EventType, EventUtc, EventUser, Detail, DiffSummary)
                        VALUES
                            ($familyId, $buildingId, $eventType, $eventUtc, $eventUser, $detail, $diffSummary);";

                    command.Parameters.AddWithValue("$familyId", (object?)familyId ?? DBNull.Value);
                    command.Parameters.AddWithValue("$buildingId", buildingId);
                    command.Parameters.AddWithValue("$eventType", eventType.Trim());
                    command.Parameters.AddWithValue("$eventUtc", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$eventUser", eventUser.Trim());
                    command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
                    command.Parameters.AddWithValue("$diffSummary", (object?)diffSummary ?? DBNull.Value);

                    command.ExecuteNonQuery();
                }
            });
        }

        /// <summary>
        /// Returns audit history for a specific family, most recent first, optionally
        /// limited to a maximum row count (default 200, this table grows unbounded
        /// over a project's life and an unfiltered full-history query for a frequently
        /// touched family is not something the dashboard should ever issue without a
        /// limit).
        /// </summary>
        public List<AuditLogEntry> GetHistoryForFamily(int familyId, int maxRows = 200)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<AuditLogEntry>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT AuditId, FamilyId, BuildingId, EventType, EventUtc, EventUser, Detail
                        FROM AuditLog
                        WHERE FamilyId = $familyId
                        ORDER BY EventUtc DESC
                        LIMIT $maxRows;";

                    command.Parameters.AddWithValue("$familyId", familyId);
                    command.Parameters.AddWithValue("$maxRows", maxRows);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapEntry(reader));
                        }
                    }
                }

                return results;
            });
        }

        /// <summary>
        /// Returns the most recent audit entries across the whole catalog, used for a
        /// general activity feed view in the coordination dashboard. maxRows defaults
        /// to 200 for the same reason as GetHistoryForFamily.
        /// </summary>
        public List<AuditLogEntry> GetRecent(int maxRows = 200)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<AuditLogEntry>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT AuditId, FamilyId, BuildingId, EventType, EventUtc, EventUser, Detail
                        FROM AuditLog
                        ORDER BY EventUtc DESC
                        LIMIT $maxRows;";

                    command.Parameters.AddWithValue("$maxRows", maxRows);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapEntry(reader));
                        }
                    }
                }

                return results;
            });
        }

        private static AuditLogEntry MapEntry(SqliteDataReader reader)
        {
            var familyIdOrdinal = reader.GetOrdinal("FamilyId");
            var detailOrdinal = reader.GetOrdinal("Detail");

            // DiffSummary column was added in v3. Use GetOrdinal with a try/catch
            // as a safety net when reading databases that may still be mid-migration,
            // though in practice EnsureSchema guarantees the column exists before
            // any connection is opened to the application layer.
            int diffSummaryOrdinal;
            try { diffSummaryOrdinal = reader.GetOrdinal("DiffSummary"); }
            catch { diffSummaryOrdinal = -1; }

            return new AuditLogEntry
            {
                AuditId = reader.GetInt32(reader.GetOrdinal("AuditId")),
                FamilyId = reader.IsDBNull(familyIdOrdinal) ? (int?)null : reader.GetInt32(familyIdOrdinal),
                BuildingId = reader.GetInt32(reader.GetOrdinal("BuildingId")),
                EventType = reader.GetString(reader.GetOrdinal("EventType")),
                EventUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("EventUtc"))).ToUniversalTime(),
                EventUser = reader.GetString(reader.GetOrdinal("EventUser")),
                Detail = reader.IsDBNull(detailOrdinal) ? null : reader.GetString(detailOrdinal),
                DiffSummary = diffSummaryOrdinal >= 0 && !reader.IsDBNull(diffSummaryOrdinal)
                    ? reader.GetString(diffSummaryOrdinal)
                    : null
            };
        }
    }
}
