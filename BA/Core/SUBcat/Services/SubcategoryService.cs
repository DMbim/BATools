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
        // ── Ownership check ──────────────────────────────────────────────────

        /// <summary>
        /// Real, user creatable subcategories, the ones created through
        /// NewSubcategory, always carry a positive, document owned ElementId.
        /// Negative ids represent built in, Revit reserved pseudo elements
        /// such as &lt;Hidden Lines&gt;, which cannot be deleted or renamed
        /// through the API, the same way Revit's own UI does not allow it.
        /// </summary>
        private static bool IsUserOwnedSubcategory(ElementId id) // <- NEW
        {
            return id != null && id != ElementId.InvalidElementId && id.Value > 0;
        }

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
        /// pre populated with their current color and line weight. Built in,
        /// Revit reserved styles such as &lt;Hidden Lines&gt; are skipped entirely,
        /// they cannot be deleted or renamed, so there is nothing this editor
        /// can meaningfully do with them. OriginalName is set to the live Revit
        /// name, callers must use it to look up the Category again later, never
        /// the possibly edited Name property.
        /// </summary>
        public static List<SubcategoryRow> BuildRows(Document doc, Category parent)
        {
            var rows = new List<SubcategoryRow>();
            if (parent?.SubCategories == null) return rows;

            foreach (Category sub in parent.SubCategories)
            {
                if (sub == null || string.IsNullOrEmpty(sub.Name)) continue;
                if (!IsUserOwnedSubcategory(sub.Id)) continue; // <- NEW

                var row = new SubcategoryRow
                {
                    CategoryId = sub.Id,
                    OriginalName = sub.Name,
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

        // ── Rename ────────────────────────────────────────────────────────────

        /// <summary>
        /// Renames an existing subcategory. Category.Name has no setter in the
        /// Revit API, the rename is written through the subcategory's Projection
        /// GraphicsStyle, which is what actually carries the display name.
        /// Returns true if the name already matches or the rename succeeded,
        /// false if it failed. Callers should leave the row marked dirty on
        /// failure so the edit is retried on the next Apply.
        /// </summary>
        public static bool RenameSubcategory(
            Document doc,
            Category subcat,
            string newName,
            List<string> log)
        {
            if (subcat == null || string.IsNullOrWhiteSpace(newName)) return false;

            if (!IsUserOwnedSubcategory(subcat.Id)) // <- NEW
            {
                log.Add($"Refused to rename '{subcat.Name}', it is a built in Revit reserved style.");
                return false;
            }

            string trimmed = newName.Trim();
            string oldName = subcat.Name;

            if (string.Equals(oldName, trimmed, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                GraphicsStyle? gs = subcat.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs == null)
                {
                    log.Add($"Rename failed for '{oldName}': no Projection GraphicsStyle found.");
                    return false;
                }

                gs.Name = trimmed;
                log.Add($"Renamed '{oldName}' to '{trimmed}'.");
                return true;
            }
            catch (Exception ex)
            {
                log.Add($"Rename error on '{oldName}' to '{trimmed}': {ex.Message}");
                return false;
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
        /// the API will throw if geometry is still assigned. Built in,
        /// Revit reserved styles are refused before ever calling Delete,
        /// that call cannot succeed for them and previously surfaced a
        /// raw ArgumentException instead of a clean log message.
        /// Returns true on success.
        /// </summary>
        public static bool DeleteSubcategory(Document doc, ElementId categoryId, List<string> log)
        {
            if (!IsUserOwnedSubcategory(categoryId)) // <- NEW
            {
                log.Add($"Refused to delete subcategory (id {categoryId?.Value}), it is a built in Revit reserved style and cannot be deleted through the API.");
                return false;
            }

            try
            {
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