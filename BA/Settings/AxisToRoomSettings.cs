using System;
using System.IO;
using BA.Settings;

namespace BA.Settings.Rooms
{
    public sealed class AxisToRoomSettings : AppSettingsBase
    {
        protected override string SubFolder => "Rooms";
        protected override string FileName => "AxisToRoomSettings.json";

        // Prefer UniqueId over Name (name can change with " : 1")
        public string? SelectedLinkInstanceUniqueId { get; set; }
        public string? SelectedLinkInstanceName { get; set; } // fallback
        public bool PlaceDimensionVariant { get; set; } // true => BA_Axis_Dim, false => BA_Axis

        /// <summary>One-time migration from legacy "bimBA/settings.json" (where you stuffed DateToolSettings).</summary>
        public static AxisToRoomSettings LoadWithLegacyMigration()
        {
            var s = Load<AxisToRoomSettings>();

            // If already configured, stop.
            if (!string.IsNullOrWhiteSpace(s.SelectedLinkInstanceUniqueId) ||
                !string.IsNullOrWhiteSpace(s.SelectedLinkInstanceName))
                return s;

            // Legacy file that your old code used: AppSettingsBase.Load<DateToolSettings>("bimBA","settings.json")
            // We'll try to read it as a "dynamic-ish" JSON object.
            var legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "bimBA", "bimBA", "settings.json");

            if (!File.Exists(legacyPath))
                return s;

            try
            {
                var json = File.ReadAllText(legacyPath);
                var legacy = Newtonsoft.Json.Linq.JObject.Parse(json);

                s.SelectedLinkInstanceName = legacy.Value<string>("SelectedRevitLinkName");
                s.PlaceDimensionVariant = legacy.Value<bool?>("IsDimensionChecked") ?? false;

                s.Save();
            }
            catch
            {
                // ignore
            }

            return s;
        }
    }
}
