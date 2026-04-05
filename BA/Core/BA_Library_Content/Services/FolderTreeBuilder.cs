using BA.Core.Content.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace BA.Core.Content.Services
{
    public static class FolderTreeBuilder
    {
        public static List<FolderNode> Build(IEnumerable<ContentItem> items)
        {
            Dictionary<string, FolderNode> roots = new(StringComparer.OrdinalIgnoreCase);

            foreach (ContentItem item in items)
            {
                string rootName = string.IsNullOrWhiteSpace(item.RootName) ? "Library" : item.RootName;

                if (!roots.TryGetValue(rootName, out FolderNode? root))
                {
                    root = new FolderNode
                    {
                        Name = rootName,
                        FullPath = rootName
                    };

                    roots[rootName] = root;
                }

                AddItemToTree(root, item);
            }

            return roots.Values.OrderBy(x => x.Name).ToList();
        }

        private static void AddItemToTree(FolderNode root, ContentItem item)
        {
            if (string.IsNullOrWhiteSpace(item.RelativePath))
                return;

            string[] segments = item.RelativePath
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
                return;

            FolderNode current = root;

            // Ignore the last segment if it is the file itself
            int folderCount = segments.Length;
            if (segments[^1].EndsWith(".rfa", StringComparison.OrdinalIgnoreCase) ||
                segments[^1].EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                folderCount--;
            }

            for (int i = 0; i < folderCount; i++)
            {
                string segment = segments[i];

                FolderNode? next = current.Children.FirstOrDefault(x =>
                    string.Equals(x.Name, segment, StringComparison.OrdinalIgnoreCase));

                if (next == null)
                {
                    next = new FolderNode
                    {
                        Name = segment,
                        FullPath = Path.Combine(current.FullPath, segment)
                    };

                    current.Children.Add(next);
                }

                current = next;
            }
        }
    }
}