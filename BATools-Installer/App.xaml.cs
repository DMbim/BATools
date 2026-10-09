// File: BATools-Installer/App.xaml.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BATools_Installer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var args = InstallerArgs.Parse(Environment.GetCommandLineArgs());

            if (args.WaitAndCheck)
            {
                _ = RunWaitAndCheckAsync(args);
                return;
            }

            if (args.Silent)
            {
                _ = RunSilentAndShutdownAsync(args);
                return;
            }

            var wnd = new MainWindow(args);
            MainWindow = wnd;
            wnd.Show();
        }

        /// <summary>
        /// Waits for the given Revit process to exit, then does ONE live GitHub check right
        /// at that moment. Only opens the installer window (and runs an Update) if the
        /// result is strictly newer than what's currently installed for this Revit year.
        /// Otherwise exits quietly with no window. Always logs to installer.log so a "nothing
        /// happened" report has something to look at afterward.
        /// </summary>
        private async Task RunWaitAndCheckAsync(InstallerArgs args)
        {
            var logPath = GetSilentLogPath();
            void log(string s) => AppendLog(logPath, s);

            try
            {
                log("=== BATools Installer (wait-and-check) ===");
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

                var installDir = RevitInstallPaths.GetInstallDir(args.RevitYear);
                var versionFile = Path.Combine(installDir, "BATools.version");
                string? installedRaw = File.Exists(versionFile)
                    ? File.ReadAllText(versionFile).Trim()
                    : null;

                if (!TryParseLoose(installedRaw, out var installedVersion))
                {
                    log($"Could not read/parse installed version from '{versionFile}' " +
                        $"(raw: '{installedRaw ?? "(missing)"}'). Treating as 0.0.0.");
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
                    latestTag = await client.GetLatestReleaseTagAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log("Update check failed (network/GitHub error): " + ex.Message);
                    log("=== DONE (check failed, no window shown) ===");
                    Dispatcher.Invoke(() => Shutdown());
                    return;
                }

                if (string.IsNullOrWhiteSpace(latestTag) || !TryParseLoose(latestTag, out var latestVersion))
                {
                    log($"Could not parse latest release tag ('{latestTag}'). No update.");
                    log("=== DONE (no update) ===");
                    Dispatcher.Invoke(() => Shutdown());
                    return;
                }

                log($"Latest GitHub version: {latestVersion} (tag: {latestTag})");

                if (latestVersion.CompareTo(installedVersion) <= 0)
                {
                    log("Installed version is up to date. No window shown.");
                    log("=== DONE (no update) ===");
                    Dispatcher.Invoke(() => Shutdown());
                    return;
                }

                log($"Newer version available ({latestVersion} > {installedVersion}). Opening installer window.");

                var updateArgs = new InstallerArgs
                {
                    Mode = InstallerMode.Update,
                    RevitYear = args.RevitYear,
                    AssetName = $"BA_R{(args.RevitYear % 100):00}.zip",
                    AssetUrl = null, // resolved fresh by InstallerRunner against the latest release
                    Tag = latestTag,
                    WaitPid = 0, // already waited above
                    Silent = false
                };

                Dispatcher.Invoke(() =>
                {
                    var wnd = new MainWindow(updateArgs);
                    MainWindow = wnd;
                    wnd.Show();
                });
            }
            catch (Exception ex)
            {
                try { AppendLog(GetSilentLogPath(), "FATAL in wait-and-check: " + ex); } catch { }
                Dispatcher.Invoke(() => Shutdown());
            }
        }

        private async Task RunSilentAndShutdownAsync(InstallerArgs args)
        {
            try
            {
                var logPath = GetSilentLogPath();
                void log(string s) => AppendLog(logPath, s);

                log("=== BATools Installer (silent) ===");
                await InstallerRunner.RunAsync(args, log).ConfigureAwait(false);
                log("=== DONE ===");
            }
            catch (Exception ex)
            {
                try
                {
                    var logPath = GetSilentLogPath();
                    AppendLog(logPath, "FATAL: " + ex);
                }
                catch { }
            }
            finally
            {
                Dispatcher.Invoke(() => Shutdown());
            }
        }

        private static string GetSilentLogPath()
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

        /// <summary>
        /// Local copy of the same loose version parsing used by BA.Updates.VersionUtil.
        /// Duplicated deliberately: this installer project does not reference the BA add in
        /// assembly, so there is nothing to share it with without adding a project reference.
        /// If that reference is ever added, consolidate these into one implementation.
        /// </summary>
        private static bool TryParseLoose(string? s, out Version version)
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
            if (parts.Length == 1) core = $"{parts[0]}.0.0";
            else if (parts.Length == 2) core = $"{parts[0]}.{parts[1]}.0";

            return Version.TryParse(core, out version!);
        }
    }
}