using System;
using System.IO;
using BA.Settings;

namespace BA.Settings.Rooms
{
    public sealed class ElementToRoomSettings : AppSettingsBase
    {
        protected override string SubFolder => "Rooms";
        protected override string FileName => "ElementToRoomSettings.json";

        public string? SelectedLinkInstanceUniqueId { get; set; }
        public string? SelectedLinkInstanceName { get; set; } // fallback

        // Store category token ideally as BuiltInCategory (e.g. "OST_Walls") or display name
        public string? SelectedCategoryToken { get; set; }

        public string SourceParameter { get; set; } = "BA_ID";
        public string DestinationParameter { get; set; } = "BA_ID";

        public static ElementToRoomSettings LoadWithLegacyMigration()
        {
            var s = Load<ElementToRoomSettings>();
            if (!string.IsNullOrWhiteSpace(s.SelectedCategoryToken) ||
                !string.IsNullOrWhiteSpace(s.SelectedLinkInstanceUniqueId) ||
                !string.IsNullOrWhiteSpace(s.SelectedLinkInstanceName))
                return s;

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
                s.SelectedCategoryToken = legacy.Value<string>("SelectedCategory");
                s.SourceParameter = legacy.Value<string>("SourceParameter") ?? s.SourceParameter;
                s.DestinationParameter = legacy.Value<string>("DestinationParameter") ?? s.DestinationParameter;

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
