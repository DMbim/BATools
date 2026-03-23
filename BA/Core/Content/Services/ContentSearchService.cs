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
            string approvalState,
            string rootName,
            bool favoritesOnly)
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

            if (!string.IsNullOrWhiteSpace(approvalState) && approvalState != "All")
                query = query.Where(x => string.Equals(x.ApprovalState, approvalState, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(rootName) && rootName != "All")
                query = query.Where(x => string.Equals(x.RootName, rootName, StringComparison.OrdinalIgnoreCase));

            if (favoritesOnly)
                query = query.Where(x => x.IsFavorite);

            return query
                .OrderByDescending(x => x.LastUsedUtc ?? DateTime.MinValue)
                .ThenBy(x => x.Category)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }
    }
}