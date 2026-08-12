using System;
using System.IO;
using System.Text.Json;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Settings.Export
{
    /// <summary>
    /// Persists ParameterColumnLayout to %AppData%\BA\Export\ColumnLayout.json.
    /// Global to the user, not project scoped, unlike ExportSettingsStore.
    /// Plain file IO only, no Document dependency, safe to call directly
    /// from WPF code, this never needs to route through ExportUiBridge.
    /// </summary>
    public static class ParameterColumnLayoutStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static ParameterColumnLayout Load()
        {
            var path = GetPath();

            if (!File.Exists(path))
            {
                return new ParameterColumnLayout();
            }

            try
            {
                var json = File.ReadAllText(path);
                var layout = JsonSerializer.Deserialize<ParameterColumnLayout>(json, SerializerOptions);
                return layout ?? new ParameterColumnLayout();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ParameterColumnLayoutStore failed to load column layout", ex);
                return new ParameterColumnLayout();
            }
        }

        public static void Save(ParameterColumnLayout layout)
        {
            var path = GetPath();
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(layout, SerializerOptions);

            // Write to a temp file then swap in, same pattern as
            // ExportSettingsStore, avoids a half written file if the
            // process is interrupted mid save.
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, path, overwrite: true);
            File.Delete(tempPath);
        }

        private static string GetPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "BA", "Export", "ColumnLayout.json");
        }
    }
}
