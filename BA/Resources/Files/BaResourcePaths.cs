using System;
using System.IO;
using System.Reflection;

namespace BA.Resources
{
    internal static class BaResourcePaths
    {
        /// <summary>Folder where BA.dll is running from (install directory).</summary>
        public static string GetInstallRoot()
        {
            var asmPath = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(asmPath) ?? "";
        }

        public static string FamiliesRoot()
        {
            return Path.Combine(GetInstallRoot(), "Assets", "Families");
        }

        public static string GetFamilyPath(string fileNameOrRelativePath)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrRelativePath))
                throw new ArgumentException("Family path is empty.", nameof(fileNameOrRelativePath));

            // Allow either "BA_Foo.rfa" or "Subfolder\\BA_Foo.rfa"
            return Path.Combine(FamiliesRoot(), fileNameOrRelativePath);
        }

        public static bool FamilyExists(string fileNameOrRelativePath, out string fullPath)
        {
            fullPath = GetFamilyPath(fileNameOrRelativePath);
            return File.Exists(fullPath);
        }
    }
}
