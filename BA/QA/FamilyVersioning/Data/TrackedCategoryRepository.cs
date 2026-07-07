using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Data
{
    /// <summary>
    /// Data access for TrackedCategories. Controls which Revit family categories
    /// trigger detection in the DocumentChanged hook. BuiltInCategoryId (the integer
    /// value of Autodesk.Revit.DB.BuiltInCategory) is the authoritative filter key.
    /// CategoryLabel is a cached display name for the UI only, not used for filtering.
    /// </summary>
    public sealed class TrackedCategoryRepository
    {
        private readonly CatalogConnectionFactory _connectionFactory;

        public TrackedCategoryRepository(CatalogConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Returns all tracked categories including disabled ones. The hook should
        /// filter on Enabled = true; this method returns all for the Setup UI which
        /// needs to show disabled categories for the user to re-enable them.
        /// </summary>
        public List<TrackedCategory> GetAll()
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new List<TrackedCategory>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT TrackedCategoryId, BuiltInCategoryId, CategoryLabel, Enabled, CreatedUtc
                        FROM TrackedCategories
                        ORDER BY CategoryLabel;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(MapRow(reader));
                        }
                    }
                }

                return results;
            });
        }

        /// <summary>
        /// Returns only enabled category IDs as a HashSet for O(1) lookup in the
        /// DocumentChanged hook. Called once per session on hook initialization and
        /// cached in FamilyVersioningSession to avoid a database round-trip on every
        /// family change event.
        /// </summary>
        public HashSet<int> GetEnabledBuiltInCategoryIds()
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                var results = new HashSet<int>();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT BuiltInCategoryId
                        FROM TrackedCategories
                        WHERE Enabled = 1;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(reader.GetInt32(0));
                        }
                    }
                }

                return results;
            });
        }

        public bool HasAnyCategories()
        {
            return _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(1) FROM TrackedCategories;";
                    return Convert.ToInt32(command.ExecuteScalar()) > 0;
                }
            });
        }

        /// <summary>
        /// Adds a category. Safe to call with a BuiltInCategoryId that already exists:
        /// INSERT OR IGNORE makes it a no-op in that case so callers do not need to
        /// check for existence first.
        /// </summary>
        public void AddCategory(int builtInCategoryId, string categoryLabel)
        {
            if (string.IsNullOrWhiteSpace(categoryLabel))
                throw new ArgumentException("Category label must not be empty.", nameof(categoryLabel));

            var nowUtc = DateTime.UtcNow;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT OR IGNORE INTO TrackedCategories
                            (BuiltInCategoryId, CategoryLabel, Enabled, CreatedUtc)
                        VALUES
                            ($catId, $label, 1, $created);";

                    command.Parameters.AddWithValue("$catId", builtInCategoryId);
                    command.Parameters.AddWithValue("$label", categoryLabel.Trim());
                    command.Parameters.AddWithValue("$created", nowUtc.ToString("o"));

                    command.ExecuteNonQuery();
                }
            });
        }

        public void SetEnabled(int trackedCategoryId, bool enabled)
        {
            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE TrackedCategories
                        SET Enabled = $enabled
                        WHERE TrackedCategoryId = $id;";

                    command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
                    command.Parameters.AddWithValue("$id", trackedCategoryId);

                    command.ExecuteNonQuery();
                }
            });
        }

        public void RemoveCategory(int trackedCategoryId)
        {
            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "DELETE FROM TrackedCategories WHERE TrackedCategoryId = $id;";

                    command.Parameters.AddWithValue("$id", trackedCategoryId);
                    command.ExecuteNonQuery();
                }
            });
        }

        /// <summary>
        /// Seeds the default category set. Called from the first-run flow in the
        /// Setup command when HasAnyCategories() returns false. The categories list
        /// is built by the command from live Revit document context so labels are
        /// locale-correct. The BuiltInCategoryId values are the stable integer keys.
        /// </summary>
        public void SeedCategories(List<(int BuiltInCategoryId, string Label)> categories)
        {
            if (categories == null || categories.Count == 0) return;

            _connectionFactory.ExecuteWithRetry(connection =>
            {
                using (var tx = connection.BeginTransaction())
                {
                    foreach (var (id, label) in categories)
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = tx;
                            command.CommandText = @"
                                INSERT OR IGNORE INTO TrackedCategories
                                    (BuiltInCategoryId, CategoryLabel, Enabled, CreatedUtc)
                                VALUES
                                    ($catId, $label, 1, $created);";

                            command.Parameters.AddWithValue("$catId", id);
                            command.Parameters.AddWithValue("$label", label);
                            command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("o"));

                            command.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                }
            });
        }

        private static TrackedCategory MapRow(SqliteDataReader reader)
        {
            return new TrackedCategory
            {
                TrackedCategoryId = reader.GetInt32(reader.GetOrdinal("TrackedCategoryId")),
                BuiltInCategoryId = reader.GetInt32(reader.GetOrdinal("BuiltInCategoryId")),
                CategoryLabel = reader.GetString(reader.GetOrdinal("CategoryLabel")),
                Enabled = reader.GetBoolean(reader.GetOrdinal("Enabled")),
                CreatedUtc = reader.GetDateTime(reader.GetOrdinal("CreatedUtc"))
            };
        }
    }
}
