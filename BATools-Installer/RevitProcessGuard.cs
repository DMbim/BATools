using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BA.Installer
{
    public static class RevitProcessGuard
    {
        /// <summary>
        /// Returns all currently running Revit.exe processes.
        /// </summary>
        public static List<Process> GetRunningRevitProcesses()
            => Process.GetProcessesByName("Revit").ToList();

        public static bool IsRevitRunning()
            => GetRunningRevitProcesses().Count > 0;

        /// <summary>
        /// Attempts to gracefully close all Revit instances, then waits
        /// for them to exit. Returns true if all exited within the timeout.
        /// </summary>
        public static async Task<bool> RequestCloseAndWaitAsync(
            int timeoutMs, Action<string> log)
        {
            var processes = GetRunningRevitProcesses();
            if (processes.Count == 0) return true;

            log($"Requesting close of {processes.Count} Revit instance(s)...");

            foreach (var p in processes)
            {
                try
                {
                    // CloseMainWindow sends WM_CLOSE — gives Revit a chance to prompt save
                    p.CloseMainWindow();
                }
                catch (Exception ex)
                {
                    log($"WARN: Could not send close to PID {p.Id}: {ex.Message}");
                }
            }

            // Poll until all exited or timeout
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (!IsRevitRunning())
                {
                    log("All Revit instances closed.");
                    return true;
                }
            }

            // Still running — report which PIDs remain
            var remaining = GetRunningRevitProcesses();
            log($"WARN: {remaining.Count} Revit instance(s) still running after timeout: " +
                string.Join(", ", remaining.Select(p => p.Id)));
            return false;
        }

        /// <summary>
        /// Force-kills all remaining Revit instances.
        /// Only called after user explicitly confirms force-kill.
        /// </summary>
        public static void ForceKillAll(Action<string> log)
        {
            foreach (var p in GetRunningRevitProcesses())
            {
                try
                {
                    p.Kill();
                    log($"Force-killed Revit PID {p.Id}.");
                }
                catch (Exception ex)
                {
                    log($"WARN: Could not kill PID {p.Id}: {ex.Message}");
                }
            }
        }
    }
}