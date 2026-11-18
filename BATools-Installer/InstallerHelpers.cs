// File: BATools-Installer/InstallerHelpers.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BATools_Installer
{
    internal static class VersionHelper
    {
        private const string VersionFileName = "BATools.version";

        /// <summary>Raw text of BATools.version for the given Revit year, or null if missing.</summary>
        public static string? ReadInstalledRaw(int revitYear)
        {
            var path = Path.Combine(RevitInstallPaths.GetInstallDir(revitYear), VersionFileName);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }

        /// <summary>
        /// Same loose parsing as BA.Updates.VersionUtil in the add in. Duplicated on purpose,
        /// the installer does not reference the add in assembly.
        /// </summary>
        public static bool TryParseLoose(string? s, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(s)) return false;

            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);

            var firstPart = s.Split('-', '+')[0];
            var core = new string(firstPart.Where(c => char.IsDigit(c) || c == '.').ToArray());
            if (string.IsNullOrWhiteSpace(core)) return false;

            var parts = core.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            if (parts.Length == 1) core = $"{parts[0]}.0.0";
            else if (parts.Length == 2) core = $"{parts[0]}.{parts[1]}.0";

            if (Version.TryParse(core, out var parsed))
            {
                version = parsed;
                return true;
            }

            return false;
        }
    }

    internal static class TaskTimeout
    {
        /// <summary>
        /// Returns the task result, or throws TimeoutException if it does not finish in time.
        /// The underlying request is not cancelled, it is simply no longer awaited.
        /// </summary>
        public static async Task<T> WithTimeoutAsync<T>(Task<T> task, TimeSpan timeout)
        {
            var delay = Task.Delay(timeout);
            var finished = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (finished != task)
                throw new TimeoutException("The request timed out.");

            return await task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Cross process locks based on files opened with FileShare.None and DeleteOnClose.
    /// Unlike a Mutex they have no thread affinity, so they are safe across awaits, and the
    /// operating system releases them if the process dies.
    ///   kind "install": an install, update or uninstall is running for this Revit year.
    ///   kind "window":  an updater window (ribbon or after close) is open for this Revit year.
    /// </summary>
    internal static class InstallLock
    {
        private static string PathFor(string kind, int year)
            => Path.Combine(Path.GetTempPath(), $"BATools_{kind}_{year}.lock");

        public static IDisposable? TryAcquire(string kind, int year)
        {
            try
            {
                return new FileStream(PathFor(kind, year), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static bool IsHeld(string kind, int year)
        {
            var probe = TryAcquire(kind, year);
            if (probe == null) return true;

            probe.Dispose();
            return false;
        }

        public static async Task<IDisposable?> AcquireAsync(
            string kind, int year, TimeSpan timeout, Action<string>? log, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;
            var logged = false;

            while (true)
            {
                var handle = TryAcquire(kind, year);
                if (handle != null)
                    return handle;

                if (!logged)
                {
                    log?.Invoke("Another BA Tools installer is working on this Revit year. Waiting for it to finish...");
                    logged = true;
                }

                if (DateTime.UtcNow >= deadline)
                    return null;

                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }
}