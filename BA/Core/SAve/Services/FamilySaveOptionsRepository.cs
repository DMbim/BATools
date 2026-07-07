using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BA.Families.Models;

namespace BA.Families.Services
{
    /// <summary>
    /// Persists SaveFamiliesOptions as JSON to %AppData%\BATools\.
    /// All failures are swallowed — persistence must never crash the command.
    /// </summary>
    public class FamilySaveOptionsRepository
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BA");

        private static readonly string SettingsFile = Path.Combine(
            SettingsDir, "SaveFamiliesOptions.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static void MigrateIfNeeded()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string oldPath = Path.Combine(appData, "BATools", "SaveFamiliesOptions.json");
            string newPath = SettingsFile;
            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                File.Move(oldPath, newPath);
            }
        }

        public SaveFamiliesOptions Load()
        {
            MigrateIfNeeded();
            try
            {
                if (!File.Exists(SettingsFile))
                    return new SaveFamiliesOptions();

                string json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<SaveFamiliesOptions>(json, JsonOpts)
                       ?? new SaveFamiliesOptions();
            }
            catch
            {
                return new SaveFamiliesOptions();
            }
        }

        public void Save(SaveFamiliesOptions options)
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                File.WriteAllText(SettingsFile,
                    JsonSerializer.Serialize(options, JsonOpts));
            }
            catch { /* intentional — never crash on persistence failure */ }
        }
    }
}