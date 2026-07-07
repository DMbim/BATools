using System;

namespace BATools_Installer
{
    internal static class InstallerConfig
    {
        public const string RepoOwner = "DMbim";
        public const string RepoName = "BATools";

        // For private repos
        public const string GitHubTokenEnvVar = "BA_GITHUB_TOKEN";

        // Add-in identity
        public const string VendorId = "BA";
        public const string AddinName = "BA";
        public const string AddinFolderName = "BA";
        public const string AddinManifestName = "BA.addin";

        public const string AddinAssemblyName = "BA.dll";
        public const string AddinFullClassName = "BA.BAApplication.BaApplication";

        // Generate once and keep forever
        public const string AddinIdGuid = "8D83B3C3-7C9B-4E3B-9A5B-2E4E3E5A8F21";

        // Installer copied next to BA.dll
        public const string InstalledInstallerExeName = "BATools-Installer.exe";

        // Issue Reporter settings deployment
        public const string IssueReporterSharedSettingsPath =
                    @"S:\CAD\Autodesk Revit\_admin\BA_tools\BA_Issues\settings.json";
        public const string IssueReporterLocalCompanyFolderName = "BA";
        public const string IssueReporterLocalFolderName = "IssueReporter";
        public const string IssueReporterSettingsFileName = "settings.json";

        // Content Browser settings distribution
        public const string ContentBrowserSharedSettingsPath =
            @"S:\CAD\Autodesk Revit\_admin\BA_tools\ContentBrowser\content-browser.settings.json";
        public const string ContentBrowserLocalFolderName = "ContentBrowser";
        public const string ContentBrowserSettingsFileName = "content-browser.settings.json";
    }
}