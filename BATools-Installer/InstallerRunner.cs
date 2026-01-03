using BA.Installer;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BATools_Installer
{
    internal static class InstallerRunner
    {
        public static async Task RunAsync(InstallerArgs args, Action<string> log)
        {
            log($"Mode: {args.Mode}, Revit: {args.RevitYear}, Asset: {args.AssetName}, WaitPid: {args.WaitPid}, Silent: {args.Silent}");

            if (args.WaitPid > 0)
            {
                await WaitForProcessExit(args.WaitPid, log).ConfigureAwait(false);
            }

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
                    RevitInstallPaths.Uninstall(args.RevitYear);
                    log("Uninstall complete.");
                    break;
            }
        }

        private static async Task InstallOrUpdate(InstallerArgs args, bool isUpdate, Action<string> log)
        {
            var installDir = RevitInstallPaths.GetInstallDir(args.RevitYear);
            var manifestPath = RevitInstallPaths.GetManifestPath(args.RevitYear);

            log((isUpdate ? "Updating..." : "Installing..."));

            var client = new GitHubReleaseClient(InstallerConfig.RepoOwner, InstallerConfig.RepoName);
            var payloadZip = await client.DownloadLatestAssetToTempAsync(args.AssetName).ConfigureAwait(false);

            var extractedDir = ZipPayload.ExtractToTempFolder(payloadZip);
            Directory.CreateDirectory(installDir);

            if (isUpdate && Directory.Exists(installDir))
            {
                var backupDir = installDir + "_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                log($"Backup to: {backupDir}");
                FileCopy.CopyDirectory(installDir, backupDir, overwrite: true);
            }

            FileCopy.CopyDirectory(extractedDir, installDir, overwrite: true);

            // Copy THIS installer exe into install folder so BA.dll can launch it later
            TryCopySelfToInstallDir(installDir, log);

            ManifestWriter.WriteApplicationAddinManifest(
                manifestPath,
                assemblyPath: Path.Combine(installDir, InstallerConfig.AddinAssemblyName),
                fullClassName: InstallerConfig.AddinFullClassName,
                addinIdGuid: InstallerConfig.AddinIdGuid,
                name: InstallerConfig.AddinName,
                vendorId: InstallerConfig.VendorId,
                vendorDescription: "Bogle Architects - BA Tools"
            );

            log("Done.");
        }

        private static void TryCopySelfToInstallDir(string installDir, Action<string> log)
        {
            try
            {
                var self = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(self) || !File.Exists(self)) return;

                var dst = Path.Combine(installDir, InstallerConfig.InstalledInstallerExeName);
                File.Copy(self, dst, overwrite: true);
                log($"Installer copied to: {dst}");
            }
            catch (Exception ex)
            {
                log("WARN: Failed to copy installer into install dir: " + ex.Message);
            }
        }

        private static async Task WaitForProcessExit(int pid, Action<string> log)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                log($"Waiting for process {pid} to exit...");
                await Task.Run(() => p.WaitForExit()).ConfigureAwait(false);
                log("Process exited. Continuing...");
            }
            catch
            {
                // process already gone
                log($"WaitPid {pid}: process not found (already exited). Continuing...");
            }
        }
    }
}
