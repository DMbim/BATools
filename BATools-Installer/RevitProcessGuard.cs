using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BA.Installer
{
    public static class RevitProcessGuard
    {
        /// <summary>
        /// Running Revit processes for the given year. knownPid (if > 0) is always included,
        /// even if its path cannot be read.
        /// </summary>
        public static List<Process> GetRunningRevitProcesses(int revitYear, int knownPid = 0)
            => Process.GetProcessesByName("Revit")
                      .Where(p => MatchesYear(p, revitYear, knownPid))
                      .ToList();

        public static bool IsRevitRunning(int revitYear, int knownPid = 0)
            => GetRunningRevitProcesses(revitYear, knownPid).Count > 0;

        private static bool MatchesYear(Process p, int revitYear, int knownPid)
        {
            if (knownPid > 0 && p.Id == knownPid)
                return true;

            try
            {
                var path = p.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path))
                    return true; // unknown, treat as a match

                return path.IndexOf($"Revit {revitYear}", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return true; // access denied or process exited, treat as a match
            }
        }

        /// <summary>
        /// Asks every Revit process of this year to close (Revit shows its normal save prompts),
        /// then waits until they are gone, the timeout passes, or ct is cancelled.
        /// Cancellation throws OperationCanceledException. Returns false on timeout.
        /// </summary>
        public static async Task<bool> RequestCloseAndWaitAsync(
            int revitYear, int knownPid, int timeoutMs, Action<string> log, CancellationToken ct)
        {
            var processes = GetRunningRevitProcesses(revitYear, knownPid);
            if (processes.Count == 0) return true;

            log($"Asking {processes.Count} Revit {revitYear} instance(s) to close. Save your work in Revit when prompted.");

            foreach (var p in processes)
            {
                try
                {
                    // CloseMainWindow sends WM_CLOSE, which lets Revit prompt to save.
                    if (!p.CloseMainWindow())
                        log($"WARN: Revit PID {p.Id} did not accept the close request. A dialog may be open in Revit.");
                }
                catch (Exception ex)
                {
                    log($"WARN: Could not send close to PID {p.Id}: {ex.Message}");
                }
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);

                if (!IsRevitRunning(revitYear, knownPid))
                {
                    log("Revit closed.");
                    return true;
                }
            }

            var remaining = GetRunningRevitProcesses(revitYear, knownPid);
            log($"WARN: {remaining.Count} Revit instance(s) still running after the timeout: " +
                string.Join(", ", remaining.Select(p => p.Id)));
            return false;
        }

        /// <summary>
        /// Force kills Revit processes of this year. Only called after the user explicitly
        /// confirms, because unsaved work is lost.
        /// </summary>
        public static void ForceKill(int revitYear, int knownPid, Action<string> log)
        {
            foreach (var p in GetRunningRevitProcesses(revitYear, knownPid))
            {
                try
                {
                    p.Kill();
                    log($"Force closed Revit PID {p.Id}.");
                }
                catch (Exception ex)
                {
                    log($"WARN: Could not kill PID {p.Id}: {ex.Message}");
                }
            }
        }
    }
}