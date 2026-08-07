// File: BA.Core/ViewFilters/FilterGroupLibraryService.cs
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
    // Mirrors ColorSchemeLibraryService's project-set-scoped network share pattern
    // exactly, sibling folder under the same BA_tools root, separate subtree and
    // extension so the two libraries never collide on disk. // <- NEW
    public static class FilterGroupLibraryService
    {
        private const string NetworkRoot = @"S:\CAD\Autodesk Revit\_admin\BA_tools\FilterGroups";
        private const string FallbackSubfolder = "Default";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public static IReadOnlyList<FilterGroupSummary> ListGroups(Document doc)
        {
            var folder = ResolveGroupsFolder(doc, createIfMissing: false);
            if (folder == null || !Directory.Exists(folder))
                return Array.Empty<FilterGroupSummary>();

            var results = new List<FilterGroupSummary>();

            foreach (var file in Directory.EnumerateFiles(folder, "*.bafg"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var dto = JsonSerializer.Deserialize<FilterGroupDto>(json);
                    if (dto == null) continue;

                    results.Add(new FilterGroupSummary(
                        string.IsNullOrWhiteSpace(dto.GroupName) ? Path.GetFileNameWithoutExtension(file) : dto.GroupName,
                        dto.FilterNames?.Count ?? 0,
                        Path.GetFileName(file)));
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"FilterGroupLibraryService: skipped unreadable group file '{file}'", ex);
                }
            }

            return [.. results.OrderBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase)];
        }

        public static FilterGroupDto LoadGroup(Document doc, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentNullException(nameof(fileName));

            var folder = ResolveGroupsFolder(doc, createIfMissing: false) ?? throw new InvalidOperationException("Could not resolve a filter group library location for this document.");
            var path = Path.Combine(folder, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Filter group file '{fileName}' was not found in '{folder}'.", path);

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<FilterGroupDto>(json);
            return dto ?? throw new InvalidDataException($"Filter group file '{fileName}' is empty or invalid.");
        }

        public static string SaveGroup(Document doc, FilterGroupDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);
            if (string.IsNullOrWhiteSpace(dto.GroupName))
                throw new InvalidOperationException("A group name is required before saving.");
            if (dto.FilterNames == null || dto.FilterNames.Count == 0)
                throw new InvalidOperationException("A group needs at least one filter.");

            string folder = ResolveGroupsFolder(doc, createIfMissing: true) ?? throw new InvalidOperationException("Could not resolve or create a filter group library location for this document.");
            string baseFileName = SanitizeFileName(dto.GroupName);
            string fileName = MakeUniqueFileName(folder, baseFileName, dto.GroupName);
            string path = Path.Combine(folder, fileName);

            var json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(path, json);

            return fileName;
        }

        public static void DeleteGroup(Document doc, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            var folder = ResolveGroupsFolder(doc, createIfMissing: false);
            if (folder == null) return;

            var path = Path.Combine(folder, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string MakeUniqueFileName(string folder, string baseFileName, string groupName)
        {
            string candidate = baseFileName + ".bafg";
            string candidatePath = Path.Combine(folder, candidate);

            if (!File.Exists(candidatePath))
                return candidate;

            try
            {
                var existingJson = File.ReadAllText(candidatePath);
                var existingDto = JsonSerializer.Deserialize<FilterGroupDto>(existingJson);
                if (existingDto != null &&
                    string.Equals(existingDto.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch
            {
                // Unreadable existing file, fall through to dedup.
            }

            for (int i = 1; i < 1000; i++)
            {
                candidate = $"{baseFileName}_{i}.bafg";
                candidatePath = Path.Combine(folder, candidate);
                if (!File.Exists(candidatePath))
                    return candidate;
            }

            throw new InvalidOperationException($"Could not generate a unique file name for group '{groupName}'.");
        }

        private static string? ResolveGroupsFolder(Document doc, bool createIfMissing)
        {
            string? projectSet = null;

            try
            {
                projectSet = ProjectSetService.GetProjectSetName(doc);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FilterGroupLibraryService: ProjectSetService.GetProjectSetName threw", ex);
            }

            string primaryFolder = Path.Combine(NetworkRoot, projectSet ?? FallbackSubfolder);

            if (TryEnsureFolder(primaryFolder, createIfMissing, out var usablePrimary))
                return usablePrimary;

            string localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BA", "FilterGroups", projectSet ?? FallbackSubfolder);

            if (TryEnsureFolder(localRoot, createIfMissing, out var usableLocal))
                return usableLocal;

            return null;
        }

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
                AppLogger.LogInfo($"FilterGroupLibraryService: folder '{folder}' unusable ({ex.Message}).");
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