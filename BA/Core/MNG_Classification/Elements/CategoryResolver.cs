using System;
using Autodesk.Revit.DB;

namespace BA.Core.Classification
{
    public static class CategoryResolver
    {
        /// <summary>
        /// Resolve category from:
        /// - BuiltInCategory enum name (recommended): "OST_Walls"
        /// - Category display name in the current Revit language (fallback)
        /// Returns InvalidElementId if not found.
        /// </summary>
        public static ElementId TryResolveCategoryId(Document doc, string categoryToken)
        {
            if (doc == null || string.IsNullOrWhiteSpace(categoryToken))
                return ElementId.InvalidElementId;

            categoryToken = categoryToken.Trim();

            // 1) Try BuiltInCategory enum name
            if (Enum.TryParse(categoryToken, ignoreCase: true, out BuiltInCategory bic))
            {
                var cat = Category.GetCategory(doc, bic);
                if (cat != null) return cat.Id;

            }

            var cats = doc.Settings.Categories;

            // 2) Exact display name
            foreach (Category c in cats)
            {
                if (c == null) continue;
                if (string.Equals(c.Name, categoryToken, StringComparison.OrdinalIgnoreCase))
                    return c.Id;
            }

            // 3) Contains (very loose)
            foreach (Category c in cats)
            {
                if (c == null) continue;
                if (c.Name?.IndexOf(categoryToken, StringComparison.OrdinalIgnoreCase) >= 0)
                    return c.Id;
            }

            return ElementId.InvalidElementId;
        }
    }
}
