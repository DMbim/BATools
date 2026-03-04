using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace BA.Core.Overhead
{
    public static class LineStyleLookup
    {
        private static Document? _lastDoc;
        private static GraphicsStyle? _lastOverhead;

        public static GraphicsStyle? FindByNames(Document doc, IEnumerable<string> candidateNames)
        {
            if (doc == null) return null;

            var linesCat = doc.Settings?.Categories?.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null) return null;

            var subs = linesCat.SubCategories;
            if (subs == null) return null;

            var nameSet = new HashSet<string>(
                (candidateNames ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(Normalize),
                StringComparer.InvariantCultureIgnoreCase);

            foreach (Category sub in subs)
            {
                var n = Normalize(sub?.Name ?? string.Empty);
                if (nameSet.Contains(n))
                    return sub.GetGraphicsStyle(GraphicsStyleType.Projection);

                foreach (var target in nameSet)
                    if (!string.IsNullOrEmpty(n) && n.Contains(target))
                        return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }

            return null;
        }
        public static string DumpGraphicsStylesByName(Document doc, string name)
        {
            var styles = new FilteredElementCollector(doc)
                .OfClass(typeof(GraphicsStyle))
                .Cast<GraphicsStyle>()
                .Where(gs => string.Equals(gs.Name ?? "", name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (styles.Count == 0) return $"No GraphicsStyle named '{name}'";

            return string.Join("\n", styles.Select(gs =>
            {
                var c = gs.GraphicsStyleCategory;
                return $"GS Id={gs.Id.Value} Name={gs.Name} GSCatId={(c?.Id?.Value ?? -1)} Parent={(c?.Parent?.Name ?? "(none)")}";
            }));
        }
        public static GraphicsStyle? FindOverhead(Document doc)
        {
            if (doc != null && _lastDoc == doc && _lastOverhead != null && _lastOverhead.IsValidObject)
                return _lastOverhead;

            var gs = FindByNames(doc, new[] { "BA_Overhead", "BA_Overhead_BT" });
            _lastDoc = doc;
            _lastOverhead = gs;
            return gs;
        }

        private static string Normalize(string name)
        {
            var s = (name ?? string.Empty)
                .Replace("<", string.Empty)
                .Replace(">", string.Empty)
                .Trim()
                .ToLowerInvariant();

            return RemoveDiacritics(s);
        }
        public static IReadOnlyList<Category> GetAllLinesSubcategoriesByName(Document doc, string subCategoryName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(subCategoryName))
                throw new ArgumentException("Subcategory name is empty.", nameof(subCategoryName));

            Category? linesRoot = null;
            try { linesRoot = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines); }
            catch { return Array.Empty<Category>(); }

            if (linesRoot == null) return Array.Empty<Category>();

            var matches = new List<Category>();
            var stack = new Stack<Category>();
            stack.Push(linesRoot);

            while (stack.Count > 0)
            {
                var c = stack.Pop();
                if (c == null) continue;

                // match (skip root itself)
                if (c.Id != linesRoot.Id &&
                    string.Equals(c.Name ?? "", subCategoryName, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(c);
                }

                // recurse
                CategoryNameMap? subs = null;
                try { subs = c.SubCategories; } catch { subs = null; }
                if (subs == null) continue;

                foreach (Category sub in subs)
                    if (sub != null)
                        stack.Push(sub);
            }

            return matches;
        }

        public static IReadOnlyList<Category> GetAllLinesSubcategoriesOrThrow(Document doc, string subCategoryName)
        {
            var cats = GetAllLinesSubcategoriesByName(doc, subCategoryName);
            if (cats.Count == 0)
                throw new InvalidOperationException(
                    $"Line subcategory '{subCategoryName}' not found under Model Categories → Lines (OST_Lines).");
            return cats;
        }
        private static string RemoveDiacritics(string text)
        {
            var norm = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(norm.Length);
            foreach (var ch in norm)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
        public static Category? TryGetLinesSubcategory(Document doc, string subCategoryName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(subCategoryName))
                throw new ArgumentException("Subcategory name is empty.", nameof(subCategoryName));

            Category? linesCat = null;

            try
            {
                linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            }
            catch
            {
                return null;
            }

            if (linesCat == null) return null;

            CategoryNameMap? subs = null;
            try { subs = linesCat.SubCategories; } catch { subs = null; }
            if (subs == null) return null;

            foreach (Category c in subs)
            {
                if (c == null) continue;
                if (string.Equals(c.Name ?? "", subCategoryName, StringComparison.OrdinalIgnoreCase))
                    return c;
            }

            return null;
        }
        public static IReadOnlyList<Category> GetLineStyleCategoriesByGraphicsStyleName(Document doc, string lineStyleName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(lineStyleName))
                throw new ArgumentException("Line style name is empty.", nameof(lineStyleName));

            // GraphicsStyle elements represent line styles (and other style categories).
            // We filter by name and take their GraphicsStyleCategory.
            var styles = new FilteredElementCollector(doc)
                .OfClass(typeof(GraphicsStyle))
                .Cast<GraphicsStyle>()
                .Where(gs => gs != null && string.Equals(gs.Name ?? "", lineStyleName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Map to categories and de-duplicate by Id
            var cats = styles
                .Select(gs => gs.GraphicsStyleCategory)
                .Where(c => c != null && c.Id != null && c.Id != ElementId.InvalidElementId)
                .GroupBy(c => c.Id.Value)
                .Select(g => g.First())
                .ToList();

            return cats;
        }

        public static IReadOnlyList<Category> GetLineStyleCategoriesOrThrow(Document doc, string lineStyleName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(lineStyleName))
                throw new ArgumentException("Line style name is empty.", nameof(lineStyleName));

            var styles = new FilteredElementCollector(doc)
                .OfClass(typeof(GraphicsStyle))
                .Cast<GraphicsStyle>()
                .Where(gs => gs != null && string.Equals(gs.Name ?? "", lineStyleName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var cats = styles
                .Select(gs => gs.GraphicsStyleCategory)
                .Where(c => c != null && c.Id != null && c.Id != ElementId.InvalidElementId)
                .GroupBy(c => c.Id.Value)
                .Select(g => g.First())
                .ToList();

            if (cats.Count == 0)
                throw new InvalidOperationException($"GraphicsStyle '{lineStyleName}' not found.");

            return cats;
        }
    }
}
