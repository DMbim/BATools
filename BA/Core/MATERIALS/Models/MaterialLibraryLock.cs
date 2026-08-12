// Path: BA\Materials\MaterialLibraryLock.cs
using System;
using System.IO;
using Newtonsoft.Json;
using BA.BAApplication;

namespace BA.Materials
{
    /// <summary>
    /// File-based advisory lock for BA_MaterialLibrary.rvt, written alongside the .rvt
    /// itself as "<library>.lock". This is advisory only, it does not stop Revit from
    /// opening the file, it only stops BA Tools' own save path from clobbering another
    /// user's in-progress edit session. Browsing (read-only, no lock held) is always
    /// permitted regardless of lock state, only OpenForEditing/Save should check it.
    ///
    /// A lock older than StaleAfter is treated as abandoned (crashed session, forgotten
    /// close) and can be force-acquired, with the caller responsible for warning the user.
    /// </summary>
    public sealed class MaterialLibraryLock
    {
        private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

        private readonly string _lockFilePath;
        private bool _ownsLock;

        public MaterialLibraryLock(string libraryRvtPath)
        {
            if (string.IsNullOrWhiteSpace(libraryRvtPath))
                throw new ArgumentException("Library path cannot be null or empty.", nameof(libraryRvtPath));

            _lockFilePath = libraryRvtPath + ".lock";
        }

        private sealed class LockPayload
        {
            public string UserName { get; set; } = string.Empty;
            public string MachineName { get; set; } = string.Empty;
            public int ProcessId { get; set; }
            public DateTime AcquiredAtUtc { get; set; }
        }

        /// <summary>
        /// Result of an acquire attempt. Reason is populated only when Success is false,
        /// and is safe to show directly to the user (no stack traces, no internals).
        /// </summary>
        public sealed class AcquireResult
        {
            public bool Success { get; set; }
            public string Reason { get; set; } = string.Empty;
            public bool WasStaleOverride { get; set; }
        }

        public AcquireResult TryAcquire(bool allowStaleOverride)
        {
            bool overridingStaleLock = false;

            try
            {
                if (File.Exists(_lockFilePath))
                {
                    LockPayload existing = ReadLockPayload(_lockFilePath);

                    if (existing != null)
                    {
                        bool isOwnProcessOnOwnMachine =
                            existing.MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                            && existing.ProcessId == System.Diagnostics.Process.GetCurrentProcess().Id;

                        if (isOwnProcessOnOwnMachine)
                        {
                            // Re-entrant acquire from the same process, e.g. window was
                            // reopened without a clean release. Treat as already owned.
                            _ownsLock = true;
                            return new AcquireResult { Success = true };
                        }

                        TimeSpan age = DateTime.UtcNow - existing.AcquiredAtUtc;
                        bool isStale = age > StaleAfter;

                        if (!isStale)
                        {
                            return new AcquireResult
                            {
                                Success = false,
                                Reason = $"Material library is currently locked for editing by " +
                                         $"{existing.UserName} on {existing.MachineName} " +
                                         $"(since {existing.AcquiredAtUtc.ToLocalTime():g})."
                            };
                        }

                        if (!allowStaleOverride)
                        {
                            return new AcquireResult
                            {
                                Success = false,
                                Reason = $"Material library has a stale lock from {existing.UserName} " +
                                         $"on {existing.MachineName}, last acquired " +
                                         $"{existing.AcquiredAtUtc.ToLocalTime():g}. It appears abandoned " +
                                         "but was not force-cleared."
                            };
                        }
                        // Falls through to WriteLockPayload below, overwriting the stale lock.
                        overridingStaleLock = true;
                    }
                }

                WriteLockPayload();
                _ownsLock = true;

                AppLogger.LogInfo($"BA.Materials: acquired material library lock at {_lockFilePath}" +
                    (overridingStaleLock ? " (overrode stale lock)" : string.Empty));

                return new AcquireResult { Success = true, WasStaleOverride = overridingStaleLock };
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MaterialLibraryLock.TryAcquire", ex);
                return new AcquireResult
                {
                    Success = false,
                    Reason = "Could not access the material library lock file. Check network connectivity to the library path."
                };
            }
        }

        public void Release()
        {
            if (!_ownsLock)
                return;

            try
            {
                if (File.Exists(_lockFilePath))
                {
                    LockPayload existing = ReadLockPayload(_lockFilePath);
                    bool isOwnProcess = existing != null
                        && existing.MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                        && existing.ProcessId == System.Diagnostics.Process.GetCurrentProcess().Id;

                    if (isOwnProcess)
                    {
                        File.Delete(_lockFilePath);
                        AppLogger.LogInfo($"BA.Materials: released material library lock at {_lockFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MaterialLibraryLock.Release", ex);
            }
            finally
            {
                _ownsLock = false;
            }
        }

        private void WriteLockPayload()
        {
            LockPayload payload = new LockPayload
            {
                UserName = Environment.UserName,
                MachineName = Environment.MachineName,
                ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                AcquiredAtUtc = DateTime.UtcNow
            };

            string json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            string tempPath = _lockFilePath + ".tmp";

            File.WriteAllText(tempPath, json);

            if (File.Exists(_lockFilePath))
                File.Delete(_lockFilePath);

            File.Move(tempPath, _lockFilePath);
        }

        private static LockPayload ReadLockPayload(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<LockPayload>(json);
            }
            catch
            {
                // Corrupt or partially-written lock file, treat as unreadable rather than
                // throwing, caller will fall through the "existing == null" branches above
                // is not applicable here since this returns null and the outer method
                // still holds a non-null File.Exists check; a null payload is handled as
                // "could not determine owner", which the outer method treats conservatively.
                return null;
            }
        }
    }
}