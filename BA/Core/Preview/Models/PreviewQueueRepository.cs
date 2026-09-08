// Path: BA/Core/Content/Preview/PreviewQueueRepository.cs
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace BA.Core.Content.Preview
{
    /// <summary>
    /// Reads and writes the library preview backfill queue. Lives as a
    /// dedicated table inside the shared FamilyVersioning SQLite database
    /// file. The connection string is supplied by the caller rather than
    /// assumed here, on purpose, given the settings path mismatch already
    /// found elsewhere between Cmd_LoadedFamilyBrowser and
    /// Cmd_OpenContentBrowserCommand.
    /// </summary>
    public sealed class PreviewQueueRepository
    {
        private readonly string _connectionString;

        public PreviewQueueRepository(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            string? dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
        }

        public void EnsureSchema()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS LibraryPreviewQueue (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FamilyPath TEXT NOT NULL UNIQUE,
                    Status INTEGER NOT NULL,
                    AttemptCount INTEGER NOT NULL DEFAULT 0,
                    LastAttemptUtc TEXT NULL,
                    LastError TEXT NOT NULL DEFAULT '',
                    EnqueuedUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_LibraryPreviewQueue_Status
                    ON LibraryPreviewQueue (Status);";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Adds paths as Pending, or resets an existing row back to Pending
        /// with a fresh attempt count if it already exists. Only call this
        /// with paths the scanner has already determined actually need a
        /// preview, not with every path in the library.
        /// </summary>
        public void UpsertPending(IEnumerable<string> familyPaths)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO LibraryPreviewQueue (FamilyPath, Status, AttemptCount, LastError, EnqueuedUtc)
                VALUES ($path, $status, 0, '', $enqueued)
                ON CONFLICT(FamilyPath) DO UPDATE SET
                    Status = $status,
                    AttemptCount = 0,
                    LastError = '',
                    EnqueuedUtc = $enqueued;";

            var pathParam = cmd.CreateParameter();
            pathParam.ParameterName = "$path";
            cmd.Parameters.Add(pathParam);

            var statusParam = cmd.CreateParameter();
            statusParam.ParameterName = "$status";
            statusParam.Value = (int)PreviewQueueStatus.Pending;
            cmd.Parameters.Add(statusParam);

            var enqueuedParam = cmd.CreateParameter();
            enqueuedParam.ParameterName = "$enqueued";
            cmd.Parameters.Add(enqueuedParam);

            foreach (string path in familyPaths)
            {
                pathParam.Value = path;
                enqueuedParam.Value = DateTime.UtcNow.ToString("o");
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        /// <summary>
        /// Call once at the start of every worker run, before pulling a
        /// batch. Any row still Processing means the previous run's Revit
        /// process died or was killed mid item, since a normal run always
        /// transitions Processing to Success or Failed before moving on.
        /// </summary>
        public void ReconcileStuckProcessingRows(int maxAttemptsBeforeQuarantine)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var stuckIds = new List<(long Id, int AttemptCount)>();

            using (var selectCmd = conn.CreateCommand())
            {
                selectCmd.CommandText = "SELECT Id, AttemptCount FROM LibraryPreviewQueue WHERE Status = $processing;";
                selectCmd.Parameters.AddWithValue("$processing", (int)PreviewQueueStatus.Processing);

                using var reader = selectCmd.ExecuteReader();
                while (reader.Read())
                    stuckIds.Add((reader.GetInt64(0), reader.GetInt32(1)));
            }

            foreach (var (id, attemptCount) in stuckIds)
            {
                int newAttemptCount = attemptCount + 1;

                if (newAttemptCount >= maxAttemptsBeforeQuarantine)
                {
                    UpdateStatus(conn, id, PreviewQueueStatus.Quarantined, newAttemptCount,
                        "Worker process did not return a result for this item across multiple attempts, likely a hang or crash. Needs manual review.");
                }
                else
                {
                    UpdateStatus(conn, id, PreviewQueueStatus.Pending, newAttemptCount, string.Empty);
                }
            }
        }

        public List<PreviewQueueItem> GetNextBatch(int maxCount)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FamilyPath, Status, AttemptCount, LastAttemptUtc, LastError, EnqueuedUtc
                FROM LibraryPreviewQueue
                WHERE Status = $pending
                ORDER BY EnqueuedUtc ASC
                LIMIT $max;";
            cmd.Parameters.AddWithValue("$pending", (int)PreviewQueueStatus.Pending);
            cmd.Parameters.AddWithValue("$max", maxCount);

            var results = new List<PreviewQueueItem>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new PreviewQueueItem
                {
                    Id = reader.GetInt64(0),
                    FamilyPath = reader.GetString(1),
                    Status = (PreviewQueueStatus)reader.GetInt32(2),
                    AttemptCount = reader.GetInt32(3),
                    LastAttemptUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                    LastError = reader.GetString(5),
                    EnqueuedUtc = DateTime.Parse(reader.GetString(6))
                });
            }

            return results;
        }

        public void MarkProcessing(long id)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            UpdateStatus(conn, id, PreviewQueueStatus.Processing, incrementAttemptBy: 0, error: string.Empty);
        }

        public void MarkSuccess(long id)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            UpdateStatus(conn, id, PreviewQueueStatus.Success, incrementAttemptBy: 0, error: string.Empty);
        }

        public void MarkFailed(long id, string error, int maxAttemptsBeforeQuarantine)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            int currentAttempts;
            using (var selectCmd = conn.CreateCommand())
            {
                selectCmd.CommandText = "SELECT AttemptCount FROM LibraryPreviewQueue WHERE Id = $id;";
                selectCmd.Parameters.AddWithValue("$id", id);
                object? value = selectCmd.ExecuteScalar();
                currentAttempts = value == null ? 0 : Convert.ToInt32(value);
            }

            int newAttemptCount = currentAttempts + 1;
            PreviewQueueStatus nextStatus = newAttemptCount >= maxAttemptsBeforeQuarantine
                ? PreviewQueueStatus.Quarantined
                : PreviewQueueStatus.Pending;

            UpdateStatus(conn, id, nextStatus, newAttemptCount, error, isAbsoluteAttemptCount: true);
        }

        private static void UpdateStatus(
            SqliteConnection conn,
            long id,
            PreviewQueueStatus status,
            int incrementAttemptBy,
            string error,
            bool isAbsoluteAttemptCount = false)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = isAbsoluteAttemptCount
                ? @"UPDATE LibraryPreviewQueue
                    SET Status = $status, AttemptCount = $attempts, LastError = $error, LastAttemptUtc = $now
                    WHERE Id = $id;"
                : @"UPDATE LibraryPreviewQueue
                    SET Status = $status, AttemptCount = AttemptCount + $attempts, LastError = $error, LastAttemptUtc = $now
                    WHERE Id = $id;";

            cmd.Parameters.AddWithValue("$status", (int)status);
            cmd.Parameters.AddWithValue("$attempts", incrementAttemptBy);
            cmd.Parameters.AddWithValue("$error", error ?? string.Empty);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }
}