using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Data
{
    /// <summary>
    /// Data access for ExceptionTable: approved, intentional divergences of a family's
    /// state in a specific building from the canonical version (e.g. a fire-rated
    /// variant unique to one building, deliberately different and not a bug).
    ///
    /// The schema enforces at most one ACTIVE exception per (FamilyId, BuildingId) via
    /// a partial unique index (see CatalogSchema.sql, UX_ExceptionTable_OneActive).
    /// That index is the backstop against a logic bug; the correct workflow for
    /// "this family/building pair already has an exception and we need a different
    /// reason now" is to revoke the existing one first, then add a new one, both
    /// inside the same operation, see AddException below, which does exactly that
    /// rather than leaving the caller to orchestrate two separate calls and risk
    /// hitting the constraint mid-sequence.
    /// </summary>
    public sealed class ExceptionRepository
    {
        private readonly CatalogConnectionFactory _connectionFactory;

        public ExceptionRepository(CatalogConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Adds a new active exception for (familyId, buildingId). If an active
        /// exception already exists for this pair, it is revoked first (Active set to
        /// 0) within the same transaction, then the new one is inserted. This means
        /// calling AddException always succeeds in producing exactly one active
        /// exception for the pair, callers do not need to check for an existing one
        /// first, history of the superseded exception is preserved as a revoked row.
        /// </summary>
        public FamilyException AddException(int familyId, int buildingId, string reason, string approvedByUser)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Reason must not be empty. An exception without a documented " +
                    "reason defeats the purpose of the exception table, see prior discussion on exception " +
                    "table entries needing real justification, not silent overrides.", nameof(reason));
            }

            if (string.IsNullOrWhiteSpace(approvedByUser))
            {
                throw new ArgumentException("Approving user must not be empty.", nameof(approvedByUser));
            }

            var nowUtc = DateTime.UtcNow;

            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    using (var revokeCommand = connection.CreateCommand())
                    {
                        revokeCommand.Transaction = transaction;
                        revokeCommand.CommandText = @"
                            UPDATE ExceptionTable
                            SET Active = 0
                            WHERE FamilyId = $familyId AND BuildingId = $buildingId AND Active = 1;";

                        revokeCommand.Parameters.AddWithValue("$familyId", familyId);
                        revokeCommand.Parameters.AddWithValue("$buildingId", buildingId);
                        revokeCommand.ExecuteNonQuery();
                    }

                    int newExceptionId;

                    using (var insertCommand = connection.CreateCommand())
                    {
                        insertCommand.Transaction = transaction;
                        insertCommand.CommandText = @"
                            INSERT INTO ExceptionTable
                                (FamilyId, BuildingId, Reason, ApprovedByUser, CreatedUtc, Active)
                            VALUES
                                ($familyId, $buildingId, $reason, $approvedBy, $created, 1);
                            SELECT last_insert_rowid();";

                        insertCommand.Parameters.AddWithValue("$familyId", familyId);
                        insertCommand.Parameters.AddWithValue("$buildingId", buildingId);
                        insertCommand.Parameters.AddWithValue("$reason", reason.Trim());
                        insertCommand.Parameters.AddWithValue("$approvedBy", approvedByUser.Trim());
                        insertCommand.Parameters.AddWithValue("$created", nowUtc.ToString("o"));

                        newExceptionId = Convert.ToInt32(insertCommand.ExecuteScalar());
                    }

                    transaction.Commit();

                    return new FamilyException
                    {
                        ExceptionId = newExceptionId,
                        FamilyId = familyId,
                        BuildingId = buildingId,
                        Reason = reason.Trim(),
                        ApprovedByUser = approvedByUser.Trim(),
                        CreatedUtc = nowUtc,
                        Active = true
                    };
                }
            });
        }

        /// <summary>
        /// Revokes the active exception, if any, for (familyId, buildingId). Use this
        /// when a previously-approved variant is no longer valid (e.g. the building
        /// was redesigned to match the standard after all). Returns true if a row was
        /// revoked, false if there was no active exception to revoke.
        /// </summary>
        public bool RevokeActiveException(int familyId, int buildingId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE ExceptionTable
                        SET Active = 0
                        WHERE FamilyId = $familyId AND BuildingId = $buildingId AND Active = 1;";

                    command.Parameters.AddWithValue("$familyId", familyId);
                    command.Parameters.AddWithValue("$buildingId", buildingId);

                    return command.ExecuteNonQuery() > 0;
                }
            });
        }

        /// <summary>
        /// Returns the active exception for (familyId, buildingId), or null if the
        /// pair currently has no approved divergence. This is the check the
        /// coordination dashboard's mismatch query must run before flagging a version
        /// difference as drift, see ExistsActiveException for the cheaper boolean form
        /// when the full row is not needed.
        /// </summary>
        public FamilyException? GetActiveException(int familyId, int buildingId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT ExceptionId, FamilyId, BuildingId, Reason, ApprovedByUser, CreatedUtc, Active
                        FROM ExceptionTable
                        WHERE FamilyId = $familyId AND BuildingId = $buildingId AND Active = 1;";

                    command.Parameters.AddWithValue("$familyId", familyId);
                    command.Parameters.AddWithValue("$buildingId", buildingId);

                    using (var reader = command.ExecuteReader())
                    {
                        return reader.Read() ? MapException(reader) : null;
                    }
                }
            });
        }

        public bool ExistsActiveException(int familyId, int buildingId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT COUNT(1)
                        FROM ExceptionTable
                        WHERE FamilyId = $familyId AND BuildingId = $buildingId AND Active = 1;";

                    command.Parameters.AddWithValue("$familyId", familyId);
                    command.Parameters.AddWithValue("$buildingId", buildingId);

                    var count = Convert.ToInt32(command.ExecuteScalar());
                    return count > 0;
                }
            });
        }

        /// <summary>
        /// Returns the full history (active and revoked) of exceptions for a given
        /// family/building pair, newest first. Useful for audit review of why a
        /// variant exists and whether it has changed justification over time.
        /// </summary>
        public List<FamilyException> GetHistory(int familyId, int buildingId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<FamilyException>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT ExceptionId, FamilyId, BuildingId, Reason, ApprovedByUser, CreatedUtc, Active
                        FROM ExceptionTable
                        WHERE FamilyId = $familyId AND BuildingId = $buildingId
                        ORDER BY CreatedUtc DESC;";

                    command.Parameters.AddWithValue("$familyId", familyId);
                    command.Parameters.AddWithValue("$buildingId", buildingId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapException(reader));
                        }
                    }
                }

                return results;
            });
        }

        /// <summary>
        /// Returns all currently active exceptions across the whole catalog. The
        /// coordination dashboard loads this once per refresh and filters mismatches
        /// in memory against it, cheaper than querying ExistsActiveException per row
        /// when scanning the full family list.
        /// </summary>
        public List<FamilyException> GetAllActive()
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<FamilyException>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT ExceptionId, FamilyId, BuildingId, Reason, ApprovedByUser, CreatedUtc, Active
                        FROM ExceptionTable
                        WHERE Active = 1;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapException(reader));
                        }
                    }
                }

                return results;
            });
        }

        private static FamilyException MapException(SqliteDataReader reader)
        {
            return new FamilyException
            {
                ExceptionId = reader.GetInt32(reader.GetOrdinal("ExceptionId")),
                FamilyId = reader.GetInt32(reader.GetOrdinal("FamilyId")),
                BuildingId = reader.GetInt32(reader.GetOrdinal("BuildingId")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                ApprovedByUser = reader.GetString(reader.GetOrdinal("ApprovedByUser")),
                CreatedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedUtc"))).ToUniversalTime(),
                Active = reader.GetInt32(reader.GetOrdinal("Active")) == 1
            };
        }
    }
}
