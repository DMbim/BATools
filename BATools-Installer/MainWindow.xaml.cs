// File: BATools-Installer/MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace BATools_Installer
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

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
                OnChanged(nameof(RevitVersionSubtitle));
                RefreshCurrentVersion();
            }
        }

        public string RevitVersionSubtitle =>
            $"Install / Update / Uninstall for Revit {SelectedRevitYear} (per-user)";

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

        private readonly InstallerArgs? _startupArgs;

        public MainWindow(InstallerArgs? startupArgs = null)
        {
            InitializeComponent();
            DataContext = this;

            _startupArgs = startupArgs;

            if (_startupArgs != null && _startupArgs.RevitYear > 0)
            {
                // Keep the picker in sync with whatever version BA.dll asked
                // us to target, so the UI reflects reality even if the
                // auto-run below fails and the user has to act manually.
                SelectedRevitYear = _startupArgs.RevitYear;
            }

            // The setter above only fires RefreshCurrentVersion() when the
            // value actually changes, so call it explicitly once to cover
            // the case where startupArgs is null or already matches 2026.
            RefreshCurrentVersion();

            Log("Ready.");
            Log($"Install dir: {RevitInstallPaths.GetInstallDir(SelectedRevitYear)}");
            Log($"Manifest: {RevitInstallPaths.GetManifestPath(SelectedRevitYear)}");

            Loaded += async (_, __) =>
            {
                // Non-blocking: don't hold up the window or the auto-update
                // check below just to hear back from GitHub.
                _ = RefreshAvailableVersionAsync();

                // If BA launched us with update args (interactive), auto-run update immediately
                if (_startupArgs != null && _startupArgs.Mode == InstallerMode.Update)
                {
                    Log("Launched in UPDATE mode from Revit.");
                    await Run(_startupArgs).ConfigureAwait(true);
                }
            };
        }

        private void RefreshCurrentVersion()
        {
            try
            {
                var versionFile = Path.Combine(RevitInstallPaths.GetInstallDir(SelectedRevitYear), "BATools.version");
                CurrentVersion = File.Exists(versionFile)
                    ? $"Current version: {File.ReadAllText(versionFile).Trim()}"
                    : "Current version: not installed";
            }
            catch (Exception ex)
            {
                CurrentVersion = "Current version: unknown";
                Log("WARN: Could not read local version file: " + ex.Message);
            }
        }

        private async Task RefreshAvailableVersionAsync()
        {
            try
            {
                var client = new GitHubReleaseClient(InstallerConfig.RepoOwner, InstallerConfig.RepoName);
                var tag = await client.GetLatestReleaseTagAsync().ConfigureAwait(true);

                AvailableVersion = string.IsNullOrWhiteSpace(tag)
                    ? "Available version: unknown"
                    : $"Available version: {tag.TrimStart('v', 'V')}";
            }
            catch (Exception ex)
            {
                AvailableVersion = "Available version: check failed";
                Log("WARN: Could not check latest release: " + ex.Message);
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var args = new InstallerArgs
            {
                Mode = InstallerMode.Install,
                RevitYear = SelectedRevitYear,
                AssetName = AssetNameFor(SelectedRevitYear),
                WaitPid = 0,
                Silent = false
            };

            await Run(args);
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            var args = new InstallerArgs
            {
                Mode = InstallerMode.Update,
                RevitYear = SelectedRevitYear,
                AssetName = AssetNameFor(SelectedRevitYear),
                WaitPid = 0,
                Silent = false
            };

            await Run(args);
        }

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            var args = new InstallerArgs
            {
                Mode = InstallerMode.Uninstall,
                RevitYear = SelectedRevitYear,
                AssetName = "",
                WaitPid = 0,
                Silent = false
            };

            await Run(args);
        }

        private async Task Run(InstallerArgs args)
        {
            try
            {
                IsBusy = true;
                await InstallerRunner.RunAsync(args, Log).ConfigureAwait(true);
                RefreshCurrentVersion();
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("Revit is still running"))
            {
                // Offer force-kill option
                var result = MessageBox.Show(
                    "Revit did not close automatically.\n\n" +
                    "Click YES to force-close Revit and continue the update.\n" +
                    "Any unsaved work in Revit will be lost.\n\n" +
                    "Click NO to cancel — close Revit manually and try again.",
                    "Revit Still Running",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    Log("Force-closing Revit...");
                    BA.Installer.RevitProcessGuard.ForceKillAll(Log);

                    // Brief pause to let OS release file locks
                    await Task.Delay(2000).ConfigureAwait(true);

                    // Retry the operation
                    Log("Retrying after force-close...");
                    try
                    {
                        await InstallerRunner.RunAsync(args, Log).ConfigureAwait(true);
                        RefreshCurrentVersion();
                    }
                    catch (Exception retryEx)
                    {
                        Log("ERROR: " + retryEx.Message);
                        Log(retryEx.ToString());
                        MessageBox.Show(retryEx.Message, "Installer Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    Log("Update cancelled. Close Revit manually and try again.");
                }
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                Log(ex.ToString());
                MessageBox.Show(ex.Message, "Installer Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

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