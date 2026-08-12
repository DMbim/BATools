// Path: BA\Materials\AssetFavoritesStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using BA.BAApplication;

namespace BA.Materials
{
    /// <summary>
    /// Shared, network-stored list of favorited built-in appearance asset names, plain
    /// JSON, no Revit API dependency, callable from any thread. Deliberately not
    /// lock-protected the way MaterialLibraryLock protects the .rvt, this is low-stakes
    /// curation metadata, a rare simultaneous edit losing to last-write-wins is an
    /// acceptable tradeoff here, unlike the material library file itself.
    /// </summary>
    public sealed class AssetFavoritesStore
    {
        public const string DefaultFavoritesPath =
            @"S:\CAD\Autodesk Revit\_admin\BA_tools\MaterialAssetFavorites.json";

        private readonly string _path;

        public AssetFavoritesStore(string path = DefaultFavoritesPath)
        {
            _path = path;
        }

        public HashSet<string> LoadFavoriteNames()
        {
            try
            {
                if (!File.Exists(_path))
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                string json = File.ReadAllText(_path);
                List<string> list = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();

                // Explicitly reconstruct with OrdinalIgnoreCase, per the established
                // pitfall that comparer settings do not survive JSON deserialization.
                return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AssetFavoritesStore.LoadFavoriteNames", ex);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public bool SaveFavoriteNames(IEnumerable<string> names)
        {
            try
            {
                List<string> list = names
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string json = JsonConvert.SerializeObject(list, Formatting.Indented);

                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(_path))
                    File.Delete(_path);

                File.Move(tempPath, _path);

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AssetFavoritesStore.SaveFavoriteNames", ex);
                return false;
            }
        }

        /// <summary>Convenience wrapper: load, mutate one entry, save. Fine for the
        /// occasional toggle-favorite click, not intended for bulk operations.</summary>
        public bool ToggleFavorite(string assetName, bool isFavorite)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return false;

            HashSet<string> current = LoadFavoriteNames();

            if (isFavorite) current.Add(assetName);
            else current.Remove(assetName);

            return SaveFavoriteNames(current);
        }
    }
}