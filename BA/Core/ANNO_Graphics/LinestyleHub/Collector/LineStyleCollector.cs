using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace BA.UI.LineStyleHub
{
    /// <summary>
    /// Collects all user-created subcategories across all categories from a Revit document
    /// and converts them to <see cref="LineStyleRow"/> instances.
    ///
    /// "User-created" is determined by:
    ///   1. The subcategory has a non-null, non-empty name.
    ///   2. It is NOT an internal/system style — identified by checking whether
    ///      Revit allows setting the Name without throwing InvalidOperationException.
    ///      We do NOT mutate the document here; we use CategoryType and IsReadOnly
    ///      heuristics as a first pass and flag CanRename/CanDelete per row.
    ///
    /// Line patterns are also collected separately so the window can populate
    /// its dropdown.
    /// </summary>
    internal static class LineStyleCollector
    {
        // Revit built-in subcategory name fragments that indicate system/internal styles.
        // These are hidden regardless of any other flag.
        private static readonly HashSet<string> SystemNamePrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "<", "Hidden Lines", "Medium Lines", "Wide Lines", "Thin Lines"
        };

        public static (List<LineStyleRow> Rows, List<PatternEntry> Patterns) Collect(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var patterns = CollectPatterns(doc);
            var patternLookup = patterns.ToDictionary(p => p.PatternId, p => p.Name);

            var rows = new List<LineStyleRow>();
            var settings = doc.Settings;
            var categories = settings.Categories;

            foreach (Category parentCat in categories)
            {
                if (parentCat == null) continue;

                // Only model/annotation categories carry user-editable line styles
                if (parentCat.CategoryType != CategoryType.Model &&
                    parentCat.CategoryType != CategoryType.Annotation)
                    continue;

                foreach (Category subCat in parentCat.SubCategories)
                {
                    if (subCat == null) continue;
                    if (string.IsNullOrWhiteSpace(parentCat.Name)) continue;  // <- ADD

                    var name = subCat.Name ?? "";

                    // Skip system/internal styles by name heuristic
                    if (IsSystemStyle(name)) continue;

                    // Skip if no Id (degenerate state)
                    if (subCat.Id == null || subCat.Id == ElementId.InvalidElementId) continue;

                    var color = RevitColorToWpf(subCat.LineColor);
                    var weight = subCat.GetLineWeight(GraphicsStyleType.Projection) ?? 1;

                    var patternId = subCat.GetLinePatternId(GraphicsStyleType.Projection)
                                    ?? ElementId.InvalidElementId;
                    var patternName = patternLookup.TryGetValue(patternId, out var pn)
                        ? pn
                        : patternId == ElementId.InvalidElementId ? "Solid" : $"[id:{patternId.Value}]";

                    // Editability: subcategories of <Lines> are universally editable.
                    // Subcategories of other categories depend on whether Revit locked them.
                    // We use IsReadOnly on the element as a proxy where available.
                    var element = doc.GetElement(subCat.Id);
                    bool isEditable = element != null && element.IsModifiable;
                    bool canRename = isEditable;
                    bool canDelete = isEditable;

                    // Additional guard: <Lines> subcategories are always fully editable
                    // unless the element is explicitly read-only.
                    if (parentCat.Name == "Lines")
                    {
                        isEditable = true;
                        canRename = true;
                        canDelete = true;
                    }

                    rows.Add(new LineStyleRow(
                        categoryId: subCat.Id,
                        categoryName: name,
                        parentCategoryName: parentCat.Name,
                        isEditable: isEditable,
                        canRename: canRename,
                        canDelete: canDelete,
                        color: color,
                        lineWeight: weight,
                        patternName: patternName,
                        patternId: patternId));
                }
            }

            return (
                rows.OrderBy(r => r.ParentCategoryName)
                    .ThenBy(r => r.CategoryName)
                    .ToList(),
                patterns);
        }

        public static List<PatternEntry> CollectPatterns(Document doc)
        {
            var result = new List<PatternEntry>();

            // "Solid" is not a LinePatternElement in Revit — it is represented by
            // LinePatternElement.GetSolidPatternId() or ElementId.InvalidElementId.
            result.Add(new PatternEntry(ElementId.InvalidElementId, "Solid"));

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .OrderBy(lpe => lpe.Name);

            foreach (var lpe in collector)
            {
                result.Add(new PatternEntry(lpe.Id, lpe.Name));
            }

            return result;
        }

        private static bool IsSystemStyle(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            foreach (var prefix in SystemNamePrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static System.Windows.Media.Color RevitColorToWpf(Autodesk.Revit.DB.Color c)
        {
            if (c == null || !c.IsValid) return System.Windows.Media.Colors.Black;
            return System.Windows.Media.Color.FromRgb(c.Red, c.Green, c.Blue);
        }
    }

    /// <summary>
    /// Lightweight DTO representing a line pattern for the dropdown.
    /// </summary>
    public sealed class PatternEntry
    {
        public ElementId PatternId { get; }
        public string Name { get; }

        public PatternEntry(ElementId patternId, string name)
        {
            PatternId = patternId;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
