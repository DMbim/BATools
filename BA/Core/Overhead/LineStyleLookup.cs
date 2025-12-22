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

        public static GraphicsStyle? FindOverhead(Document doc)
        {
            if (doc != null && _lastDoc == doc && _lastOverhead != null && _lastOverhead.IsValidObject)
                return _lastOverhead;

            var gs = FindByNames(doc, new[] { "<Overhead>", "Overhead" });
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
    }
}
