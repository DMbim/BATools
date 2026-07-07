using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Data
{
    /// <summary>
    /// Data access for PendingRequests: the queue of "please reload family X to
    /// version Y" requests raised from the coordination dashboard (Phase 2/3) and
    /// fulfilled later, inside the target building's own session (Phase 3).
    ///
    /// CRITICAL ARCHITECTURE NOTE, see prior discussion: nothing in this repository,
    /// or anywhere in this module, opens or writes to a building's central model
    /// directly from outside that building's own session. This repository only ever
    /// reads and writes the catalog database. The actual Document.LoadFamily call
    /// against a target building's central happens in code that runs inside that
    /// building's own Revit session (a ribbon command checking for pending requests
    /// targeting its own BuildingId), which then reports completion back here. This
    /// separation exists specifically to avoid cross-session document writes, which
    /// risk corruption if the target building is open locally by another user at
    /// the time of the write.
    /// </summary>
    public sealed class PendingRequestRepository
    {
        private readonly CatalogConnectionFactory _connectionFactory;

        public PendingRequestRepository(CatalogConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Queues a new reload request. Does not perform any reload, only records the
        /// request. Called from the coordination dashboard when a user selects a
        /// mismatched family and a target building and confirms "request reload."
        /// </summary>
        public PendingRequest CreateRequest(
            int familyId,
            int targetBuildingId,
            string requestedVersion,
            string requestedHash,
            string requestedByUser)
        {
            if (string.IsNullOrWhiteSpace(requestedVersion))
            {
                throw new ArgumentException("Requested version must not be empty.", nameof(requestedVersion));
            }

            if (string.IsNullOrWhiteSpace(requestedByUser))
            {
                throw new ArgumentException("Requesting user must not be empty.", nameof(requestedByUser));
            }

            var nowUtc = DateTime.UtcNow;

            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO PendingRequests
                            (FamilyId, TargetBuildingId, RequestedVersion, RequestedHash,
                             RequestedByUser, RequestedUtc, Status)
                        VALUES
                            ($familyId, $targetBuildingId, $version, $hash, $user, $requestedUtc, 'Pending');
                        SELECT last_insert_rowid();";

                    command.Parameters.AddWithValue("$familyId", familyId);
                    command.Parameters.AddWithValue("$targetBuildingId", targetBuildingId);
                    command.Parameters.AddWithValue("$version", requestedVersion.Trim());
                    command.Parameters.AddWithValue("$hash", requestedHash ?? string.Empty);
                    command.Parameters.AddWithValue("$user", requestedByUser.Trim());
                    command.Parameters.AddWithValue("$requestedUtc", nowUtc.ToString("o"));

                    var newId = Convert.ToInt32(command.ExecuteScalar());

                    return new PendingRequest
                    {
                        RequestId = newId,
                        FamilyId = familyId,
                        TargetBuildingId = targetBuildingId,
                        RequestedVersion = requestedVersion.Trim(),
                        RequestedHash = requestedHash ?? string.Empty,
                        RequestedByUser = requestedByUser.Trim(),
                        RequestedUtc = nowUtc,
                        Status = PendingRequestStatus.Pending
                    };
                }
            });
        }

        /// <summary>
        /// Returns all Pending-status requests targeting a specific building. Called
        /// from inside that building's own session by a "Check Pending Family Updates"
        /// ribbon command (Phase 3), never called cross-session.
        /// </summary>
        public List<PendingRequest> GetPendingForBuilding(int targetBuildingId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<PendingRequest>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT RequestId, FamilyId, TargetBuildingId, RequestedVersion, RequestedHash,
                               RequestedByUser, RequestedUtc, Status, ResolvedByUser, ResolvedUtc, ResolutionNote
                        FROM PendingRequests
                        WHERE TargetBuildingId = $buildingId AND Status = 'Pending'
                        ORDER BY RequestedUtc;";

                    command.Parameters.AddWithValue("$buildingId", targetBuildingId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapRequest(reader));
                        }
                    }
                }

                return results;
            });
        }

        /// <summary>
        /// Marks a request Completed after the target building's own session has
        /// successfully performed the reload locally and synced. resolvedByUser should
        /// be the user who accepted and executed the reload inside that session, which
        /// may differ from requestedByUser, the person who raised the request from the
        /// coordination dashboard.
        /// </summary>
        public void MarkCompleted(int requestId, string resolvedByUser, string? note)
        {
            ResolveRequest(requestId, PendingRequestStatus.Completed, resolvedByUser, note);
        }

        /// <summary>
        /// Marks a request Rejected, used when the building session's user reviews the
        /// request and declines it (for example, recognizing this building intentionally
        /// needs the divergent version and the request should not have been raised, in
        /// which case the rejecting user should also consider adding a formal exception
        /// via ExceptionRepository so the dashboard stops flagging this pair going
        /// forward, rejecting a request alone does not create an exception, those are
        /// deliberately separate actions).
        /// </summary>
        public void MarkRejected(int requestId, string resolvedByUser, string? note)
        {
            ResolveRequest(requestId, PendingRequestStatus.Rejected, resolvedByUser, note);
        }

        private void ResolveRequest(int requestId, PendingRequestStatus status, string resolvedByUser, string? note)
        {
            if (string.IsNullOrWhiteSpace(resolvedByUser))
            {
                throw new ArgumentException("Resolving user must not be empty.", nameof(resolvedByUser));
            }

            var nowUtc = DateTime.UtcNow;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE PendingRequests
                        SET Status = $status,
                            ResolvedByUser = $resolvedBy,
                            ResolvedUtc = $resolvedUtc,
                            ResolutionNote = $note
                        WHERE RequestId = $id AND Status = 'Pending';";

                    command.Parameters.AddWithValue("$status", status.ToString());
                    command.Parameters.AddWithValue("$resolvedBy", resolvedByUser.Trim());
                    command.Parameters.AddWithValue("$resolvedUtc", nowUtc.ToString("o"));
                    command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
                    command.Parameters.AddWithValue("$id", requestId);

                    var rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected == 0)
                    {
                        throw new InvalidOperationException(
                            $"Request {requestId} was not found or was not in Pending status, " +
                            "it may have already been resolved by another session.");
                    }
                }
            });
        }

        /// <summary>
        /// Returns full request history for a family across all buildings, most recent
        /// first. Used by the coordination dashboard to show "is there already a
        /// pending request for this family/building" before allowing a duplicate.
        /// </summary>
        public List<PendingRequest> GetHistoryForFamily(int familyId)
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<PendingRequest>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT RequestId, FamilyId, TargetBuildingId, RequestedVersion, RequestedHash,
                               RequestedByUser, RequestedUtc, Status, ResolvedByUser, ResolvedUtc, ResolutionNote
                        FROM PendingRequests
                        WHERE FamilyId = $familyId
                        ORDER BY RequestedUtc DESC;";

                    command.Parameters.AddWithValue("$familyId", familyId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapRequest(reader));
                        }
                    }
                }

                return results;
            });
        }

        private static PendingRequest MapRequest(SqliteDataReader reader)
        {
            var resolvedByOrdinal = reader.GetOrdinal("ResolvedByUser");
            var resolvedUtcOrdinal = reader.GetOrdinal("ResolvedUtc");
            var noteOrdinal = reader.GetOrdinal("ResolutionNote");

            return new PendingRequest
            {
                RequestId = reader.GetInt32(reader.GetOrdinal("RequestId")),
                FamilyId = reader.GetInt32(reader.GetOrdinal("FamilyId")),
                TargetBuildingId = reader.GetInt32(reader.GetOrdinal("TargetBuildingId")),
                RequestedVersion = reader.GetString(reader.GetOrdinal("RequestedVersion")),
                RequestedHash = reader.GetString(reader.GetOrdinal("RequestedHash")),
                RequestedByUser = reader.GetString(reader.GetOrdinal("RequestedByUser")),
                RequestedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("RequestedUtc"))).ToUniversalTime(),
                Status = Enum.TryParse<PendingRequestStatus>(
                    reader.GetString(reader.GetOrdinal("Status")), out var parsedStatus)
                    ? parsedStatus
                    : PendingRequestStatus.Pending,
                ResolvedByUser = reader.IsDBNull(resolvedByOrdinal) ? null : reader.GetString(resolvedByOrdinal),
                ResolvedUtc = reader.IsDBNull(resolvedUtcOrdinal)
                    ? (DateTime?)null
                    : DateTime.Parse(reader.GetString(resolvedUtcOrdinal)).ToUniversalTime(),
                ResolutionNote = reader.IsDBNull(noteOrdinal) ? null : reader.GetString(noteOrdinal)
            };
        }
    }
}
