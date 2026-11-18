// File: BATools-Installer/App.xaml.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BATools_Installer
{
    public partial class App : Application
    {
        private static readonly string TempRunRoot =
            Path.Combine(Path.GetTempPath(), "BATools_Installer_run");

        private IDisposable? _windowLock;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var args = InstallerArgs.Parse(Environment.GetCommandLineArgs());

            if (args.WaitAndCheck)
            {
                // The waiter runs from the install folder and does nothing visible until Revit exits.
                _ = RunWaitAndCheckAsync(args);
                return;
            }

            // A running exe cannot be overwritten or deleted, so when started from the Revit
            // add ins folder, run from a temp copy instead. That lets an update replace the
            // installer exe and lets an uninstall delete the whole install folder.
            if (!args.Relaunched
                && IsRunningFromAddinsFolder()
                && TryStartTempCopy(Environment.GetCommandLineArgs().Skip(1)))
            {
                Shutdown();
                return;
            }

            CleanupOldTempRuns();

            if (args.Source != LaunchSource.Direct)
            {
                // One updater window per Revit year. A second ribbon click or a waiter that fires
                // while a window is open just exits.
                _windowLock = InstallLock.TryAcquire("window", args.RevitYear);
                if (_windowLock == null)
                {
                    Shutdown();
                    return;
                }
            }

            if (args.Silent)
            {
                _ = RunSilentAndShutdownAsync(args);
                return;
            }

            ShowMainWindow(args);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _windowLock?.Dispose(); } catch { }
            base.OnExit(e);
        }

        private void ShowMainWindow(InstallerArgs args)
        {
            var wnd = new MainWindow(args);
            MainWindow = wnd;
            wnd.Show();
        }

        /// <summary>
        /// Hidden waiter. Waits for the Revit process to exit, then does ONE live GitHub check.
        /// Opens the updater window only if the latest version is newer than what is installed.
        /// Always writes installer.log so a "nothing happened" report has something to inspect.
        /// </summary>
        private async Task RunWaitAndCheckAsync(InstallerArgs args)
        {
            var logPath = GetLogPath();
            void log(string s) { try { AppendLog(logPath, s); } catch { } }
            void exit() => Dispatcher.Invoke(() => Shutdown());

            try
            {
                log("=== BATools Installer (waiter) ===");
                log($"Revit: {args.RevitYear}, WaitPid: {args.WaitPid}");

                if (args.WaitPid > 0)
                {
                    try
                    {
                        var p = Process.GetProcessById(args.WaitPid);
                        log($"Waiting for Revit process {args.WaitPid} to exit...");
                        await Task.Run(() => p.WaitForExit()).ConfigureAwait(false);
                        log("Revit process exited.");
                    }
                    catch
                    {
                        log($"Revit process {args.WaitPid} was already gone. Continuing.");
                    }
                }

                if (InstallLock.IsHeld("install", args.RevitYear) || InstallLock.IsHeld("window", args.RevitYear))
                {
                    log("An installer or updater window is already active for this Revit year. Nothing to do.");
                    exit();
                    return;
                }

                var installedRaw = VersionHelper.ReadInstalledRaw(args.RevitYear);
                if (!VersionHelper.TryParseLoose(installedRaw, out var installedVersion))
                {
                    log($"Could not read the installed version (raw: '{installedRaw ?? "(missing)"}'). Treating it as 0.0.0.");
                    installedVersion = new Version(0, 0, 0);
                }
                else
                {
                    log($"Installed version: {installedVersion}");
                }

                string? latestTag;
                try
                {
                    var client = new GitHubReleaseClient(InstallerConfig.RepoOwner, InstallerConfig.RepoName);
                    latestTag = await TaskTimeout
                        .WithTimeoutAsync(client.GetLatestReleaseTagAsync(), TimeSpan.FromSeconds(30))
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log("Update check failed: " + ex.Message);
                    log("=== DONE (check failed, no window shown) ===");
                    exit();
                    return;
                }

                if (!VersionHelper.TryParseLoose(latestTag, out var latestVersion))
                {
                    log($"Could not parse the latest release tag ('{latestTag}'). No update.");
                    log("=== DONE (no update) ===");
                    exit();
                    return;
                }

                log($"Latest GitHub version: {latestVersion} (tag: {latestTag})");

                if (latestVersion.CompareTo(installedVersion) <= 0)
                {
                    log("Installed version is up to date. No window shown.");
                    log("=== DONE (no update) ===");
                    exit();
                    return;
                }

                if (InstallLock.IsHeld("install", args.RevitYear) || InstallLock.IsHeld("window", args.RevitYear))
                {
                    log("An installer or updater window became active while checking. Nothing to do.");
                    exit();
                    return;
                }

                log($"Newer version available ({latestVersion} > {installedVersion}). Opening the updater window.");

                var windowArgs = new[]
                {
                    "--afterclose",
                    "--revit", args.RevitYear.ToString(),
                    "--tag", latestTag!
                };

                if (TryStartTempCopy(windowArgs))
                {
                    log("Updater window started from a temporary copy.");
                    exit();
                    return;
                }

                log("Could not start a temporary copy. Showing the window from the install folder instead.");

                var inPlaceArgs = new InstallerArgs
                {
                    Source = LaunchSource.AfterClose,
                    RevitYear = args.RevitYear,
                    Tag = latestTag,
                    AssetName = $"BA_R{(args.RevitYear % 100):00}.zip"
                };

                Dispatcher.Invoke(() =>
                {
                    _windowLock = InstallLock.TryAcquire("window", inPlaceArgs.RevitYear);
                    if (_windowLock == null)
                    {
                        Shutdown();
                        return;
                    }

                    ShowMainWindow(inPlaceArgs);
                });
            }
            catch (Exception ex)
            {
                log("FATAL in waiter: " + ex);
                exit();
            }
        }

        private async Task RunSilentAndShutdownAsync(InstallerArgs args)
        {
            var logPath = GetLogPath();
            void log(string s) { try { AppendLog(logPath, s); } catch { } }

            try
            {
                log("=== BATools Installer (silent) ===");
                await InstallerRunner.RunAsync(args, log).ConfigureAwait(false);
                log("=== DONE ===");
            }
            catch (Exception ex)
            {
                log("FATAL: " + ex);
            }
            finally
            {
                Dispatcher.Invoke(() => Shutdown());
            }
        }

        // Temp copy helpers

        private static bool IsRunningFromAddinsFolder()
        {
            try
            {
                var self = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(self))
                    return false;

                var selfDir = Path.GetDirectoryName(self) ?? "";
                var addinsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "Revit", "Addins");

                return selfDir.StartsWith(addinsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Copies this exe to a fresh temp folder and starts it there with the given arguments
        /// plus --relaunched. Returns false if anything fails, so the caller can fall back to
        /// running in place.
        /// </summary>
        private static bool TryStartTempCopy(IEnumerable<string> arguments)
        {
            try
            {
                var self = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(self) || !File.Exists(self))
                    return false;

                var runDir = Path.Combine(TempRunRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(runDir);

                var tempExe = Path.Combine(runDir, Path.GetFileName(self));
                File.Copy(self, tempExe, overwrite: true);

                var psi = new ProcessStartInfo
                {
                    FileName = tempExe,
                    WorkingDirectory = runDir,
                    UseShellExecute = false
                };

                foreach (var a in arguments)
                    psi.ArgumentList.Add(a);

                psi.ArgumentList.Add("--relaunched");

                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CleanupOldTempRuns()
        {
            try
            {
                if (!Directory.Exists(TempRunRoot))
                    return;

                foreach (var dir in Directory.GetDirectories(TempRunRoot))
                {
                    try
                    {
                        if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) > TimeSpan.FromDays(1))
                            Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // still running or locked, try again next time
                    }
                }
            }
            catch { }
        }

        // Logging

        private static string GetLogPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(root, "BA", "BATools");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "installer.log");
        }

        private static void AppendLog(string path, string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
    }
}