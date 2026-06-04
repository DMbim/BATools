using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BATools.SelectionManager.Models;

namespace BATools.SelectionManager.Services
{
    public static class FavoriteFamiliesService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BA", "fav_families.json");

        public static FavoriteFamiliesProfile Load()
        {
            MigrateIfNeeded();
            if (!File.Exists(FilePath)) return new FavoriteFamiliesProfile();
            try
            {
                return JsonSerializer.Deserialize<FavoriteFamiliesProfile>(
                    File.ReadAllText(FilePath), JsonOptions)
                       ?? new FavoriteFamiliesProfile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavoriteFamiliesService] Load failed: {ex.Message}");
                return new FavoriteFamiliesProfile();
            }
        }

        private static void MigrateIfNeeded()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string oldPath = Path.Combine(appData, "BATools", "fav_families.json");
            string newPath = FilePath;
            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                File.Move(oldPath, newPath);
            }
        }

        public static void Save(FavoriteFamiliesProfile profile)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(profile, JsonOptions));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FavoriteFamiliesService] Save failed: {ex.Message}");
            }
        }
 
    }
}