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

        // NOTE: BA_Axis and BA_Axis_Dim were merged into a single family, BA_Axis.rfa,
        // which now carries the Start_X/Mid_X/End_X and Start_Y/Mid_Y/End_Y named
        // reference planes needed for dimensioning. This flag no longer switches which
        // family gets placed -- it now toggles whether AxisDimensionService.CreateAxisDimensions
        // also runs after placement.
        public bool PlaceDimensionVariant { get; set; }

        /// <summary>Offset pushing the horizontal (X) dimension string above the room's top edge, in millimetres.</summary>
        public double DimensionOffsetXMm { get; set; } = 200.0;

        /// <summary>Offset pushing the vertical (Y) dimension string left of the room's left edge, in millimetres.</summary>
        public double DimensionOffsetYMm { get; set; } = 200.0;

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
