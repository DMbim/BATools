using Autodesk.Revit.DB;
using BA.Subcategories.Models;
using System;
using System.Collections.Generic;
using Form = Autodesk.Revit.DB.Form;

namespace BA.Subcategories.Services
{
    public static class GraphicsAssignmentService
    {
        // ── Candidate filter ──────────────────────────────────────────────────

        public static bool IsFamilyGeometryCandidate(Element e)
        {
            if (e == null || e.Category == null) return false;
            if (IsExcludedCategory(e)) return false;

            if (e is CurveElement) return true;
            if (e is Form) return true;
            if (e is FamilyInstance) return true;
            if (e is FreeFormElement) return true;
            if (e is DirectShape) return true;

            return e.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY) != null;
        }

        // ── Read current subcategory ──────────────────────────────────────────

        public static string GetSubcategoryName(Document doc, Element e)
        {
            // CurveElement stores subcategory as LineStyle
            if (e is CurveElement ce)
            {
                var ls = ce.LineStyle as GraphicsStyle;
                return ls?.GraphicsStyleCategory?.Name ?? string.Empty;
            }

            // Standard elements use FAMILY_ELEM_SUBCATEGORY parameter
            Parameter? p = e.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
            if (p != null)
            {
                var id = p.AsElementId();
                if (id != ElementId.InvalidElementId)
                {
                    var gs = doc.GetElement(id) as GraphicsStyle;
                    var name = gs?.GraphicsStyleCategory?.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }

            // Reflection fallback for types that expose Subcategory property
            try
            {
                var pi = e.GetType().GetProperty("Subcategory");
                if (pi != null)
                {
                    var cat = pi.GetValue(e) as Category;
                    var name = cat?.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            catch { }

            return string.Empty;
        }

        // ── Apply ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Assigns targetSubcat to elements according to scope.
        /// Must be called inside an active Transaction.
        /// Returns count of changed elements.
        /// </summary>
        public static int ApplySubcategoryToFamilyGeometry(
            Document doc,
            Category targetSubcat,
            IEnumerable<FamilyGeometryRow> rows,
            ApplyScope scope,
            List<string> log)
        {
            if (doc == null || targetSubcat == null || rows == null) return 0;

            int count = 0;
            GraphicsStyle? targetGs = SubcategoryService.GetProjectionGraphicsStyle(doc, targetSubcat);

            foreach (var row in rows)
            {
                var e = doc.GetElement(row.Id);
                if (e == null || !IsFamilyGeometryCandidate(e)) continue;

                bool currentHasSub = !string.IsNullOrWhiteSpace(GetSubcategoryName(doc, e));

                bool include = scope switch
                {
                    ApplyScope.All                  => true,
                    ApplyScope.AllSelected          => row.IsSelected,
                    ApplyScope.AllWithNoSubcategory => !currentHasSub,
                    ApplyScope.AllButSelected       => !row.IsSelected,
                    _                               => false
                };

                if (!include) continue;

                bool changed = false;

                if (e is CurveElement ce)
                {
                    if (targetGs != null && ce.LineStyle?.Id != targetGs.Id)
                    {
                        try { ce.LineStyle = targetGs; changed = true; }
                        catch { /* element may be locked */ }
                    }
                }
                else
                {
                    Parameter? p = e.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                    if (p != null && !p.IsReadOnly)
                    {
                        try
                        {
                            if (p.AsElementId() != targetSubcat.Id)
                            {
                                p.Set(targetSubcat.Id);
                                changed = true;
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        // Reflection fallback
                        try
                        {
                            var pi = e.GetType().GetProperty("Subcategory");
                            if (pi != null && pi.CanWrite)
                            {
                                var current = pi.GetValue(e) as Category;
                                if (current?.Id != targetSubcat.Id)
                                {
                                    pi.SetValue(e, targetSubcat);
                                    changed = true;
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (changed)
                {
                    row.SubcategoryName = targetSubcat.Name;
                    count++;
                }
            }

            log.Add($"Assigned '{targetSubcat.Name}' to {count} element(s).");
            return count;
        }

        // ── Excluded categories ───────────────────────────────────────────────

        private static readonly HashSet<long> ExcludedBicValues = new()
        {
            (long)BuiltInCategory.OST_Levels,
            (long)BuiltInCategory.OST_Grids,
            (long)BuiltInCategory.OST_ReferencePoints_Planes,
            (long)BuiltInCategory.OST_CenterLines,
            (long)BuiltInCategory.OST_Dimensions,
            (long)BuiltInCategory.OST_Constraints,
            (long)BuiltInCategory.OST_SketchLines,
        };

        private static bool IsExcludedCategory(Element e)
        {
            if (e.Category == null) return false;
            try { return ExcludedBicValues.Contains(e.Category.Id.Value); }
            catch { return false; }
        }
    }
}
