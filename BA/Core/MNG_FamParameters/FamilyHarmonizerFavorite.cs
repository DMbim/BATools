using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BA.Core
{
    /// <summary>
    /// A single saved "favorite" shared parameter for the Family Parameter Manager.
    /// Keyed by the shared parameter's stable GUID. Name/Spec/IsInstance/GroupTypeId
    /// are display/seed hints captured at save time and refreshed against the
    /// current shared parameter file when the favorites panel loads.
    /// </summary>
    public sealed class FamilyHarmonizerFavorite
    {
        [JsonPropertyName("guid")]
        public Guid Guid { get; set; }

        /// <summary>
        /// Name of the shared parameter as it was in the SP file at save time.
        /// Used as the fallback display name if the GUID can no longer be resolved.
        /// </summary>
        [JsonPropertyName("lastKnownName")]
        public string LastKnownName { get; set; } = "";

        [JsonPropertyName("spec")]
        public string Spec { get; set; } = "";

        [JsonPropertyName("isInstance")]
        public bool IsInstance { get; set; }

        [JsonPropertyName("groupTypeId")]
        public string GroupTypeId { get; set; } = "";
    }

    public sealed class FamilyHarmonizerFavoritesFile
    {
        [JsonPropertyName("favorites")]
        public List<FamilyHarmonizerFavorite> Favorites { get; set; } = new();
    }

    /// <summary>
    /// Loads/saves the favorites list to %AppData%\BA\Settings\FamilyHarmonizerFavorites.json.
    /// Plain JSON file, not routed through AppSettingsBase since this is a simple list,
    /// not a settings object with versioned properties.
    /// </summary>
    public static class FamilyHarmonizerFavoritesStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string GetFilePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA", "Settings");

            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "FamilyHarmonizerFavorites.json");
        }

        public static List<FamilyHarmonizerFavorite> Load()
        {
            string path = GetFilePath();

            try
            {
                if (!File.Exists(path))
                    return new List<FamilyHarmonizerFavorite>();

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<FamilyHarmonizerFavorite>();

                var data = JsonSerializer.Deserialize<FamilyHarmonizerFavoritesFile>(json, JsonOptions);
                return data?.Favorites ?? new List<FamilyHarmonizerFavorite>();
            }
            catch
            {
                // Corrupt or unreadable file: do not crash the harmonizer, start empty.
                return new List<FamilyHarmonizerFavorite>();
            }
        }

        public static void Save(IEnumerable<FamilyHarmonizerFavorite> favorites)
        {
            string path = GetFilePath();

            var data = new FamilyHarmonizerFavoritesFile
            {
                Favorites = favorites?.ToList() ?? new List<FamilyHarmonizerFavorite>()
            };

            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Adds a favorite if its GUID is not already present, otherwise updates the
        /// existing entry's LastKnownName/Spec/IsInstance/GroupTypeId. Returns the
        /// updated list (already persisted).
        /// </summary>
        public static List<FamilyHarmonizerFavorite> AddOrUpdate(FamilyHarmonizerFavorite favorite)
        {
            var list = Load();

            var existing = list.FirstOrDefault(f => f.Guid == favorite.Guid);
            if (existing != null)
            {
                existing.LastKnownName = favorite.LastKnownName;
                existing.Spec = favorite.Spec;
                existing.IsInstance = favorite.IsInstance;
                existing.GroupTypeId = favorite.GroupTypeId;
            }
            else
            {
                list.Add(favorite);
            }

            Save(list);
            return list;
        }

        public static List<FamilyHarmonizerFavorite> Remove(Guid guid)
        {
            var list = Load();
            list.RemoveAll(f => f.Guid == guid);
            Save(list);
            return list;
        }
    }
}
