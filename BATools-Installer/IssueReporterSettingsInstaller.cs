using System;
using System.IO;
using System.Text.Json;

namespace BATools_Installer
{
    internal static class IssueReporterSettingsInstaller
    {
        public static void InstallOrUpdate(Action<string> log)
        {
            string sourcePath = InstallerConfig.IssueReporterSharedSettingsPath;
            string localPath = GetLocalSettingsPath();

            log("Installing Issue Reporter settings...");

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "Issue Reporter shared settings.json was not found.",
                    sourcePath);
            }

            string sourceJson = File.ReadAllText(sourcePath);

            ValidateSettingsJson(sourceJson, sourcePath);

            string localDir = Path.GetDirectoryName(localPath);

            if (string.IsNullOrWhiteSpace(localDir))
            {
                throw new InvalidOperationException(
                    $"Could not determine local Issue Reporter settings folder from path:\n{localPath}");
            }

            Directory.CreateDirectory(localDir);

            File.Copy(sourcePath, localPath, overwrite: true);

            log($"Issue Reporter settings copied:");
            log($"  From: {sourcePath}");
            log($"  To:   {localPath}");
        }

        private static string GetLocalSettingsPath()
        {
            string programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);

            return Path.Combine(
                programData,
                InstallerConfig.IssueReporterLocalCompanyFolderName,
                InstallerConfig.IssueReporterLocalFolderName,
                InstallerConfig.IssueReporterSettingsFileName);
        }

        private static void ValidateSettingsJson(string json, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(
                    $"Issue Reporter settings file is empty:\n{sourcePath}");
            }

            IssueReporterSettingsFile settings;

            try
            {
                settings = JsonSerializer.Deserialize<IssueReporterSettingsFile>(json)
                           ?? new IssueReporterSettingsFile();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Issue Reporter settings file is not valid JSON:\n{sourcePath}\n\n{ex.Message}",
                    ex);
            }

            if (string.IsNullOrWhiteSpace(settings.IssueDatabasePath))
            {
                throw new InvalidOperationException(
                    $"Issue Reporter settings file is missing IssueDatabasePath:\n{sourcePath}");
            }

            if (string.IsNullOrWhiteSpace(settings.TeamsWorkflowUrl))
            {
                throw new InvalidOperationException(
                    $"Issue Reporter settings file is missing TeamsWorkflowUrl:\n{sourcePath}");
            }

            if (settings.ManagerUsers == null || settings.ManagerUsers.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Issue Reporter settings file is missing ManagerUsers:\n{sourcePath}");
            }
        }

        private sealed class IssueReporterSettingsFile
        {
            public string IssueDatabasePath { get; set; } = string.Empty;
            public string TeamsWorkflowUrl { get; set; } = string.Empty;
            public string[] ManagerUsers { get; set; } = Array.Empty<string>();
        }
    }
}