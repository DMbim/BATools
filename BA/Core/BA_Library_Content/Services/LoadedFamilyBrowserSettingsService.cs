using System;
using System.IO;
using System.Text.Json;
using BA.Core.Content.Models;

namespace BA.Core.Content.Services
{
    public sealed class LoadedFamilyBrowserSettingsService
    {
        private readonly string _settingsPath;

        public LoadedFamilyBrowserSettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _settingsPath = Path.Combine(appData, "BA", "ContentBrowser", "LoadedFamilyBrowserSettings.json");
        }

        public LoadedFamilyBrowserSettings LoadOrCreateDefault()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    var loaded = JsonSerializer.Deserialize<LoadedFamilyBrowserSettings>(json);
                    if (loaded != null)
                        return loaded;
                }
                catch
                {
                    // fall through to default
                }
            }

            var settings = new LoadedFamilyBrowserSettings();
            Save(settings);
            return settings;
        }

        public void Save(LoadedFamilyBrowserSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsPath, json);
        }
    }
}