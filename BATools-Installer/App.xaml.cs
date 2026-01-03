// File: BATools-Installer/App.xaml.cs
using System;
using System.IO;
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

            if (args.Silent)
            {
                _ = RunSilentAndShutdownAsync(args);
                return;
            }

            var wnd = new MainWindow(args);
            MainWindow = wnd;
            wnd.Show();
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
                // last resort logging
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
    }
}
