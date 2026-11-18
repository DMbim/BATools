// File: BATools-Installer/MainWindow.xaml.cs
using BA.Installer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BATools_Installer
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private enum UpdateStatus { Checking, Failed, UpToDate, Available }

        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly InstallerArgs _startupArgs;
        private readonly LaunchSource _source;
        private CancellationTokenSource? _runCts;

        // View state
        private bool _isInstalled;
        private Version? _installedVersion;
        private bool _checkDone;
        private string? _latestTag;
        private Version? _latestVersion;
        private bool _revitRunning;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnChanged(nameof(IsBusy));
                OnChanged(nameof(IsNotBusy));
            }
        }

        public bool IsNotBusy => !IsBusy;

        private bool _isWaitingForRevit;
        public bool IsWaitingForRevit
        {
            get => _isWaitingForRevit;
            set { _isWaitingForRevit = value; OnChanged(nameof(IsWaitingForRevit)); }
        }

        private string _logText = "";
        public string LogText
        {
            get => _logText;
            set { _logText = value; OnChanged(nameof(LogText)); }
        }

        public string GitHubInfo => $"https://github.com/{InstallerConfig.RepoOwner}/{InstallerConfig.RepoName}";

        // Years the picker offers. Add a year here when a new Revit release
        // gets a BA Tools build; nothing else in this file needs to change.
        public List<int> AvailableRevitYears { get; } = new List<int> { 2025, 2026 };

        private int _selectedRevitYear = 2026;
        public int SelectedRevitYear
        {
            get => _selectedRevitYear;
            set
            {
                if (_selectedRevitYear == value) return;
                _selectedRevitYear = value;
                OnChanged(nameof(SelectedRevitYear));
                OnChanged(nameof(InstallInfo));
                RefreshLocalState();
            }
        }

        public bool IsYearPickerVisible => _source == LaunchSource.Direct;

        public string RevitVersionSubtitle
        {
            get
            {
                switch (_source)
                {
                    case LaunchSource.AfterClose:
                        return $"A BA Tools update is ready for Revit {SelectedRevitYear}";
                    case LaunchSource.Ribbon:
                        return $"BA Tools for Revit {SelectedRevitYear} (per user)";
                    default:
                        return $"Install / Update / Uninstall for Revit {SelectedRevitYear} (per user)";
                }
            }
        }

        public string InstallInfo =>
            $"Install dir: {RevitInstallPaths.GetInstallDir(SelectedRevitYear)}{Environment.NewLine}" +
            $"Manifest: {RevitInstallPaths.GetManifestPath(SelectedRevitYear)}";

        private string _currentVersion = "Current version: checking...";
        public string CurrentVersion
        {
            get => _currentVersion;
            set { _currentVersion = value; OnChanged(nameof(CurrentVersion)); }
        }

        private string _availableVersion = "Available version: checking...";
        public string AvailableVersion
        {
            get => _availableVersion;
            set { _availableVersion = value; OnChanged(nameof(AvailableVersion)); }
        }

        // Button rules:
        //   not installed            -> Install (Direct only)
        //   installed, newer exists  -> Update only
        //   installed, up to date    -> Uninstall (Direct and Ribbon)
        public bool ShowInstall => _source == LaunchSource.Direct && !_isInstalled;

        public bool ShowUpdate => _isInstalled && GetUpdateStatus() == UpdateStatus.Available;

        public bool ShowUninstall
        {
            get
            {
                if (!_isInstalled || _source == LaunchSource.AfterClose)
                    return false;

                var status = GetUpdateStatus();
                return status == UpdateStatus.UpToDate || status == UpdateStatus.Failed;
            }
        }

        public string StatusText
        {
            get
            {
                string main;

                if (!_isInstalled)
                {
                    main = _source == LaunchSource.Direct
                        ? $"BA Tools is not installed for Revit {SelectedRevitYear}."
                        : $"BA Tools was not found in the install folder for Revit {SelectedRevitYear}.";
                }
                else
                {
                    switch (GetUpdateStatus())
                    {
                        case UpdateStatus.Checking:
                            main = "Checking for updates...";
                            break;
                        case UpdateStatus.Failed:
                            main = "Could not check for updates. Check your connection and reopen this window.";
                            break;
                        case UpdateStatus.UpToDate:
                            main = "BA Tools is up to date.";
                            break;
                        default:
                            main = "A new BA Tools version is available.";
                            break;
                    }
                }

                if (_revitRunning && (ShowInstall || ShowUpdate || ShowUninstall))
                {
                    main += Environment.NewLine +
                            $"Revit {SelectedRevitYear} is open. It will be asked to close first and you will be prompted to save.";
                }

                return main;
            }
        }

        public MainWindow(InstallerArgs? startupArgs = null)
        {
            _startupArgs = startupArgs ?? new InstallerArgs();
            _source = _startupArgs.Source;

            InitializeComponent();
            DataContext = this;

            if (_startupArgs.RevitYear > 0)
            {
                // Keep the picker in sync with the Revit year the caller asked for.
                SelectedRevitYear = _startupArgs.RevitYear;
            }

            // The setter above only refreshes when the value changes, so call it once explicitly.
            RefreshLocalState();

            Log("Ready.");
            Log($"Install dir: {RevitInstallPaths.GetInstallDir(SelectedRevitYear)}");
            Log($"Manifest: {RevitInstallPaths.GetManifestPath(SelectedRevitYear)}");

            // App.xaml sets ShutdownMode="OnExplicitShutdown", so closing the window does not end
            // the process on its own. Every way of closing (X, Alt+F4, Close button) ends up here.
            Closing += MainWindow_Closing;
            Closed += (_, __) => Application.Current.Shutdown();

            Loaded += (_, __) =>
            {
                _ = RefreshAvailableVersionAsync();

                // Legacy: older BA.dll versions and scripts pass an explicit mode and expect it to
                // run immediately. New launches (ribbon, after close, double click) never do.
                if (_source == LaunchSource.Direct && _startupArgs.HasExplicitMode)
                {
                    Log($"Launched with explicit {_startupArgs.Mode} arguments. Running immediately.");
                    _ = Run(_startupArgs);
                }
            };
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Nothing has been changed while only waiting for Revit, so closing is safe then.
            if (!IsBusy || IsWaitingForRevit)
                return;

            var result = MessageBox.Show(
                "An install, update or uninstall is still in progress.\n\n" +
                "Closing now may leave BA Tools partially installed.\n\n" +
                "Close anyway?",
                "Operation In Progress",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                e.Cancel = true;
        }

        // State

        private UpdateStatus GetUpdateStatus()
        {
            if (!_checkDone) return UpdateStatus.Checking;
            if (_latestVersion == null) return UpdateStatus.Failed;

            var installed = _installedVersion ?? new Version(0, 0, 0);
            return _latestVersion.CompareTo(installed) > 0 ? UpdateStatus.Available : UpdateStatus.UpToDate;
        }

        private void RefreshLocalState()
        {
            try
            {
                _isInstalled = RevitInstallPaths.IsInstalled(SelectedRevitYear);

                var raw = VersionHelper.ReadInstalledRaw(SelectedRevitYear);
                _installedVersion = VersionHelper.TryParseLoose(raw, out var v) ? v : null;

                if (raw != null)
                    CurrentVersion = $"Current version: {raw}";
                else
                    CurrentVersion = _isInstalled ? "Current version: unknown" : "Current version: not installed";
            }
            catch (Exception ex)
            {
                CurrentVersion = "Current version: unknown";
                Log("WARN: Could not read local version file: " + ex.Message);
            }

            RefreshViewState();
        }

        private void RefreshViewState()
        {
            try
            {
                _revitRunning = RevitProcessGuard.IsRevitRunning(SelectedRevitYear, _startupArgs.RevitPid);
            }
            catch
            {
                _revitRunning = false;
            }

            OnChanged(nameof(IsYearPickerVisible));
            OnChanged(nameof(RevitVersionSubtitle));
            OnChanged(nameof(ShowInstall));
            OnChanged(nameof(ShowUpdate));
            OnChanged(nameof(ShowUninstall));
            OnChanged(nameof(StatusText));
        }

        private async Task RefreshAvailableVersionAsync()
        {
            // The hidden waiter already looked up the latest tag, so do not ask GitHub again.
            if (_source == LaunchSource.AfterClose && !string.IsNullOrWhiteSpace(_startupArgs.Tag))
            {
                SetLatest(_startupArgs.Tag);
                return;
            }

            try
            {
                var client = new GitHubReleaseClient(InstallerConfig.RepoOwner, InstallerConfig.RepoName);
                var tag = await TaskTimeout
                    .WithTimeoutAsync(client.GetLatestReleaseTagAsync(), TimeSpan.FromSeconds(15))
                    .ConfigureAwait(true);

                SetLatest(tag);
            }
            catch (Exception ex)
            {
                _latestTag = null;
                _latestVersion = null;
                _checkDone = true;
                AvailableVersion = "Available version: check failed";
                Log("WARN: Could not check latest release: " + ex.Message);
                RefreshViewState();
            }
        }

        private void SetLatest(string? tag)
        {
            _checkDone = true;
            _latestTag = tag;
            _latestVersion = VersionHelper.TryParseLoose(tag, out var v) ? v : null;

            AvailableVersion = string.IsNullOrWhiteSpace(tag)
                ? "Available version: unknown"
                : $"Available version: {tag.TrimStart('v', 'V')}";

            RefreshViewState();
        }

        // Buttons

        private InstallerArgs BuildArgs(InstallerMode mode)
        {
            return new InstallerArgs
            {
                Mode = mode,
                RevitYear = SelectedRevitYear,
                AssetName = mode == InstallerMode.Uninstall ? "" : AssetNameFor(SelectedRevitYear),
                Tag = _latestTag,
                RevitPid = _startupArgs.RevitPid,
                WaitPid = 0,
                Silent = false,
                Source = _source
            };
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
            => await Run(BuildArgs(InstallerMode.Install));

        private async void Update_Click(object sender, RoutedEventArgs e)
            => await Run(BuildArgs(InstallerMode.Update));

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
            => await Run(BuildArgs(InstallerMode.Uninstall));

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Log("Cancelling...");
            _runCts?.Cancel();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Close() raises Closing (checked above) then Closed (which shuts the process down).
            Close();
        }

        // Run

        private async Task Run(InstallerArgs args)
        {
            using var cts = new CancellationTokenSource();
            _runCts = cts;

            var completed = false;
            string? failure = null;

            try
            {
                IsBusy = true;
                completed = await RunWithForceKillFallbackAsync(args, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Log("Cancelled. Nothing was changed.");
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                Log(ex.ToString());
                failure = ex.Message;
            }
            finally
            {
                IsBusy = false;
                IsWaitingForRevit = false;
                _runCts = null;
            }

            RefreshLocalState();

            if (failure != null)
            {
                ShowResult("Something went wrong", failure, success: false);
            }
            else if (completed)
            {
                ShowResult(BuildSuccessHeading(args.Mode), BuildSuccessMessage(args.Mode), success: true);

                // Closing the success popup ends the whole installer, not just the popup.
                Application.Current.Shutdown();
            }
        }

        /// <summary>
        /// Runs the operation once. If Revit refuses to close, offers a force close and retries
        /// once. Returns false when the user declines the force close (nothing was changed).
        /// </summary>
        private async Task<bool> RunWithForceKillFallbackAsync(InstallerArgs args, CancellationToken ct)
        {
            try
            {
                await InstallerRunner.RunAsync(args, Log, ct, SetWaitingForRevit).ConfigureAwait(true);
                return true;
            }
            catch (RevitStillRunningException)
            {
                var result = MessageBox.Show(
                    $"Revit {args.RevitYear} did not close.\n\n" +
                    "Click YES to force close it and continue.\n" +
                    "Any unsaved work in Revit will be lost.\n\n" +
                    "Click NO to stop. Close Revit yourself and try again.",
                    "Revit Still Running",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    Log("Stopped. Close Revit manually and try again.");
                    return false;
                }

                Log("Force closing Revit...");
                RevitProcessGuard.ForceKill(args.RevitYear, args.RevitPid, Log);

                // Brief pause to let the operating system release file locks.
                await Task.Delay(2000, ct).ConfigureAwait(true);

                Log("Retrying after force close...");
                await InstallerRunner.RunAsync(args, Log, ct, SetWaitingForRevit).ConfigureAwait(true);
                return true;
            }
        }

        private void SetWaitingForRevit(bool waiting)
        {
            Dispatcher.BeginInvoke(new Action(() => IsWaitingForRevit = waiting));
        }

        // Result popup

        private void ShowResult(string heading, string message, bool success)
        {
            var dlg = new ResultWindow(heading, message, success) { Owner = this };
            dlg.ShowDialog();
        }

        private static string BuildSuccessHeading(InstallerMode mode)
        {
            switch (mode)
            {
                case InstallerMode.Install: return "Installation finished";
                case InstallerMode.Update: return "Update finished";
                default: return "Uninstall finished";
            }
        }

        private string BuildSuccessMessage(InstallerMode mode)
        {
            var year = SelectedRevitYear;

            switch (mode)
            {
                case InstallerMode.Install:
                    return $"BA Tools was installed for Revit {year}.{Environment.NewLine}{Environment.NewLine}You can now start Revit.";

                case InstallerMode.Update:
                    var versionText = _installedVersion != null ? $" to version {_installedVersion}" : "";
                    return $"BA Tools was updated{versionText} for Revit {year}.{Environment.NewLine}{Environment.NewLine}You can now start Revit.";

                default:
                    return $"BA Tools was uninstalled for Revit {year}.";
            }
        }

        // Helpers

        private void Log(string msg)
        {
            LogText += $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}";
        }

        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string AssetNameFor(int revitYear)
        {
            return $"BA_R{(revitYear % 100):00}.zip";
        }
    }
}