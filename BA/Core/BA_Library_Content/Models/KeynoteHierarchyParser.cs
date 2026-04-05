using BA.Core.Content.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace BA.Core.Content.Services
{
    public sealed class KeynoteHierarchyParser
    {
        public IReadOnlyList<ClassificationNode> ParseFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("Keynote hierarchy file not found.", path);

            var lines = File.ReadAllLines(path);
            return ParseLines(lines);
        }

        public IReadOnlyList<ClassificationNode> ParseLines(IEnumerable<string> lines)
        {
            if (lines == null)
                throw new ArgumentNullException(nameof(lines));

            Dictionary<string, ClassificationNode> all = new(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                string[] parts = line.Split('\t');
                if (parts.Length < 2)
                    continue;

                string code = (parts[0] ?? string.Empty).Trim();
                string name = (parts[1] ?? string.Empty).Trim();
                string parentCode = parts.Length >= 3 ? (parts[2] ?? string.Empty).Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(code))
                    continue;

                string level = DetectLevel(code);

                if (!all.TryGetValue(code, out ClassificationNode? node))
                {
                    node = new ClassificationNode();
                    all[code] = node;
                }

                node.Code = code;
                node.Name = name;
                node.ParentCode = parentCode;
                node.Level = level;
            }

            // Build tree
            List<ClassificationNode> roots = new();

            foreach (ClassificationNode node in all.Values.OrderBy(x => x.Code))
            {
                if (string.IsNullOrWhiteSpace(node.ParentCode) || !all.TryGetValue(node.ParentCode, out ClassificationNode? parent))
                {
                    roots.Add(node);
                }
                else
                {
                    parent.Children.Add(node);
                }
            }

            return roots
                .OrderBy(x => x.Code)
                .ToList();
        }

        private static string DetectLevel(string code)
        {
            if (!code.Contains(".") && !code.Contains("-"))
                return "Domain";

            if (code.Contains(".") && !code.Contains("-"))
                return "Group";

            if (code.Contains("-"))
                return "Type";

            return "Unknown";
        }
    }
}