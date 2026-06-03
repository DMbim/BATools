using BA.IssueReporter.Models;
using System;
using System.IO;
using System.Text.Json;

namespace BA.IssueReporter.Services;

public class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsPath = GetSettingsPath();
    }

    public string SettingsPath => _settingsPath;

    public IssueReporterSettings Load()
    {
        EnsureFolder();

        if (!File.Exists(_settingsPath))
        {
            throw new FileNotFoundException(
                "Issue Reporter settings.json was not found. Please run the BA installer/update.",
                _settingsPath);
        }

        string json = File.ReadAllText(_settingsPath);

        if (string.IsNullOrWhiteSpace(json))
        {
            var defaults = new IssueReporterSettings();
            Save(defaults);
            return defaults;
        }

        return JsonSerializer.Deserialize<IssueReporterSettings>(json)
               ?? new IssueReporterSettings();
    }

    public void Save(IssueReporterSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        EnsureFolder();

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(settings, options);
        File.WriteAllText(_settingsPath, json);
    }

    private void EnsureFolder()
    {
        string folder = Path.GetDirectoryName(_settingsPath);

        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException(
                $"Could not determine settings folder from path:\n{_settingsPath}");
        }

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
    }

    private static string GetSettingsPath()
    {
        string programData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);

        return Path.Combine(
            programData,
            "BA",
            "IssueReporter",
            "settings.json");
    }
}