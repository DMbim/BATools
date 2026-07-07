using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace BA.QA.FamilyVersioning.Data
{
    /// <summary>
    /// Creates and configures SQLite connections against the project catalog database.
    /// Responsible for first-run schema creation and applying the pragmas every
    /// connection in this application needs (foreign keys, WAL mode, busy timeout).
    ///
    /// IMPORTANT DEPLOYMENT NOTE:
    /// Microsoft.Data.Sqlite depends on SQLitePCLRaw, which bundles a native
    /// e_sqlite3.dll. When this assembly is referenced by the Revit add-in project,
    /// confirm the .csproj targets a Windows TFM (e.g. net8.0-windows) so NuGet
    /// resolves the correct runtime asset and copies e_sqlite3.dll next to the add-in
    /// DLL in the output folder. If you see a DllNotFoundException for e_sqlite3 at
    /// runtime inside Revit, this is a deployment/probing issue, not a code bug,
    /// check the add-in's bin folder for the native DLL under a runtimes\win-x64\native
    /// or runtimes\win-x86\native subfolder matching Revit's process bitness (Revit
    /// 2026 is 64-bit only, so win-x64 is what you need).
    ///
    /// CONCURRENCY NOTE:
    /// Up to N building sessions (one per linked central, potentially several users
    /// per central) may write to this catalog concurrently during a submission crunch.
    /// SQLite serializes writers at the database file level. WAL mode allows readers
    /// to proceed concurrently with a single writer, which is the right mode for this
    /// access pattern (frequent small writes from DocumentChanged hooks, frequent reads
    /// from the coordination dashboard). Busy timeout is set per-connection so a writer
    /// that finds the database locked retries internally rather than throwing
    /// immediately; callers should still wrap multi-statement operations in
    /// ExecuteWithRetry for the rare case the busy timeout itself is exceeded under
    /// heavy contention.
    /// </summary>
    public sealed class CatalogConnectionFactory
    {
        private readonly string _databasePath;
        private readonly object _bootstrapLock = new object();
        private bool _schemaVerified;

        /// <summary>
        /// Busy timeout applied to every connection, in milliseconds. If a write
        /// cannot acquire the database lock within this window, SQLite raises
        /// SQLITE_BUSY and Microsoft.Data.Sqlite surfaces it as a SqliteException
        /// with SqliteErrorCode == 5. Five seconds is generous for this workload,
        /// individual writes here are single-row upserts, not bulk operations.
        /// </summary>
        private const int BusyTimeoutMilliseconds = 5000;

        public CatalogConnectionFactory(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("Database path must not be null or empty.", nameof(databasePath));
            }

            _databasePath = databasePath;
        }

        /// <summary>
        /// Absolute path to the catalog database file this factory targets.
        /// </summary>
        public string DatabasePath => _databasePath;

        /// <summary>
        /// Opens a new connection with the standard pragmas applied. Ensures the
        /// schema exists on first call (subsequent calls skip the verification
        /// after the first successful check within this factory instance's lifetime).
        /// Caller is responsible for disposing the returned connection.
        /// </summary>
        public SqliteConnection OpenConnection()
        {
            EnsureSchema();

            var connectionStringBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Default
            };

            var connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
            connection.Open();

            using (var pragmaCommand = connection.CreateCommand())
            {
                // Foreign keys are off by default per-connection in SQLite regardless
                // of whether the schema declares them. Must be set on every connection.
                pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
                pragmaCommand.ExecuteNonQuery();
            }

            using (var pragmaCommand = connection.CreateCommand())
            {
                pragmaCommand.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};";
                pragmaCommand.ExecuteNonQuery();
            }

            return connection;
        }

        /// <summary>
        /// Executes the given action against a fresh connection, retrying on
        /// SQLITE_BUSY (error code 5) and SQLITE_LOCKED (error code 6) up to
        /// maxAttempts times with a short randomized backoff. The per-connection
        /// busy_timeout pragma handles most contention transparently; this retry
        /// wrapper exists for the residual case where contention persists beyond
        /// that window, expected to be rare but realistic during a crunch with
        /// five sessions writing simultaneously.
        /// </summary>
        public void ExecuteWithRetry(Action<SqliteConnection> action, int maxAttempts = 3)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            var attempt = 0;
            var random = new Random();

            while (true)
            {
                attempt++;
                try
                {
                    using (var connection = OpenConnection())
                    {
                        action(connection);
                    }
                    return;
                }
                catch (SqliteException ex) when (IsRetryable(ex) && attempt < maxAttempts)
                {
                    var backoffMilliseconds = random.Next(50, 250) * attempt;
                    Thread.Sleep(backoffMilliseconds);
                }
            }
        }

        /// <summary>
        /// Same as ExecuteWithRetry but for operations that return a value (typically
        /// reads, though writes that need to return a generated ID also use this).
        /// </summary>
        public T ExecuteWithRetry<T>(Func<SqliteConnection, T> func, int maxAttempts = 3)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            var attempt = 0;
            var random = new Random();

            while (true)
            {
                attempt++;
                try
                {
                    using (var connection = OpenConnection())
                    {
                        return func(connection);
                    }
                }
                catch (SqliteException ex) when (IsRetryable(ex) && attempt < maxAttempts)
                {
                    var backoffMilliseconds = random.Next(50, 250) * attempt;
                    Thread.Sleep(backoffMilliseconds);
                }
            }
        }

        private static bool IsRetryable(SqliteException ex)
        {
            // SQLITE_BUSY = 5, SQLITE_LOCKED = 6.
            return ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6;
        }

        /// <summary>
        /// Verifies the schema exists, creating it from the embedded resource script
        /// if this is a first run against this database file. Guarded by a lock plus
        /// a boolean so the (relatively expensive) check only runs once per factory
        /// instance, not on every OpenConnection call. Note this guards against
        /// redundant work WITHIN one process; it does not prevent two different
        /// processes (e.g. two building sessions pointed at a brand new, not-yet-created
        /// catalog path simultaneously) from both attempting creation. The CREATE TABLE
        /// IF NOT EXISTS statements in the schema script are idempotent specifically to
        /// make that race harmless, worst case both run the script and the second one's
        /// statements are no-ops.
        /// </summary>
        private void EnsureSchema()
        {
            if (_schemaVerified)
            {
                return;
            }

            lock (_bootstrapLock)
            {
                if (_schemaVerified)
                {
                    return;
                }

                var directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var connectionStringBuilder = new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                };

                using (var connection = new SqliteConnection(connectionStringBuilder.ConnectionString))
                {
                    connection.Open();

                    using (var walCommand = connection.CreateCommand())
                    {
                        walCommand.CommandText = "PRAGMA journal_mode = WAL;";
                        walCommand.ExecuteNonQuery();
                    }

                    using (var foreignKeyCommand = connection.CreateCommand())
                    {
                        foreignKeyCommand.CommandText = "PRAGMA foreign_keys = ON;";
                        foreignKeyCommand.ExecuteNonQuery();
                    }

                    // Run migrations before the main schema script so that tables
                    // exist before ALTER TABLE statements attempt to modify them,
                    // and so that new installs handled by CREATE TABLE IF NOT EXISTS
                    // in the schema script already have the correct final shape.
                    ApplyMigrations(connection);

                    var schemaScript = LoadEmbeddedSchemaScript();

                    using (var schemaCommand = connection.CreateCommand())
                    {
                        schemaCommand.CommandText = schemaScript;
                        schemaCommand.ExecuteNonQuery();
                    }
                }

                _schemaVerified = true;
            }
        }

        /// <summary>
        /// Applies incremental migrations against an already-open connection.
        /// Each migration is guarded by a version check so it is idempotent: running
        /// this against a fresh database (SchemaVersion table does not yet exist) is
        /// safe because the SELECT returns 0 rows which evaluates to version 0, and
        /// all migrations then run in sequence. Running against a v3 database is also
        /// safe because the version check skips all migrations.
        /// </summary>
        private static void ApplyMigrations(SqliteConnection connection)
        {
            var currentVersion = GetSchemaVersion(connection);

            if (currentVersion < 3)
            {
                using (var tx = connection.BeginTransaction())
                {
                    if (TableExists(connection, "AuditLog") &&
                        !ColumnExists(connection, "AuditLog", "DiffSummary"))
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "ALTER TABLE AuditLog ADD COLUMN DiffSummary TEXT;";
                            cmd.ExecuteNonQuery();
                        }
                    }

                    if (!TableExists(connection, "TrackedCategories"))
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
                                CREATE TABLE TrackedCategories (
                                    TrackedCategoryId  INTEGER PRIMARY KEY AUTOINCREMENT,
                                    BuiltInCategoryId  INTEGER NOT NULL UNIQUE,
                                    CategoryLabel      TEXT    NOT NULL,
                                    Enabled            INTEGER NOT NULL DEFAULT 1,
                                    CreatedUtc         TEXT    NOT NULL
                                );
                                CREATE INDEX IF NOT EXISTS IX_TrackedCategories_Enabled
                                    ON TrackedCategories (Enabled);";
                            cmd.ExecuteNonQuery();
                        }
                    }

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = TableExists(connection, "SchemaVersion")
                            ? "UPDATE SchemaVersion SET Version = 3;"
                            : "CREATE TABLE SchemaVersion (Version INTEGER NOT NULL); INSERT INTO SchemaVersion (Version) VALUES (3);";
                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }
        }

        private static int GetSchemaVersion(SqliteConnection connection)
        {
            if (!TableExists(connection, "SchemaVersion")) return 0;

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Version FROM SchemaVersion LIMIT 1;";
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=$name;";
                cmd.Parameters.AddWithValue("$name", tableName);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({tableName});";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(
                            reader.GetString(reader.GetOrdinal("name")),
                            columnName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Loads CatalogSchema.sql from the assembly's embedded resources. The .csproj
        /// must mark Schema\CatalogSchema.sql as an EmbeddedResource for this to work.
        /// The resource name follows MSBuild's default convention:
        /// {RootNamespace}.{FolderPath with dots}.{FileName}. RootNamespace here is
        /// "BA.UI" (confirmed from the actual .csproj, not "BA", an earlier assumption
        /// that turned out wrong since BA.UI is the project's RootNamespace while
        /// individual C# files use their own namespace declarations independent of it),
        /// so the resource name is BA.UI.BA.QA.FamilyVersioning.Schema.CatalogSchema.sql
        /// given the folder sits at BA.QA.FamilyVersioning\Schema\ relative to the
        /// .csproj. If this project's RootNamespace ever changes, this string must be
        /// updated to match, it is not derived automatically.
        /// </summary>
        private static string LoadEmbeddedSchemaScript()
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string expectedResourceName = "BA.UI.QA.FamilyVersioning.Schema.CatalogSchema.sql";

            var resourceName = expectedResourceName;
            var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                // Exact expected name not found, RootNamespace or folder layout may have
                // changed since this was written. Fall back to finding any embedded
                // resource ending in "CatalogSchema.sql" rather than failing outright,
                // this keeps the module working through a RootNamespace rename and
                // surfaces a clear diagnostic instead of a hard crash if the schema
                // file was simply never marked as an EmbeddedResource at all.
                var candidateName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith("CatalogSchema.sql", StringComparison.OrdinalIgnoreCase));

                if (candidateName != null)
                {
                    resourceName = candidateName;
                    stream = assembly.GetManifestResourceStream(resourceName);
                }
            }

            if (stream == null)
            {
                var availableResources = string.Join(", ", assembly.GetManifestResourceNames());
                throw new InvalidOperationException(
                    $"Embedded schema resource was not found in assembly '{assembly.FullName}'. " +
                    $"Expected '{expectedResourceName}' or any resource ending in 'CatalogSchema.sql'. " +
                    "Confirm Schema\\CatalogSchema.sql has Build Action 'Embedded Resource' in the .csproj " +
                    "(check for an <EmbeddedResource Include=\"...CatalogSchema.sql\" /> entry) and that the " +
                    "file actually exists on disk at the path the .csproj references. " +
                    $"Resources found in assembly: {availableResources}");
            }

            using (stream)
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
