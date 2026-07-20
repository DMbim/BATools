using Autodesk.Revit.UI;
using System;
using System.Diagnostics;
using System.IO;

namespace BA.Updates
{
    internal static class InstallerLauncher
    {
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

            // We are running inside the Revit process, so the current process IS Revit
            // regardless of whether this was triggered from Idling or ApplicationClosing.
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