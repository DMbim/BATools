using System;

namespace BA.Updates
{
    internal static class UpdateConfig
    {
        public const string GitHubOwner = "DMbim";
        public const string GitHubRepo = "BATools"; // or your public releases repo

        // Release asset name format: BA_R26.zip
        public static string GetAssetNameForRevit(string revitMajorVersion)
        {
            var r = ToRxx(revitMajorVersion); // "2026" -> "R26"
            return $"BA_{r}.zip";
        }

        // This must exist next to BA.dll after install/update
        public const string InstallerExeName = "BATools-Installer.exe";

        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
        public const int HttpTimeoutSeconds = 8;

        // Needed only for PRIVATE repos
        public const string GitHubTokenEnvVar = "BA_GITHUB_TOKEN";

        private static string ToRxx(string revitMajor)
        {
            if (string.IsNullOrWhiteSpace(revitMajor)) return "R??";
            if (int.TryParse(revitMajor, out var y)) return "R" + (y % 100).ToString("00");
            return "R" + revitMajor;
        }
    }
}
