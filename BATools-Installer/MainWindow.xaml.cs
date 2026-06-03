// File: BATools-Installer/MainWindow.xaml.cs
using System;
using System.ComponentModel;
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
            set { _isBusy = value; OnChanged(nameof(IsBusy)); }
        }

        private string _logText = "";
        public string LogText
        {
            get => _logText;
            set { _logText = value; OnChanged(nameof(LogText)); }
        }

        public string GitHubInfo => $"https://github.com/{InstallerConfig.RepoOwner}/{InstallerConfig.RepoName}";
        public int SelectedRevitYear { get; set; } = 2026;


        private readonly InstallerArgs? _startupArgs;

        public MainWindow(InstallerArgs? startupArgs = null)
        {
            InitializeComponent();
            DataContext = this;

            _startupArgs = startupArgs;

            Log("Ready.");
            Log($"Install dir: {RevitInstallPaths.GetInstallDir(SelectedRevitYear)}");
            Log($"Manifest: {RevitInstallPaths.GetManifestPath(SelectedRevitYear)}");

            Loaded += async (_, __) =>
            {
                // If BA launched us with update args (interactive), auto-run update immediately
                if (_startupArgs != null && _startupArgs.Mode == InstallerMode.Update)
                {
                    Log("Launched in UPDATE mode from Revit.");
                    await Run(_startupArgs).ConfigureAwait(true);
                }
            };
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
