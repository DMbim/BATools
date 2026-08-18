// File: BA.Settings/DateToolSettings.cs
using System;
using System.IO;

namespace BA.Settings
{
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