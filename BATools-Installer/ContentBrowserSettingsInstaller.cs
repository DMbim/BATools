using System;
using System.IO;
using System.Text.Json;

namespace BATools_Installer
{
    internal static class ContentBrowserSettingsInstaller
    {
        public static void InstallOrUpdate(Action<string> log)
        {
            string sourcePath = InstallerConfig.ContentBrowserSharedSettingsPath;
            string localPath = GetLocalSettingsPath();

            log("Installing Content Browser settings...");

            if (!File.Exists(sourcePath))
            {
                log($"WARN: Content Browser shared settings not found at: {sourcePath}");
                log("WARN: Skipping Content Browser settings distribution — S: drive may be unavailable.");
                return;
            }

            if (File.Exists(localPath))
            {
                log($"Content Browser settings already exist at: {localPath}");
                log("Skipping — local settings preserved.");
                return;
            }

            string sourceJson = File.ReadAllText(sourcePath);
            ValidateSettingsJson(sourceJson, sourcePath);

            string localDir = Path.GetDirectoryName(localPath)!;
            if (string.IsNullOrWhiteSpace(localDir))
            {
                throw new InvalidOperationException(
                    $"Could not determine local Content Browser settings folder from path:\n{localPath}");
            }

            Directory.CreateDirectory(localDir);
            File.Copy(sourcePath, localPath, overwrite: false);

            log("Content Browser settings copied:");
            log($"  From: {sourcePath}");
            log($"  To:   {localPath}");
        }

        private static string GetLocalSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(
                appData,
                InstallerConfig.IssueReporterLocalCompanyFolderName,
                InstallerConfig.ContentBrowserLocalFolderName,
                InstallerConfig.ContentBrowserSettingsFileName);
        }

        private static void ValidateSettingsJson(string json, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(
                    $"Content Browser settings file is empty:\n{sourcePath}");
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Roots", out var roots) ||
                    roots.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        $"Content Browser settings file is missing Roots array:\n{sourcePath}");
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Content Browser settings file is not valid JSON:\n{sourcePath}\n\n{ex.Message}",
                    ex);
            }
        }
    }
}