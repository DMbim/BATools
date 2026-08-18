// File: BA_Tools/CadPurge/Services/MappingConfigService.cs
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BA.BAApplication;
using BA.CadPurge.Models;
using BA.CadPurge.Services.Json;

namespace BA.CadPurge.Services
{
    /// <summary>
    /// Loads, validates, and caches corporate_standards.json. Nothing in this class touches the
    /// Revit API, so it is safe to call directly from the WPF UI thread, no ExternalEvent/
    /// AppExternalInvoker routing is needed for config loading, only for the scan/mapping/delete
    /// operations that actually read or write the Revit document (see PurgeScanService and
    /// PurgeBatchExecutor).
    ///
    /// Not thread-safe by design, always call from the thread that owns CadPurgeViewModel.
    /// </summary>
    public sealed class MappingConfigService
    {
        // Matches the actual project structure: Core/CAD Purge/Config/, not the CadPurge/Config
        // path this originally assumed. Relies on the default SDK-style content-copy behavior
        // preserving that same relative path into the build output next to the compiled DLL.
        // Confirm the json file's Build Action is Content (or None) with Copy to Output Directory
        // set, otherwise it never reaches bin/ regardless of this path being correct.
        private static readonly string DefaultConfigPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            "Core", "CAD Purge", "Config", "corporate_standards.json"); // <- CHANGED

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new CaseInsensitiveEnumJsonConverter<PurgeItemType>() },
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly string _configPath;
        private MappingConfig _cached;

        public MappingConfigService(string configPath = null)
        {
            _configPath = string.IsNullOrWhiteSpace(configPath) ? DefaultConfigPath : configPath;
        }

        /// <summary>
        /// Loads corporate_standards.json (or returns the cached instance from a prior successful
        /// load). Never throws for missing file / malformed JSON / failed validation, those are
        /// reported through the out parameters so the UI can show a clear message instead of an
        /// unhandled exception surfacing from deep in the load path.
        /// </summary>
        public bool TryLoad(out MappingConfig config, out string errorMessage)
        {
            if (_cached != null)
            {
                config = _cached;
                errorMessage = null;
                return true;
            }

            config = null;
            errorMessage = null;

            if (!File.Exists(_configPath))
            {
                errorMessage = $"Corporate standards config not found at '{_configPath}'.";
                AppLogger.LogInfo($"[CadPurge] {errorMessage}");
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(_configPath);
            }
            catch (Exception ex)
            {
                errorMessage = $"Could not read '{_configPath}': {ex.Message}";
                AppLogger.LogError("MappingConfigService.TryLoad (file read)", ex);
                return false;
            }

            MappingConfig deserialized;
            try
            {
                deserialized = JsonSerializer.Deserialize<MappingConfig>(json, SerializerOptions);
            }
            catch (JsonException ex)
            {
                errorMessage = $"'{_configPath}' is not valid JSON: {ex.Message}";
                AppLogger.LogError("MappingConfigService.TryLoad (deserialize)", ex);
                return false;
            }

            if (deserialized == null)
            {
                errorMessage = $"'{_configPath}' deserialized to an empty document.";
                return false;
            }

            var problems = deserialized.Validate();
            if (problems.Count > 0)
            {
                errorMessage = "corporate_standards.json failed validation:" + Environment.NewLine
                    + string.Join(Environment.NewLine, problems);
                AppLogger.LogInfo($"[CadPurge] {errorMessage}");
                return false;
            }

            _cached = deserialized;
            config = _cached;
            return true;
        }

        /// <summary>Forces the next TryLoad() call to re-read from disk instead of using the cached config.</summary>
        public void InvalidateCache() => _cached = null;

        /// <summary>
        /// Finds the first rule (in declaration order) whose ItemType matches and whose
        /// SourceNamePattern matches candidateName. Returns null if nothing matches, that is a
        /// normal, expected outcome, not every scanned candidate has a defined mapping yet.
        /// </summary>
        public MappingRule FindMatch(MappingConfig config, PurgeItemType itemType, string candidateName)
        {
            if (config?.Rules == null) return null;

            foreach (MappingRule rule in config.Rules)
            {
                if (rule == null || rule.ItemType != itemType) continue;
                if (rule.IsMatch(candidateName)) return rule;
            }

            return null;
        }
    }
}