// File: BA.Core/ViewFilters/ColorSchemeLibraryService.cs
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Ledger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BA.Core.ViewFilters
{
    // Persists named ParameterColorRule schemes so they can be listed and picked
    // from the View Template tab instead of browsed to via SaveFileDialog/OpenFileDialog.
    // Location follows the same Project Set resolution as the Type Data Ledger:
    // network share first, keyed by ProjectSetService.GetProjectSetName, falling back
    // to %AppData%\BA\ when the set can't be resolved or the share isn't reachable.
    // Schemes are intentionally shared (network share), not per-user: a color scheme
    // is an office standard, the same class of thing as the ledger's Main Ledger file,
    // not a personal preference. // <- NEW
    public static class ColorSchemeLibraryService
    {
        private const string NetworkRoot = @"S:\CAD\Autodesk Revit\_admin\BA_tools\ColorSchemes";
        private const string FallbackSubfolder = "Default";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static IReadOnlyList<SchemeSummary> ListSchemes(Document doc)
        {
            var folder = ResolveSchemesFolder(doc, createIfMissing: false);
            if (folder == null || !Directory.Exists(folder))
                return [];

            var results = new List<SchemeSummary>();

            foreach (var file in Directory.EnumerateFiles(folder, "*.bacs"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var dto = JsonSerializer.Deserialize<SchemeDto>(json);
                    if (dto == null) continue;

                    results.Add(new SchemeSummary(
                        string.IsNullOrWhiteSpace(dto.SchemeName) ? Path.GetFileNameWithoutExtension(file) : dto.SchemeName,
                        dto.CategoryName,
                        dto.ParameterName,
                        Path.GetFileName(file)));
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"ColorSchemeLibraryService: skipped unreadable scheme file '{file}'", ex);
                }
            }

            return results
                .OrderBy(s => s.SchemeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static SchemeDto LoadScheme(Document doc, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentNullException(nameof(fileName));

            var folder = ResolveSchemesFolder(doc, createIfMissing: false) ?? throw new InvalidOperationException("Could not resolve a color scheme library location for this document.");
            var path = Path.Combine(folder, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Scheme file '{fileName}' was not found in '{folder}'.", path);

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<SchemeDto>(json);
            return dto == null ? throw new InvalidDataException($"Scheme file '{fileName}' is empty or invalid.") : dto;
        }

        // Returns the actual file name written, since the scheme's display name
        // and its file name on disk are sanitized/deduplicated independently. // <- NEW
        public static string SaveScheme(Document doc, SchemeDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);
            if (string.IsNullOrWhiteSpace(dto.SchemeName))
                throw new InvalidOperationException("A scheme name is required before saving.");

            var folder = ResolveSchemesFolder(doc, createIfMissing: true) ?? throw new InvalidOperationException("Could not resolve or create a color scheme library location for this document.");
            string baseFileName = SanitizeFileName(dto.SchemeName);
            string fileName = MakeUniqueFileName(folder, baseFileName, dto.SchemeName);
            string path = Path.Combine(folder, fileName);

            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(path, json);

            return fileName;
        }

        public static void DeleteScheme(Document doc, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            var folder = ResolveSchemesFolder(doc, createIfMissing: false);
            if (folder == null) return;

            var path = Path.Combine(folder, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        // Existing file wins if its stored SchemeName already matches, so re-saving
        // the same named scheme overwrites rather than accumulating _1, _2, ... copies.
        // Only a genuine name collision with a *different* underlying scheme gets
        // deduplicated with a numeric suffix. // <- NEW
        private static string MakeUniqueFileName(string folder, string baseFileName, string schemeName)
        {
            string candidate = baseFileName + ".bacs";
            string candidatePath = Path.Combine(folder, candidate);

            if (!File.Exists(candidatePath))
                return candidate;

            try
            {
                var existingJson = File.ReadAllText(candidatePath);
                var existingDto = JsonSerializer.Deserialize<SchemeDto>(existingJson);
                if (existingDto != null &&
                    string.Equals(existingDto.SchemeName, schemeName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch
            {
                // Unreadable existing file, fall through to dedup rather than throw here.
            }

            for (int i = 1; i < 1000; i++)
            {
                candidate = $"{baseFileName}_{i}.bacs";
                candidatePath = Path.Combine(folder, candidate);
                if (!File.Exists(candidatePath))
                    return candidate;
            }

            throw new InvalidOperationException($"Could not generate a unique file name for scheme '{schemeName}'.");
        }

        private static string ResolveSchemesFolder(Document doc, bool createIfMissing)
        {
            string? projectSet = null;

            try
            {
                projectSet = ProjectSetService.GetProjectSetName(doc);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ColorSchemeLibraryService: ProjectSetService.GetProjectSetName threw", ex);
            }

            string primaryFolder = Path.Combine(NetworkRoot, projectSet ?? FallbackSubfolder);

            if (TryEnsureFolder(primaryFolder, createIfMissing, out var usablePrimary))
                return usablePrimary;

            string localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA", "ColorSchemes", projectSet ?? FallbackSubfolder);

            if (TryEnsureFolder(localRoot, createIfMissing, out var usableLocal))
                return usableLocal;

            return null;
        }

        // When createIfMissing is false (listing/loading), a folder that doesn't exist
        // yet is not an error, it just means no schemes have been saved there. When
        // createIfMissing is true (saving), failure to create is surfaced to the caller
        // as null so SaveScheme can fall back to the next candidate location. // <- NEW
        private static bool TryEnsureFolder(string folder, bool createIfMissing, out string usableFolder)
        {
            usableFolder = null;

            try
            {
                if (Directory.Exists(folder))
                {
                    usableFolder = folder;
                    return true;
                }

                if (!createIfMissing)
                    return false;

                Directory.CreateDirectory(folder);
                usableFolder = folder;
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogInfo($"ColorSchemeLibraryService: folder '{folder}' unusable ({ex.Message}).");
                return false;
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }
    }
}