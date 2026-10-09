using Autodesk.Revit.UI;
using BA.BAApplication;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BA.Updates
{
    internal static class InstallerLauncher
    {
        /// <summary>
        /// Ensures BATools-Installer.exe exists next to BA.dll, downloading it directly from
        /// the latest GitHub release if it's missing. Runs inside BA.dll (the Revit process),
        /// so it has no dependency on the installer exe already being present — that's what
        /// lets it repair a machine where the exe was never deployed or got deleted.
        ///
        /// Requires BATools-Installer.exe to be uploaded as its OWN release asset on GitHub,
        /// not only bundled inside BA_Rxx.zip. If that asset is missing from the release,
        /// this logs and returns false; it does not throw, since this runs silently at
        /// startup and must never interrupt Revit.
        /// </summary>
        private static async Task<bool> EnsureInstallerExeExistsAsync(string installerPath)
        {
            if (File.Exists(installerPath))
                return true;

            AppLogger.LogInfo($"BATools-Installer.exe missing at '{installerPath}'. Attempting self-heal download.");

            try
            {
                var rel = await GitHubReleaseClientLite.GetLatestReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                if (rel == null)
                {
                    AppLogger.LogInfo("Self-heal failed: could not reach GitHub or no release found.");
                    return false;
                }

                var asset = GitHubReleaseClientLite.FindAsset(rel, UpdateConfig.InstallerExeName);
                if (asset == null || string.IsNullOrWhiteSpace(asset.browser_download_url))
                {
                    AppLogger.LogInfo(
                        $"Self-heal failed: release '{rel.tag_name}' has no '{UpdateConfig.InstallerExeName}' " +
                        "asset. It must be uploaded to the GitHub release as its own file, not only inside BA_Rxx.zip.");
                    return false;
                }

                var dir = Path.GetDirectoryName(installerPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                await GitHubReleaseClientLite
                    .DownloadAssetToFileAsync(asset.browser_download_url!, installerPath, CancellationToken.None)
                    .ConfigureAwait(false);

                AppLogger.LogInfo($"Self-heal succeeded: downloaded '{UpdateConfig.InstallerExeName}' to '{installerPath}'.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("EnsureInstallerExeExistsAsync", ex);
                return false;
            }
        }

        /// <summary>
        /// Ensures the installer exe exists (self-healing it if missing), then launches it in
        /// --waitandcheck mode: it waits for this Revit process to exit, then does its own
        /// live GitHub check at that moment and only opens its window if a newer version
        /// actually exists. This is the sole mechanism for automatic close-time updates.
        /// </summary>
        public static async Task<bool> LaunchWaitAndCheckAsync(UIApplication uiapp)
        {
            var addinAsm = typeof(InstallerLauncher).Assembly;
            var addinDir = Path.GetDirectoryName(addinAsm.Location) ?? "";
            var installerPath = Path.Combine(addinDir, UpdateConfig.InstallerExeName);

            var exists = await EnsureInstallerExeExistsAsync(installerPath).ConfigureAwait(false);
            if (!exists)
            {
                // No dialog: this runs silently at startup. The manual "Check for Updates"
                // ribbon command still surfaces a missing-asset problem explicitly.
                return false;
            }

            var revitPid = Process.GetCurrentProcess().Id;
            var revitVersion = uiapp.Application.VersionNumber; // "2026"

            var args = $"--waitandcheck --revit {revitVersion} --waitpid {revitPid}";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = args,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LaunchWaitAndCheckAsync", ex);
                return false;
            }
        }

        /// <summary>
        /// Not currently called by any automatic path. Kept for a possible future manual
        /// "Update now" action from the ribbon dialog, or for direct testing.
        /// </summary>
        public static bool LaunchUpdate(UpdateCheckResult r, bool silent)
        {
            var addinAsm = typeof(InstallerLauncher).Assembly;
            var addinDir = Path.GetDirectoryName(addinAsm.Location) ?? "";
            var installerPath = Path.Combine(addinDir, UpdateConfig.InstallerExeName);

            if (!File.Exists(installerPath))
            {
                TaskDialog.Show("BA Tools Update",
                    $"Installer EXE not found:\n{installerPath}\n\n" +
                    $"Fix: ensure {UpdateConfig.InstallerExeName} is deployed next to BA.dll.");
                return false;
            }

            var revitPid = Process.GetCurrentProcess().Id;
            var revitVersion = r.RevitVersion ?? "2026";

            var args =
                $"--mode update " +
                $"--revit {revitVersion} " +
                $"--tag \"{r.Tag}\" " +
                $"--asset \"{r.AssetName}\" " +
                $"--assetUrl \"{r.AssetUrl}\" " +
                $"--waitPid {revitPid} " +
                (silent ? "--silent" : "");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = args,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA Tools Update", "Failed to start installer:\n" + ex.Message);
                return false;
            }
        }
    }
}