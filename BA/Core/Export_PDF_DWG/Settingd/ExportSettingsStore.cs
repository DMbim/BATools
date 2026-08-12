using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Settings.Export
{
    /// <summary>
    /// Persists ExportSettingsRoot to %AppData%\BA\Export\{ProjectNumber}.json,
    /// keyed by project number extracted from the central model path using
    /// the same content-matching approach established for the Type Data
    /// Ledger project set differentiation (first path segment matching
    /// \d{2}-\d{3}), so it works for UNC paths, mapped drives, and multiple
    /// office locations. Settings are local per user, not synced through the
    /// central model.
    /// </summary>
    public static class ExportSettingsStore
    {
        private static readonly Regex ProjectNumberPattern = new Regex(@"\d{2}-\d{3}", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static ExportSettingsRoot Load(Document doc)
        {
            var projectNumber = ResolveProjectNumber(doc);
            var path = GetSettingsPath(projectNumber);

            if (!File.Exists(path))
            {
                return new ExportSettingsRoot { ProjectNumber = projectNumber };
            }

            try
            {
                var json = File.ReadAllText(path);
                var root = JsonSerializer.Deserialize<ExportSettingsRoot>(json, SerializerOptions);
                return root ?? new ExportSettingsRoot { ProjectNumber = projectNumber };
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"ExportSettingsStore failed to load settings for project '{projectNumber}'", ex);
                return new ExportSettingsRoot { ProjectNumber = projectNumber };
            }
        }

        public static void Save(Document doc, ExportSettingsRoot settings)
        {
            var projectNumber = ResolveProjectNumber(doc);
            settings.ProjectNumber = projectNumber;

            var path = GetSettingsPath(projectNumber);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SerializerOptions);

            // Write to a temp file then swap in, avoids a half written file
            // if the process is interrupted mid save.
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, path, overwrite: true);
            File.Delete(tempPath);
        }

        private static string GetSettingsPath(string projectNumber)
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "BA", "Export", $"{projectNumber}.json");
        }

        private static string ResolveProjectNumber(Document doc)
        {
            string rawPath;

            try
            {
                if (doc.IsWorkshared)
                {
                    var centralModelPath = doc.GetWorksharingCentralModelPath();
                    rawPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralModelPath);
                }
                else
                {
                    rawPath = doc.PathName;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ExportSettingsStore could not resolve a path for project number extraction", ex);
                rawPath = doc.Title;
            }

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return "UNKNOWN";
            }

            foreach (var segment in rawPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                var match = ProjectNumberPattern.Match(segment);
                if (match.Success)
                {
                    return match.Value;
                }
            }

            return "UNKNOWN";
        }
    }
}
