using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BA.Core.Content.Models;

namespace BA.Core.Content.Services
{
    /// <summary>
    /// Dual-scope favorites/tags store for loaded families.
    /// User scope: %AppData%\BA\ContentBrowser\LoadedFamilyFavorites\{identityHash}.json
    /// Project scope: S:\CAD\Autodesk Revit\_admin\BA_tools\LoadedFamilyFavorites\{ProjectSet}\{identityHash}.json
    /// Project scope is unavailable (ProjectScopeAvailable == false) when no
    /// project set segment was detected in the source path.
    /// </summary>
    public sealed class LoadedFamilyFavoritesService
    {
        private const string NetworkRoot = @"S:\CAD\Autodesk Revit\_admin\BA_tools\LoadedFamilyFavorites";

        private readonly string _userFilePath;
        private readonly string? _projectFilePath;

        public bool ProjectScopeAvailable => !string.IsNullOrWhiteSpace(_projectFilePath);

        public LoadedFamilyFavoritesService(LoadedFamilyIdentity identity)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _userFilePath = Path.Combine(appData, "BA", "ContentBrowser", "LoadedFamilyFavorites",
                identity.IdentityHash + ".json");

            _projectFilePath = !string.IsNullOrWhiteSpace(identity.ProjectSet)
                ? Path.Combine(NetworkRoot, identity.ProjectSet!, identity.IdentityHash + ".json")
                : null;
        }

        public Dictionary<string, LoadedFamilyFavoriteEntry> Load(FavoriteScope scope)
        {
            string? path = ResolvePath(scope);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new Dictionary<string, LoadedFamilyFavoriteEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<LoadedFamilyFavoriteEntry>>(json) ?? new();
                return list.ToDictionary(BuildKey, x => x, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, LoadedFamilyFavoriteEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Save(FavoriteScope scope, IEnumerable<LoadedFamilyFavoriteEntry> entries)
        {
            string? path = ResolvePath(scope);
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(
                    "Project scope favorites are unavailable for this document (no project set detected in path).");

            var list = entries
                .Where(e => e.IsFavorite || (e.Tags != null && e.Tags.Count > 0))
                .OrderBy(e => e.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(list, options);

            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);

            if (scope == FavoriteScope.Project)
            {
                // Atomic write for network resource, per project convention.
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            }
            else
            {
                File.WriteAllText(path, json);
            }
        }

        public static string BuildKey(string familyName, string typeName)
        {
            return $"{familyName}::{typeName}".ToLowerInvariant();
        }

        private static string BuildKey(LoadedFamilyFavoriteEntry entry)
        {
            return BuildKey(entry.FamilyName, entry.TypeName);
        }

        private string? ResolvePath(FavoriteScope scope)
        {
            return scope == FavoriteScope.User ? _userFilePath : _projectFilePath;
        }
    }
}