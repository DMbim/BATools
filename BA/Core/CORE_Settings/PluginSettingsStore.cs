using System;
using System.IO;
using System.Text.Json;

namespace BA.Core.Settings
{
    public static class PluginSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string GetDefaultPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        public static PluginSettings Load(string? path = null)
        {
            path ??= GetDefaultPath();

            try
            {
                if (!File.Exists(path))
                    return new PluginSettings();

                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<PluginSettings>(json, JsonOpts);
                return settings ?? new PluginSettings();
            }
            catch
            {
                // If file is corrupted, don't kill Revit.
                return new PluginSettings();
            }
        }

        public static void Save(PluginSettings settings, string? path = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            path ??= GetDefaultPath();

            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(path, json);
        }
    }
}