using Autodesk.Revit.DB;
using BA.Subcategories.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using WpfColor = System.Windows.Media.Color;

namespace BA.Subcategories.Services
{
    /// <summary>
    /// All Revit API operations for subcategory CRUD and appearance.
    /// Every method must be called inside an active Transaction.
    /// </summary>
    public static class SubcategoryService
    {
        // ── Read ──────────────────────────────────────────────────────────────

        public static Dictionary<string, Category> GetExistingSubcategories(Category parent)
        {
            var dict = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            if (parent?.SubCategories == null) return dict;

            foreach (Category sub in parent.SubCategories)
                if (sub != null && !string.IsNullOrEmpty(sub.Name))
                    dict[sub.Name] = sub;

            return dict;
        }

        /// <summary>
        /// Builds a SubcategoryRow list from existing subcategories on the parent,
        /// pre-populated with their current color and line weight.
        /// </summary>
        public static List<SubcategoryRow> BuildRows(Document doc, Category parent)
        {
            var rows = new List<SubcategoryRow>();
            if (parent?.SubCategories == null) return rows;

            foreach (Category sub in parent.SubCategories)
            {
                if (sub == null || string.IsNullOrEmpty(sub.Name)) continue;

                var row = new SubcategoryRow
                {
                    CategoryId = sub.Id,
                    Name = sub.Name,
                    LineWeight = ReadLineWeight(sub),
                    LineColor = ReadLineColor(sub),
                    IsDirty = false
                };
                rows.Add(row);
            }

            return rows.OrderBy(r => r.Name).ToList();
        }

        // ── Create ────────────────────────────────────────────────────────────

        public static Category? CreateSubcategory(
            Document doc,
            Category parent,
            string name,
            List<string> log)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            try
            {
                var created = doc.Settings.Categories.NewSubcategory(parent, name);
                log.Add($"Created: {name}");
                return created;
            }
            catch (Exception ex)
            {
                log.Add($"Error creating '{name}': {ex.Message}");
                return null;
            }
        }

        // ── Ensure (create if missing) ────────────────────────────────────────

        public static Dictionary<string, Category> EnsureSubcategories(
            Document doc,
            Category parent,
            IEnumerable<string> names,
            List<string> log)
        {
            var map = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || parent == null) return map;

            var existing = GetExistingSubcategories(parent);

            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (existing.TryGetValue(name, out var found))
                {
                    map[name] = found;
                }
                else
                {
                    var created = CreateSubcategory(doc, parent, name, log);
                    if (created != null) map[name] = created;
                }
            }

            return map;
        }

        // ── Delete ────────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes a subcategory by ElementId.
        /// Revit only allows deleting subcategories that are not in use;
        /// the API will throw if geometry is still assigned.
        /// Returns true on success.
        /// </summary>
        public static bool DeleteSubcategory(Document doc, ElementId categoryId, List<string> log)
        {
            try
            {
                // Category elements are deleted via Document.Delete
                var ids = doc.Delete(categoryId);
                log.Add($"Deleted subcategory (id {categoryId.Value}).");
                return ids != null && ids.Count > 0;
            }
            catch (Exception ex)
            {
                log.Add($"Could not delete subcategory {categoryId.Value}: {ex.Message}");
                return false;
            }
        }

        // ── Appearance ────────────────────────────────────────────────────────

        public static void ApplyAppearance(
            Document doc,
            Category subcat,
            SubcategoryRow row,
            List<string> log)
        {
            if (subcat == null || row == null) return;

            try
            {
                // Line color
                var revitColor = new Autodesk.Revit.DB.Color(
                    row.LineColor.R,
                    row.LineColor.G,
                    row.LineColor.B);
                subcat.LineColor = revitColor;
            }
            catch (Exception ex)
            {
                log.Add($"Color error on '{subcat.Name}': {ex.Message}");
            }

            try
            {
                // Projection line weight
                subcat.SetLineWeight(row.LineWeight, GraphicsStyleType.Projection);
            }
            catch (Exception ex)
            {
                log.Add($"LineWeight error on '{subcat.Name}': {ex.Message}");
            }
        }

        // ── GraphicsStyle helper ──────────────────────────────────────────────

        public static GraphicsStyle? GetProjectionGraphicsStyle(Document doc, Category subcat)
        {
            if (doc == null || subcat == null) return null;
            try { return subcat.GetGraphicsStyle(GraphicsStyleType.Projection); }
            catch { return null; }
        }

        // ── Private readers ───────────────────────────────────────────────────

        private static int ReadLineWeight(Category sub)
        {
            try
            {
                int w = sub.GetLineWeight(GraphicsStyleType.Projection) ?? 1;
                return Math.Clamp(w, 1, 16);
            }
            catch { return 1; }
        }

        private static WpfColor ReadLineColor(Category sub)
        {
            try
            {
                var c = sub.LineColor;
                if (c != null && c.IsValid)
                    return WpfColor.FromRgb(c.Red, c.Green, c.Blue);
            }
            catch { }
            return WpfColor.FromRgb(0, 0, 0);
        }
    }
}
