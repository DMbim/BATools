using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BATools.SelectionManager.Models;

namespace BATools.SelectionManager.Services
{
    public static class ToolbarProfileService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BA", "ToolbarProfiles", "Default.json");

        private static void MigrateIfNeeded()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string oldPath = Path.Combine(appData, "BATools", "ToolbarProfiles", "Default.json");
            string newPath = FilePath;
            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                File.Move(oldPath, newPath);
            }
        }

        public static ToolbarProfile Load()
        {
            MigrateIfNeeded();
            string path = FilePath; // local variable resolves type name ambiguity
            if (!System.IO.File.Exists(path)) return ToolbarProfile.CreateDefault();
            try
            {
                string json = System.IO.File.ReadAllText(path);
                var profile = JsonSerializer.Deserialize<ToolbarProfile>(json, JsonOptions);
                if (profile == null || profile.Tabs == null || profile.Tabs.Count == 0)
                {
                    Debug.WriteLine("[ToolbarProfileService] Profile empty or null — using default.");
                    return ToolbarProfile.CreateDefault();
                }
                return profile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[ToolbarProfileService] Load failed ({ex.GetType().Name}: {ex.Message}) — resetting to default.");
                try { System.IO.File.Delete(path); } catch { }
                return ToolbarProfile.CreateDefault();
            }
        }
        public static void Save(ToolbarProfile profile)
        {
            string path = FilePath;
            try
            {
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path,
                    JsonSerializer.Serialize(profile, JsonOptions));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ToolbarProfileService] Save failed: {ex.Message}");
            }
        }
    }
}