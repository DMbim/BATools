using System;
using System.IO;
using Newtonsoft.Json;

namespace BA.Settings
{
    /// <summary>
    /// Base class for per-user JSON settings in %AppData%\BA\[SubFolder]\[FileName].
    /// Preserves your original public API (Load, Save) and adds migration helpers.
    /// </summary>
    public abstract class AppSettingsBase
    {
        /// <summary>
        /// Root folder under %AppData%. For BA we use "BA".
        /// If you ever want a different root for a specific tool,
        /// you can override this in a derived class.
        /// </summary>
        protected virtual string AppRootFolder => "BA";

        /// <summary>
        /// Per-tool subfolder, e.g. "Date", "Classification", "ChangeMonitor".
        /// </summary>
        protected abstract string SubFolder { get; }

        /// <summary>
        /// File name, e.g. "DateSettings.json".
        /// </summary>
        protected abstract string FileName { get; }

        private string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppRootFolder, SubFolder, FileName);

        // --------------------------------------------------------------------
        // ORIGINAL CONTRACT (kept, but with smarter migration inside)
        // --------------------------------------------------------------------

        /// <summary>
        /// Original static Load API: Load using explicit subFolder + fileName.
        /// Now automatically migrates from legacy %AppData%\bimBA\... if needed.
        /// </summary>
        public static T Load<T>(string subFolder, string fileName)
            where T : AppSettingsBase, new()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // New location under BA
            string newRoot = "BA";
            string newPath = Path.Combine(appData, newRoot, subFolder, fileName);

            // Legacy location under bimBA
            string legacyRoot = "bimBA";
            string legacyPath = Path.Combine(appData, legacyRoot, subFolder, fileName);

            // Migrate from legacy if new doesn't exist yet
            if (!File.Exists(newPath) && File.Exists(legacyPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath) ?? string.Empty);
                File.Copy(legacyPath, newPath, overwrite: false);
            }

            if (File.Exists(newPath))
            {
                var json = File.ReadAllText(newPath);
                return JsonConvert.DeserializeObject<T>(json) ?? new T();
            }

            return new T();
        }

        /// <summary>
        /// Original instance Save() – unchanged, except it now uses AppRootFolder ("BA").
        /// </summary>
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath) ?? string.Empty);
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(SettingsPath, json);
        }

        // --------------------------------------------------------------------
        // ADDITIVE HELPERS
        // --------------------------------------------------------------------

        /// <summary>
        /// Convenience loader that uses each subclass' own SubFolder/FileName.
        /// </summary>
        public static T Load<T>() where T : AppSettingsBase, new()
        {
            var tmp = new T();
            // Reuse the legacy-aware Load(subFolder,fileName)
            return Load<T>(tmp.SubFolder, tmp.FileName);
        }

        /// <summary>
        /// One-time migration helper: if no current file but a legacy file exists,
        /// copy it into this settings' normal location.
        /// </summary>
        protected void TryMigrateFromAbsolutePath(string legacyAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(legacyAbsolutePath))
                return;

            var currentPath = SettingsPath;
            var currentDir = Path.GetDirectoryName(currentPath) ?? string.Empty;

            if (!File.Exists(currentPath) && File.Exists(legacyAbsolutePath))
            {
                Directory.CreateDirectory(currentDir);
                File.Copy(legacyAbsolutePath, currentPath, overwrite: false);
            }
        }
    }

    /// <summary>
    /// Concrete settings class for the Date tool.
    /// Lives here so it can be reused easily by commands that set issue dates / revisions.
    /// </summary>
    public sealed class DateToolSettings : AppSettingsBase
    {
        // Stored under %AppData%\BA\Date\DateSettings.json
        protected override string SubFolder => "Date";
        protected override string FileName => "DateSettings.json";

        // Main fields used by your Date tool
        public string SelectedDateParam { get; set; } = "Issue Date";
        public string SelectedRevParam { get; set; } = "Revision";
        public string SelectedFormat { get; set; } = "yy/MM/dd";

        // Extra fields used by other commands (parameter copy / room-link stuff)
        public string? SelectedRevitLinkName { get; set; }
        public bool IsDimensionChecked { get; set; }
        public string? SelectedCategory { get; set; }
        public string? SourceParameter { get; set; }
        public string? DestinationParameter { get; set; }
        public string? RoomIdSharedGuid { get; set; }
        public int RoomIdBuiltIn { get; set; }
        public string? RoomIdName { get; set; }
        public string? RoomIdParamMode { get; set; }

        /// <summary>
        /// Load settings with migration from very old locations:
        /// 1) %AppData%\Date\DateSettings.json (ZoomIN-era)
        /// 2) %AppData%\bimBA\Date\DateSettings.json (previous plugin)
        /// Target is %AppData%\BA\Date\DateSettings.json.
        /// </summary>
        public static DateToolSettings LoadWithMigration()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 1) Very old plain folder (ZoomIN style)
            string legacyPlain = Path.Combine(appData, "Date", "DateSettings.json");

            // Ensure migration from plain path into the BA root
            var tmp = new DateToolSettings();
            tmp.TryMigrateFromAbsolutePath(legacyPlain);

            // 2) The base Load<T>() will then:
            //    - migrate from %AppData%\bimBA\Date\DateSettings.json -> %AppData%\BA\Date\DateSettings.json
            //    - and finally read from %AppData%\BA\Date\DateSettings.json
            return Load<DateToolSettings>();
        }
    }
}
