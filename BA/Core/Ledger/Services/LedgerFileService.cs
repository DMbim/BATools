using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using BA.BAApplication;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Exclusive-lock access to the shared Type Data Ledger JSON file. Every read-modify-write
    /// cycle happens under a single held FileStream lock (FileShare.None) so it is atomic with
    /// respect to any other Publisher/Subscriber using this same method. Retries with backoff
    /// on IOException/UnauthorizedAccessException, which is what a concurrent lock holder on
    /// the network share looks like from this process's point of view.
    ///
    /// Path and retry tuning now come from BA.Settings.LedgerSettings rather than hardcoded
    /// constants. Call ReloadSettings() after the user saves changes in the settings window.
    /// </summary>
    public static class LedgerFileService
    {
        private static BA.Settings.LedgerSettings _settings = BA.Settings.LedgerSettings.Load();

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static string CurrentLedgerPath => _settings.LedgerFilePath;

        /// <summary>
        /// Re-reads LedgerSettings from disk. Call this after the settings window saves,
        /// so an already-running session picks up the new path/retry values without restart.
        /// </summary>
        public static void ReloadSettings()
        {
            _settings = BA.Settings.LedgerSettings.Load();
        }

        /// <summary>
        /// Opens the ledger under an exclusive lock, invokes <paramref name="mutate"/> with the
        /// deserialized ledger, and writes it back only if <paramref name="mutate"/> returns true.
        /// The lock is held for the entire duration of the callback, so do not show UI (TaskDialog,
        /// PickObject, etc.) from inside <paramref name="mutate"/> or you will block every other
        /// publisher/subscriber on the project for as long as that UI is open.
        /// </summary>
        public static bool OpenAndModify(Func<TypeDataLedger, bool> mutate)
        {
            Exception lastException = null;
            int maxRetries = Math.Max(1, _settings.RetryCount);
            int retryDelayMs = Math.Max(1, _settings.RetryDelayMs);
            string path = _settings.LedgerFilePath;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    EnsureFileExists(path);

                    using (var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.None))
                    {
                        TypeDataLedger ledger = ReadLedger(stream);

                        bool shouldWrite = mutate(ledger);

                        if (shouldWrite)
                        {
                            stream.SetLength(0);
                            stream.Position = 0;
                            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                            {
                                JsonSerializer.Serialize(writer, ledger, SerializerOptions);
                            }
                            stream.Flush();
                        }

                        return shouldWrite;
                    }
                }
                catch (IOException ex)
                {
                    lastException = ex;
                    Thread.Sleep(retryDelayMs * (attempt + 1));
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastException = ex;
                    Thread.Sleep(retryDelayMs * (attempt + 1));
                }
            }

            AppLogger.LogError("LedgerFileService.OpenAndModify: exhausted retries acquiring exclusive lock", lastException);
            throw new IOException($"Could not acquire exclusive access to ledger file after {maxRetries} attempts.", lastException);
        }

        /// <summary>
        /// Read-only convenience wrapper. Still takes the exclusive lock rather than a shared
        /// read lock, since some network filesystems allow a writer to interleave mid-read
        /// under FileShare.Read, which would defeat the point.
        /// </summary>
        public static TypeDataLedger ReadOnly()
        {
            TypeDataLedger result = null;
            OpenAndModify(ledger =>
            {
                result = ledger;
                return false;
            });
            return result;
        }

        public static string BuildKey(string familyName, string typeName)
        {
            return $"{familyName}::{typeName}";
        }

        private static TypeDataLedger ReadLedger(FileStream stream)
        {
            if (stream.Length == 0)
            {
                return new TypeDataLedger();
            }

            stream.Position = 0;
            TypeDataLedger ledger = JsonSerializer.Deserialize<TypeDataLedger>(stream);
            ledger = ledger ?? new TypeDataLedger();
            ledger.NormalizeComparers();
            return ledger;
        }

        private static void EnsureFileExists(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(path))
            {
                var empty = new TypeDataLedger();
                string json = JsonSerializer.Serialize(empty, SerializerOptions);
                File.WriteAllText(path, json);
            }
        }
    }
}
