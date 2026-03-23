using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Parameters
{
    public static class CategoryProvider
    {
        /// <summary>
        /// Returns categories safe for parameter binding.
        /// Default behavior matches your intent:
        /// - must allow bound parameters
        /// - exclude internal
        /// - (optionally) filter to CategoryType.Model only
        /// </summary>
        public static List<Category> GetBindableCategories(
            Document doc,
            bool onlyModelCategories = true)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // doc.Settings.Categories is Revit API -> MUST be called in a valid Revit API context.
            var cats = doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c != null)
                .Where(c => c.CategoryType != CategoryType.Internal)
                .Where(c => c.AllowsBoundParameters);

            if (onlyModelCategories)
                cats = cats.Where(c => c.CategoryType == CategoryType.Model);

            return cats
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}