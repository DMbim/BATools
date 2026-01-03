using Autodesk.Revit.UI;
using System;
using System.Diagnostics;
using System.IO;

namespace BA.Updates
{
    internal static class InstallerLauncher
    {
        public static bool LaunchUpdate(UIApplication uiapp, UpdateCheckResult r, bool silent)
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

            // IMPORTANT: Always wait for Revit to close before touching files
            var revitPid = Process.GetCurrentProcess().Id;

            // Keep args compatible with what you already used in UpdateCoordinator.
            // Your installer must parse these.
            var args =
                $"--mode update " +
                $"--revit {uiapp.Application.VersionNumber} " +
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
