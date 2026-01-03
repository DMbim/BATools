using System;
using System.IO;

namespace BATools_Installer
{
    internal static class RevitInstallPaths
    {
        public static string GetAddinsRoot(int revitYear)
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appdata, "Autodesk", "Revit", "Addins", revitYear.ToString());
        }

        public static string GetInstallDir(int revitYear)
        {
            return Path.Combine(GetAddinsRoot(revitYear), InstallerConfig.AddinFolderName);
        }

        public static string GetManifestPath(int revitYear)
        {
            return Path.Combine(GetAddinsRoot(revitYear), InstallerConfig.AddinManifestName);
        }

        public static bool IsInstalled(int revitYear)
        {
            return Directory.Exists(GetInstallDir(revitYear)) && File.Exists(GetManifestPath(revitYear));
        }

        public static void Uninstall(int revitYear)
        {
            var dir = GetInstallDir(revitYear);
            var manifest = GetManifestPath(revitYear);

            if (File.Exists(manifest)) File.Delete(manifest);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
