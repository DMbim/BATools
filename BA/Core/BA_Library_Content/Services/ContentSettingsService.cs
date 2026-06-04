
using BA.Core.Content;
using global::BA.Core.Content.Models;
using System;
using System.IO;
using System.Text.Json;

namespace BA.Core.Content.Services
{
    public sealed class ContentSettingsService
    {
        private readonly string _settingsPath;

        public ContentSettingsService(string settingsPath)
        {
            _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        }

        public ContentBrowserSettings LoadOrCreateDefault()
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<ContentBrowserSettings>(json);
                if (loaded != null)
                {
                    ApplyMissingDefaults(loaded);
                    return loaded;
                }
            }

            var settings = CreateDefault();
            Save(settings);
            return settings;
        }

        public void Save(ContentBrowserSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsPath, json);
        }

        private static void ApplyMissingDefaults(ContentBrowserSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.CacheFolderPath))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                settings.CacheFolderPath = Path.Combine(appData, "BA", "ContentBrowser", "Cache");
            }
        }

        private static ContentBrowserSettings CreateDefault()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string cacheFolder = Path.Combine(appData, "BA", "ContentBrowser", "Cache");

            return new ContentBrowserSettings
            {
                CacheFolderPath = cacheFolder,
                IncludeRfa = true,
                IncludeRvt = false,
                Roots =
                {
                    new ContentLibraryRoot
                    {
                        Name = "Approved",
                        RootPath = @"\\SERVER\Revit_Library\2026\Approved",
                        IncludeSubfolders = true,
                        IsEnabled = true,
                        ApprovalStateOverride = "Approved"
                    },
                    new ContentLibraryRoot
                    {
                        Name = "WIP",
                        RootPath = @"\\SERVER\Revit_Library\2026\WIP",
                        IncludeSubfolders = true,
                        IsEnabled = true,
                        ApprovalStateOverride = "WIP"
                    }
                }
            };
        }
    }

}
    
