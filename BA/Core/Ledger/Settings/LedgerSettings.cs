using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BA.BAApplication;

namespace BA.Settings
{
    /// <summary>
    /// Persisted configuration for the Type Data Ledger feature.
    /// NOTE: I do not have the actual AppSettingsBase source, so this class implements its
    /// own Load/Save rather than inheriting from an unverified base class API. If you already
    /// have AppSettingsBase under BA.Settings with a known shape, paste it and I will refactor
    /// this to inherit from it instead.
    /// </summary>
    public class LedgerSettings
    {
        private static readonly string SettingsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BA", "Settings");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "LedgerSettings.json");

        public string LedgerFilePath { get; set; } = @"S:\CAD\Autodesk Revit\_admin\BA_tools\BA_Ledger\Data_Ledger.json";

        /// <summary>
        /// Revit Category.Id.Value (long, Revit 2026 API) of categories that are allowed to be
        /// tracked by the ledger. Empty list means "all categories allowed", not "none".
        /// </summary>
        public List<long> AllowedCategoryIds { get; set; } = new List<long>();

        public int RetryCount { get; set; } = 8;

        public int RetryDelayMs { get; set; } = 250;

        public bool IsCategoryAllowed(long categoryIdValue)
        {
            return AllowedCategoryIds.Count == 0 || AllowedCategoryIds.Contains(categoryIdValue);
        }

        public static LedgerSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return new LedgerSettings();
                }

                string json = File.ReadAllText(SettingsFilePath);
                LedgerSettings loaded = JsonSerializer.Deserialize<LedgerSettings>(json);
                return loaded ?? new LedgerSettings();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LedgerSettings.Load: failed to read settings, using defaults", ex);
                return new LedgerSettings();
            }
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LedgerSettings.Save: failed to write settings", ex);
                throw;
            }
        }
    }
}
