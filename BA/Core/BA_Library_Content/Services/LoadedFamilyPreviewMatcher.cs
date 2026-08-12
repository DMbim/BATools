using System;
using System.Collections.Generic;
using System.Linq;
using BA.Core.Content.Models;

namespace BA.Core.Content.Services
{
    public static class LoadedFamilyPreviewMatcher
    {
        /// <summary>
        /// Matches a loaded Family's name against the disk-indexed library
        /// (by file name without extension, case-insensitive). Returns an
        /// empty string when there is no match, in which case the UI should
        /// show a placeholder rather than attempting export.
        /// </summary>
        public static string Match(string familyName, IReadOnlyList<ContentItem>? libraryIndex)
        {
            if (string.IsNullOrWhiteSpace(familyName) || libraryIndex == null || libraryIndex.Count == 0)
                return string.Empty;

            var match = libraryIndex.FirstOrDefault(item =>
                string.Equals(
                    System.IO.Path.GetFileNameWithoutExtension(item.FileName),
                    familyName,
                    StringComparison.OrdinalIgnoreCase))
                ?? libraryIndex.FirstOrDefault(item =>
                    string.Equals(item.DisplayName, familyName, StringComparison.OrdinalIgnoreCase));

            return match != null && !string.IsNullOrWhiteSpace(match.PreviewPath)
                ? match.PreviewPath
                : string.Empty;
        }
    }
}