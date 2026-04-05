using BA.Core.Content.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Content.Services
{
    public sealed class ContentSearchService
    {
        public IReadOnlyList<ContentItem> Filter(
            IEnumerable<ContentItem> source,
            string searchText,
            string category,
            string rootName,
            bool favoritesOnly,
            ClassificationNode? selectedClassificationNode,
            FolderNode? selectedFolderNode)
        {
            if (source == null)
                return Array.Empty<ContentItem>();

            IEnumerable<ContentItem> query = source;

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string s = searchText.Trim().ToLowerInvariant();
                query = query.Where(x => x.SearchBlob.Contains(s));
            }

            if (!string.IsNullOrWhiteSpace(category) && category != "All")
                query = query.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(rootName) && rootName != "All")
                query = query.Where(x => string.Equals(x.RootName, rootName, StringComparison.OrdinalIgnoreCase));

            if (favoritesOnly)
                query = query.Where(x => x.IsFavorite);

            if (selectedClassificationNode != null && !string.IsNullOrWhiteSpace(selectedClassificationNode.Code))
            {
                if (selectedClassificationNode.Level == "Domain")
                {
                    query = query.Where(x => string.Equals(x.ClassDomainCode, selectedClassificationNode.Code, StringComparison.OrdinalIgnoreCase));
                }
                else if (selectedClassificationNode.Level == "Group")
                {
                    query = query.Where(x => string.Equals(x.ClassGroupCode, selectedClassificationNode.Code, StringComparison.OrdinalIgnoreCase));
                }
                else if (selectedClassificationNode.Level == "Type")
                {
                    query = query.Where(x => string.Equals(x.ClassCode, selectedClassificationNode.Code, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (selectedFolderNode != null && !string.IsNullOrWhiteSpace(selectedFolderNode.FullPath))
            {
                string folderPath = selectedFolderNode.FullPath.Replace('/', '\\');

                query = query.Where(x =>
                {
                    string itemPath = $"{x.RootName}\\{x.RelativePath}".Replace('/', '\\');
                    return itemPath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase);
                });
            }

            return query
                .OrderByDescending(x => x.LastUsedUtc ?? DateTime.MinValue)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }
    }
}