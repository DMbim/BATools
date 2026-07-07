using System;
using System.IO;
using Newtonsoft.Json;

namespace BA.QA.FamilyVersioning
{
    /// <summary>
    /// Persisted settings for the Family Versioning module, stored as a JSON file
    /// named BA_FamilyVersioning.json sitting next to the project's central .rvt file
    /// on the shared drive. Every machine that opens the model reads the same settings
    /// file automatically, no per-machine configuration required.
    ///
    /// File naming convention: {ModelFileNameWithoutExtension}_FamilyVersioning.json
    /// Example: if the central is "ProjectA_Arch.rvt", settings are stored as
    /// "ProjectA_Arch_FamilyVersioning.json" in the same directory. This avoids
    /// collisions if multiple Revit projects share a folder (uncommon but possible).
    /// </summary>
    public sealed class FamilyVersioningSettings
    {
        /// <summary>
        /// Absolute path to the SQLite catalog database for this project.
        /// Null or empty means not yet configured (first run).
        /// </summary>
        [JsonProperty("catalogDatabasePath")]
        public string? CatalogDatabasePath { get; set; }

        /// <summary>
        /// UTC timestamp of when these settings were last written. Informational only,
        /// not used for any logic, but useful when inspecting the JSON manually.
        /// </summary>
        [JsonProperty("lastModifiedUtc")]
        public DateTime LastModifiedUtc { get; set; }

        /// <summary>
        /// Schema version of this settings file format. Increment if the shape of this
        /// class changes in a way that would break reading an older file, allows a
        /// future migration branch in Load() without silently producing bad state.
        /// </summary>
        [JsonProperty("settingsVersion")]
        public int SettingsVersion { get; set; } = 1;
    }

    /// <summary>
    /// Reads and writes FamilyVersioningSettings to/from a JSON file located next to
    /// the Revit model file. All methods are static since there is no meaningful
    /// instance state here beyond the derived file path itself, which is always
    /// computed fresh from the model path.
    /// </summary>
    public static class FamilyVersioningSettingsStore
    {
        private const string SettingsFileSuffix = "_FamilyVersioning.json";

        /// <summary>
        /// Derives the settings file path from the model's central file path.
        /// modelPath should be the result of Document.PathName (which returns the
        /// central file path in a workshared model, not the local copy path, which
        /// is what you want so all users resolve to the same settings file on the
        /// shared drive).
        /// </summary>
        public static string GetSettingsFilePath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException(
                    "Model path must not be empty. Ensure the document has been saved " +
                    "and is a workshared central or a plain saved .rvt before calling this.",
                    nameof(modelPath));
            }

            var directory = Path.GetDirectoryName(modelPath)
                ?? throw new InvalidOperationException(
                    $"Could not determine directory from model path '{modelPath}'.");

            var modelNameWithoutExtension = Path.GetFileNameWithoutExtension(modelPath);
            return Path.Combine(directory, modelNameWithoutExtension + SettingsFileSuffix);
        }

        /// <summary>
        /// Loads settings from the JSON file next to the model. Returns null if the
        /// file does not yet exist (first run scenario), never throws for a missing
        /// file. Throws only for a corrupt/unreadable file since that is an
        /// unexpected condition that should surface rather than silently reset.
        /// </summary>
        public static FamilyVersioningSettings? Load(string modelPath)
        {
            var settingsPath = GetSettingsFilePath(modelPath);

            if (!File.Exists(settingsPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(settingsPath);
                return JsonConvert.DeserializeObject<FamilyVersioningSettings>(json);
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException)
            {
                throw new InvalidOperationException(
                    $"Family Versioning settings file at '{settingsPath}' could not be read or parsed. " +
                    "The file may be corrupt. You can delete it to reset to first-run state, " +
                    "but you will need to re-enter the catalog database path.", ex);
            }
        }

        /// <summary>
        /// Saves settings to the JSON file next to the model. Creates the file if it
        /// does not exist. Overwrites it if it does. The file is small (under 1KB)
        /// and written atomically via a temp file + rename to avoid leaving a partial
        /// write if the process is killed mid-write, which would corrupt the JSON and
        /// trigger the corrupt-file error in Load() above.
        /// </summary>
        public static void Save(string modelPath, FamilyVersioningSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.LastModifiedUtc = DateTime.UtcNow;

            var settingsPath = GetSettingsFilePath(modelPath);
            var tempPath = settingsPath + ".tmp";

            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);

            File.WriteAllText(tempPath, json);
            File.Move(tempPath, settingsPath, overwrite: true);
        }
    }
}
