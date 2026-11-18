using BA.Installer;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BATools_Installer
{
    /// <summary>Thrown when Revit could not be closed. The window offers a force close for this case.</summary>
    internal sealed class RevitStillRunningException : InvalidOperationException
    {
        public RevitStillRunningException(string message) : base(message) { }
    }

    internal static class InstallerRunner
    {
        private static readonly TimeSpan RevitCloseTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan InstallLockTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Runs an install, update or uninstall. Order: optional legacy wait, install lock,
        /// ask Revit of this year to close, then the actual work.
        /// ct only interrupts the wait for Revit and the lock, never the file operations.
        /// waitingForRevit tells the window when to show its Cancel button.
        /// </summary>
        public static async Task RunAsync(
            InstallerArgs args,
            Action<string> log,
            CancellationToken ct = default,
            Action<bool>? waitingForRevit = null)
        {
            log($"Mode: {args.Mode}, Revit: {args.RevitYear}, Source: {args.Source}, Asset: {args.AssetName}, " +
                $"Tag: {args.Tag ?? "(none)"}, WaitPid: {args.WaitPid}, RevitPid: {args.RevitPid}, Silent: {args.Silent}");

            if (args.WaitPid > 0)
                await WaitForProcessExit(args.WaitPid, log, ct).ConfigureAwait(false);

            // Take the lock BEFORE closing Revit, so the hidden waiter sees that an installer is
            // active when Revit exits and does not open a second window.
            using var installLock = await InstallLock
                .AcquireAsync("install", args.RevitYear, InstallLockTimeout, log, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Another BA Tools installer is already working on Revit {args.RevitYear}. " +
                    "Please wait for it to finish and try again.");

            await EnsureRevitClosedAsync(args, log, ct, waitingForRevit).ConfigureAwait(false);

            switch (args.Mode)
            {
                case InstallerMode.Install:
                    await InstallOrUpdate(args, isUpdate: false, log).ConfigureAwait(false);
                    break;

                case InstallerMode.Update:
                    await InstallOrUpdate(args, isUpdate: true, log).ConfigureAwait(false);
                    break;

                case InstallerMode.Uninstall:
                    log("Uninstalling...");
                    await UninstallWithRetryAsync(args.RevitYear, log).ConfigureAwait(false);
                    log("Uninstall complete.");
                    break;
            }
        }

        private static async Task EnsureRevitClosedAsync(
            InstallerArgs args, Action<string> log, CancellationToken ct, Action<bool>? waitingForRevit)
        {
            if (!RevitProcessGuard.IsRevitRunning(args.RevitYear, args.RevitPid))
                return;

            waitingForRevit?.Invoke(true);
            try
            {
                var closed = await RevitProcessGuard
                    .RequestCloseAndWaitAsync(args.RevitYear, args.RevitPid,
                        (int)RevitCloseTimeout.TotalMilliseconds, log, ct)
                    .ConfigureAwait(false);

                if (!closed)
                {
                    throw new RevitStillRunningException(
                        $"Revit {args.RevitYear} is still running and could not be closed automatically.");
                }
            }
            finally
            {
                waitingForRevit?.Invoke(false);
            }
        }

        private static async Task InstallOrUpdate(InstallerArgs args, bool isUpdate, Action<string> log)
        {
            string? payloadZip = null;
            string? extractedDir = null;

            try
            {
                var installDir = RevitInstallPaths.GetInstallDir(args.RevitYear);
                var manifestPath = RevitInstallPaths.GetManifestPath(args.RevitYear);

                log(isUpdate ? "Updating..." : "Installing...");

                // Download
                var client = new GitHubReleaseClient(InstallerConfig.RepoOwner, InstallerConfig.RepoName);

                if (!string.IsNullOrWhiteSpace(args.AssetUrl))
                {
                    log($"Using pinned asset URL: {args.AssetUrl}");
                    payloadZip = await client
                        .DownloadAssetUrlToTempAsync(args.AssetUrl, args.AssetName, log)
                        .ConfigureAwait(false);
                }
                else
                {
                    log("Resolving the latest release asset by name.");
                    payloadZip = await client
                        .DownloadLatestAssetToTempAsync(args.AssetName)
                        .ConfigureAwait(false);
                }

                extractedDir = ZipPayload.ExtractToTempFolder(payloadZip);

                Directory.CreateDirectory(installDir);

                // Backup
                if (isUpdate && Directory.Exists(installDir))
                {
                    var backupDir = installDir + "_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    log($"Backup to: {backupDir}");
                    FileCopy.CopyDirectory(installDir, backupDir, overwrite: true);
                }

                // If this process is somehow running from the install folder itself (the temp
                // relaunch failed), the installer exe in the zip, if any, cannot be overwritten.
                var extractedInstaller = Path.Combine(extractedDir, InstallerConfig.InstalledInstallerExeName);
                var targetInstaller = Path.Combine(installDir, InstallerConfig.InstalledInstallerExeName);
                if (File.Exists(extractedInstaller) && IsSameFile(Environment.ProcessPath, targetInstaller))
                {
                    File.Delete(extractedInstaller);
                    log("Skipped the installer exe from the package because it is the file currently running.");
                }

                // Copy new files
                FileCopy.CopyDirectory(extractedDir, installDir, overwrite: true);

                await TryCopySelfToInstallDirAsync(installDir, log).ConfigureAwait(false);

                ManifestWriter.WriteApplicationAddinManifest(
                    manifestPath,
                    assemblyPath: Path.Combine(installDir, InstallerConfig.AddinAssemblyName),
                    fullClassName: InstallerConfig.AddinFullClassName,
                    addinIdGuid: InstallerConfig.AddinIdGuid,
                    name: InstallerConfig.AddinName,
                    vendorId: InstallerConfig.VendorId,
                    vendorDescription: "Bogle Architects - BA Tools");

                IssueReporterSettingsInstaller.InstallOrUpdate(log);
                ContentBrowserSettingsInstaller.InstallOrUpdate(log);

                log("Done.");
            }
            finally
            {
                TryDeleteFile(payloadZip);
                TryDeleteDirectory(extractedDir);
            }
        }

        private static async Task UninstallWithRetryAsync(int revitYear, Action<string> log)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    RevitInstallPaths.Uninstall(revitYear);
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 6)
                {
                    // A hidden waiter from the Revit session that just closed may still hold the
                    // installer exe for a moment.
                    log($"Files are still in use ({ex.Message}). Retrying...");
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
        }

        private static async Task TryCopySelfToInstallDirAsync(string installDir, Action<string> log)
        {
            var self = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(self) || !File.Exists(self))
                return;

            var dst = Path.Combine(installDir, InstallerConfig.InstalledInstallerExeName);

            if (IsSameFile(self, dst))
            {
                log("Installer is already running from the install folder. Nothing to copy.");
                return;
            }

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    File.Copy(self, dst, overwrite: true);
                    log($"Installer copied to: {dst}");
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 3)
                    {
                        log("WARN: Failed to copy installer into install dir: " + ex.Message);
                        return;
                    }

                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
        }

        private static async Task WaitForProcessExit(int pid, Action<string> log, CancellationToken ct)
        {
            Process p;
            try
            {
                p = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                log($"WaitPid {pid}: process not found (already exited). Continuing...");
                return;
            }

            try
            {
                log($"Waiting for process {pid} to exit...");
                await p.WaitForExitAsync(ct).ConfigureAwait(false);
                log("Process exited. Continuing...");
            }
            catch (InvalidOperationException)
            {
                log($"WaitPid {pid}: process already gone. Continuing...");
            }
        }

        private static bool IsSameFile(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteFile(string? path)
        {
            try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string? path)
        {
            try { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
        }
    }
}