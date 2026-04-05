using BA.Core.Content.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BA.Core.Content.Services
{
    public sealed class ContentIndexService
    {
        private readonly ContentBrowserSettings _settings;

        public ContentIndexService(ContentBrowserSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string GetIndexPath()
        {
            Directory.CreateDirectory(_settings.CacheFolderPath);
            return Path.Combine(_settings.CacheFolderPath, "content-index.json");
        }

        public IReadOnlyList<ContentItem> BuildIndex()
        {
            var items = new List<ContentItem>();

            foreach (var root in _settings.Roots.Where(r => r.IsEnabled))
            {
                if (string.IsNullOrWhiteSpace(root.RootPath) || !Directory.Exists(root.RootPath))
                    continue;

                var searchOption = root.IncludeSubfolders
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                if (_settings.IncludeRfa)
                {
                    foreach (var file in SafeEnumerateFiles(root.RootPath, "*.rfa", searchOption))
                    {
                        items.Add(BuildItem(root, file, ".rfa"));
                    }
                }

                if (_settings.IncludeRvt)
                {
                    foreach (var file in SafeEnumerateFiles(root.RootPath, "*.rvt", searchOption))
                    {
                        items.Add(BuildItem(root, file, ".rvt"));
                    }
                }
            }

            return items
                .OrderBy(x => x.RootName)
                .ThenBy(x => x.Category)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }

        public void SaveIndex(IReadOnlyList<ContentItem> items)
        {
            string path = GetIndexPath();
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(items, options);
            File.WriteAllText(path, json);
        }

        public IReadOnlyList<ContentItem> LoadIndex()
        {
            string path = GetIndexPath();
            if (!File.Exists(path))
                return new List<ContentItem>();

            try
            {
                string json = File.ReadAllText(path);
                List<ContentItem>? items = JsonSerializer.Deserialize<List<ContentItem>>(json);
                return items ?? new List<ContentItem>();
            }
            catch
            {
                return new List<ContentItem>();
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, SearchOption option)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var current = pending.Pop();

                string[] files = Array.Empty<string>();
                try
                {
                    files = Directory.GetFiles(current, pattern, SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                foreach (var file in files)
                    yield return file;

                if (option == SearchOption.AllDirectories)
                {
                    string[] dirs = Array.Empty<string>();
                    try
                    {
                        dirs = Directory.GetDirectories(current);
                    }
                    catch
                    {
                    }

                    foreach (var dir in dirs)
                        pending.Push(dir);
                }
            }
        }

        private ContentItem BuildItem(ContentLibraryRoot root, string fullPath, string extension)
        {
            var info = new FileInfo(fullPath);

            string relativePath = MakeRelativePath(root.RootPath, fullPath);
            string folderCategory = InferCategoryFromPath(relativePath);
            string approvalState = !string.IsNullOrWhiteSpace(root.ApprovalStateOverride)
                ? root.ApprovalStateOverride
                : InferApprovalStateFromPath(relativePath);

            string previewPath = FindPreviewPath(fullPath);
            string metadataPath = FindMetadataPath(fullPath);

            var item = new ContentItem
            {
                Id = ComputeId(fullPath),
                FileName = Path.GetFileName(fullPath),
                DisplayName = BuildDisplayName(fullPath),
                FullPath = fullPath,
                RelativePath = relativePath,
                RootName = root.Name,
                Extension = extension,
                Category = folderCategory,
                ApprovalState = approvalState,
                PreviewPath = previewPath,
                MetadataPath = metadataPath,
                CreatedUtc = info.CreationTimeUtc,
                ModifiedUtc = info.LastWriteTimeUtc,
                FileSizeBytes = info.Exists ? info.Length : 0
            };

            ReadSidecarMetadata(item);
            item.SearchBlob = BuildSearchBlob(item);
            return item;
        }

        private static string MakeRelativePath(string rootPath, string fullPath)
        {
            try
            {
                var rootUri = new Uri(AppendDirectorySeparator(rootPath));
                var fileUri = new Uri(fullPath);
                return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString())
                    .Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return fullPath;
            }
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            if (path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                return path;

            return path + Path.DirectorySeparatorChar;
        }

        private static string InferCategoryFromPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return "Uncategorized";

            string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length == 0)
                return "Uncategorized";

            foreach (var part in parts)
            {
                string p = part.Trim().ToLowerInvariant();

                if (p.Contains("door")) return "Doors";
                if (p.Contains("window")) return "Windows";
                if (p.Contains("annotation")) return "Annotations";
                if (p.Contains("tag")) return "Tags";
                if (p.Contains("detail")) return "Detail Items";
                if (p.Contains("furniture")) return "Furniture";
                if (p.Contains("generic")) return "Generic Models";
                if (p.Contains("profile")) return "Profiles";
                if (p.Contains("titleblock")) return "Titleblocks";
                if (p.Contains("struct")) return "Structural";
                if (p.Contains("mep")) return "MEP";
            }

            return parts[0];
        }

        private static string InferApprovalStateFromPath(string relativePath)
        {
            string lower = relativePath.ToLowerInvariant();

            if (lower.Contains("approved")) return "Approved";
            if (lower.Contains("wip")) return "WIP";
            if (lower.Contains("legacy")) return "Legacy";
            if (lower.Contains("deprecated")) return "Deprecated";

            return "Unspecified";
        }

        private string FindPreviewPath(string familyPath)
        {
            string basePath = Path.Combine(
                Path.GetDirectoryName(familyPath)!,
                Path.GetFileNameWithoutExtension(familyPath));

            if (_settings.IncludeImagePreviewPng)
            {
                string png = basePath + ".png";
                if (File.Exists(png))
                    return png;
            }

            if (_settings.IncludeImagePreviewJpg)
            {
                string jpg = basePath + ".jpg";
                if (File.Exists(jpg))
                    return jpg;

                string jpeg = basePath + ".jpeg";
                if (File.Exists(jpeg))
                    return jpeg;
            }

            return string.Empty;
        }

        private static string FindMetadataPath(string familyPath)
        {
            string json = Path.Combine(
                Path.GetDirectoryName(familyPath)!,
                Path.GetFileNameWithoutExtension(familyPath) + ".json");

            return File.Exists(json) ? json : string.Empty;
        }

        private static string BuildDisplayName(string fullPath)
        {
            string name = Path.GetFileNameWithoutExtension(fullPath);
            return name.Replace('_', ' ').Trim();
        }

        private static string ComputeId(string fullPath)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant());
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static void ReadSidecarMetadata(ContentItem item)
        {
            if (string.IsNullOrWhiteSpace(item.MetadataPath) || !File.Exists(item.MetadataPath))
                return;

            try
            {
                string json = File.ReadAllText(item.MetadataPath);
                var meta = JsonSerializer.Deserialize<ContentItemSidecar>(json);
                if (meta == null)
                    return;

                if (!string.IsNullOrWhiteSpace(meta.Title))
                    item.DisplayName = meta.Title;

                if (!string.IsNullOrWhiteSpace(meta.Category))
                    item.Category = meta.Category;

                if (!string.IsNullOrWhiteSpace(meta.ApprovalState))
                    item.ApprovalState = meta.ApprovalState;

                if (!string.IsNullOrWhiteSpace(meta.Description))
                    item.Description = meta.Description;

                if (!string.IsNullOrWhiteSpace(meta.Manufacturer))
                    item.Manufacturer = meta.Manufacturer;

                if (!string.IsNullOrWhiteSpace(meta.ClassDomainCode))
                    item.ClassDomainCode = meta.ClassDomainCode;

                if (!string.IsNullOrWhiteSpace(meta.ClassDomainName))
                    item.ClassDomainName = meta.ClassDomainName;

                if (!string.IsNullOrWhiteSpace(meta.ClassGroupCode))
                    item.ClassGroupCode = meta.ClassGroupCode;

                if (!string.IsNullOrWhiteSpace(meta.ClassGroupName))
                    item.ClassGroupName = meta.ClassGroupName;

                if (!string.IsNullOrWhiteSpace(meta.ClassCode))
                    item.ClassCode = meta.ClassCode;

                if (!string.IsNullOrWhiteSpace(meta.ClassName))
                    item.ClassName = meta.ClassName;

                if (meta.Tags != null)
                    item.Tags = meta.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();

                if (meta.Keywords != null)
                    item.Keywords = meta.Keywords.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            }
            catch
            {
            }
        }

        private static string BuildSearchBlob(ContentItem item)
        {
            var parts = new List<string>
    {
        item.DisplayName,
        item.FileName,
        item.RelativePath,
        item.RootName,
        item.Category,
        item.Description,
        item.Manufacturer,

        item.ClassDomainCode,
        item.ClassDomainName,
        item.ClassGroupCode,
        item.ClassGroupName,
        item.ClassCode,
        item.ClassName
    };

            parts.AddRange(item.Tags);
            parts.AddRange(item.Keywords);

            return string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)))
                .ToLowerInvariant();
        }

        private sealed class ContentItemSidecar
        {
            public string Title { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string ApprovalState { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Manufacturer { get; set; } = string.Empty;
            public List<string>? Tags { get; set; }
            public List<string>? Keywords { get; set; }
            public string ClassDomainCode { get; set; } = string.Empty;
            public string ClassDomainName { get; set; } = string.Empty;
            public string ClassGroupCode { get; set; } = string.Empty;
            public string ClassGroupName { get; set; } = string.Empty;
            public string ClassCode { get; set; } = string.Empty;
            public string ClassName { get; set; } = string.Empty;
        }
    }
}