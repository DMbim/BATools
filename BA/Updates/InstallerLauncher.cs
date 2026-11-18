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
        // Publish.ps1 refuses to ship an installer under 30 MB, so anything smaller than this
        // is a failed or partial download and must never replace a working exe.
        private const long MinInstallerBytes = 20L * 1024 * 1024;

        private static readonly SemaphoreSlim RepairGate = new SemaphoreSlim(1, 1);

        public static string GetInstallerPath()
        {
            var addinAsm = typeof(InstallerLauncher).Assembly;
            var addinDir = Path.GetDirectoryName(addinAsm.Location) ?? "";
            return Path.Combine(addinDir, UpdateConfig.InstallerExeName);
        }

        public static bool InstallerExists() => File.Exists(GetInstallerPath());

        /// <summary>
        /// Makes sure the installer exe next to BA.dll exists and is not older than this add in.
        ///
        /// Missing: downloaded from the latest GitHub release asset named BATools-Installer.exe.
        /// Outdated: replaced once per add in version. The running exe cannot be deleted or
        /// overwritten, but Windows allows renaming it, so the old file is renamed to a unique
        /// .old name and deleted on a later run. A marker file stops this from repeating if the
        /// installer file version does not track the add in version.
        ///
        /// Runs inside BA.dll, so it has no dependency on the installer already being present.
        /// </summary>
        internal static async Task<bool> EnsureInstallerReadyAsync(string installerPath)
        {
            TryDeleteOldInstallerFiles(installerPath);

            var missing = !File.Exists(installerPath);
            if (!missing && !NeedsRefresh(installerPath))
                return true;

            await RepairGate.WaitAsync().ConfigureAwait(false);
            try
            {
                missing = !File.Exists(installerPath);
                if (!missing && !NeedsRefresh(installerPath))
                    return true;

                AppLogger.LogInfo(missing
                    ? $"Installer missing at '{installerPath}'. Downloading it."
                    : $"Installer at '{installerPath}' is older than this add in. Refreshing it.");

                var newPath = installerPath + ".new";
                var downloaded = await DownloadInstallerAsync(newPath).ConfigureAwait(false);
                if (!downloaded)
                    return !missing;

                SwapInstaller(installerPath, newPath);
                WriteRefreshMarker();

                AppLogger.LogInfo("Installer is now up to date.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("EnsureInstallerReadyAsync", ex);
                return File.Exists(installerPath);
            }
            finally
            {
                RepairGate.Release();
            }
        }

        private static async Task<bool> DownloadInstallerAsync(string newPath)
        {
            var rel = await GitHubReleaseClientLite.GetLatestReleaseAsync(CancellationToken.None).ConfigureAwait(false);
            if (rel == null)
            {
                AppLogger.LogInfo("Installer download skipped: could not reach GitHub.");
                return false;
            }

            var asset = GitHubReleaseClientLite.FindAsset(rel, UpdateConfig.InstallerExeName);
            if (asset == null || string.IsNullOrWhiteSpace(asset.browser_download_url))
            {
                AppLogger.LogInfo(
                    $"Installer download failed: release '{rel.tag_name}' has no '{UpdateConfig.InstallerExeName}' asset.");
                return false;
            }

            await GitHubReleaseClientLite
                .DownloadAssetToFileAsync(asset.browser_download_url!, newPath, CancellationToken.None)
                .ConfigureAwait(false);

            var size = new FileInfo(newPath).Length;
            if (size < MinInstallerBytes)
            {
                AppLogger.LogInfo($"Downloaded installer is only {size} bytes. Discarding it.");
                try { File.Delete(newPath); } catch { }
                return false;
            }

            return true;
        }

        private static void SwapInstaller(string installerPath, string newPath)
        {
            if (File.Exists(installerPath))
            {
                // Rename, never delete: a running exe can be renamed but not deleted or overwritten.
                var oldPath = $"{installerPath}.{Guid.NewGuid():N}.old";
                File.Move(installerPath, oldPath);
            }

            File.Move(newPath, installerPath);
        }

        private static bool NeedsRefresh(string installerPath)
        {
            try
            {
                var expected = GetExpectedInstallerVersion();
                if (File.Exists(GetRefreshMarkerPath(expected)))
                    return false;

                var info = FileVersionInfo.GetVersionInfo(installerPath);
                var installerVersion = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
                return installerVersion < expected;
            }
            catch
            {
                return false;
            }
        }

        private static Version GetExpectedInstallerVersion()
        {
            var v = VersionUtil.GetInstalledVersion(typeof(InstallerLauncher).Assembly);
            return new Version(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
        }

        private static string GetRefreshMarkerPath(Version expected)
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(root, "BA", "BATools");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"installer.refreshed.{expected}");
        }

        private static void WriteRefreshMarker()
        {
            try
            {
                File.WriteAllText(GetRefreshMarkerPath(GetExpectedInstallerVersion()), DateTime.UtcNow.ToString("o"));
            }
            catch { }
        }

        private static void TryDeleteOldInstallerFiles(string installerPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(installerPath);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    return;

                var name = Path.GetFileName(installerPath);
                foreach (var f in Directory.GetFiles(dir, name + ".*.old"))
                {
                    try { File.Delete(f); } catch { /* still running, try next time */ }
                }
            }
            catch { }
        }

        /// <summary>
        /// Called once from the first Idling event. Makes sure the installer exists, then starts
        /// it in waiter mode: it does nothing visible until this Revit process exits, then
        /// checks GitHub and opens its window only if a newer version exists.
        /// revitVersion and revitPid must be read on the Revit UI thread by the caller.
        /// </summary>
        public static async Task<bool> LaunchWaitAndCheckAsync(string revitVersion, int revitPid)
        {
            var installerPath = GetInstallerPath();

            var ready = await EnsureInstallerReadyAsync(installerPath).ConfigureAwait(false);
            if (!ready)
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = $"--waitandcheck --revit {revitVersion} --waitpid {revitPid}",
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
        /// Opens the installer window from the ribbon button. An existing installer is launched
        /// immediately even if outdated, so the click never waits on a large download. Only a
        /// missing installer is downloaded here, which blocks Revit until it finishes.
        /// </summary>
        public static bool TryLaunchRibbonWindow(string revitVersion, out string? error)
        {
            error = null;
            var installerPath = GetInstallerPath();

            try
            {
                if (!File.Exists(installerPath))
                {
                    var ready = Task.Run(() => EnsureInstallerReadyAsync(installerPath)).GetAwaiter().GetResult();
                    if (!ready)
                    {
                        error = "The BA Tools installer is missing and could not be downloaded.\n\n" +
                                $"Make sure the latest GitHub release contains {UpdateConfig.InstallerExeName} " +
                                "and that this computer can reach GitHub.";
                        return false;
                    }
                }

                var revitPid = Process.GetCurrentProcess().Id;

                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = $"--ribbon --revit {revitVersion} --revitpid {revitPid}",
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TryLaunchRibbonWindow", ex);
                error = "Failed to start the installer:\n" + ex.Message;
                return false;
            }
        }
    }
}