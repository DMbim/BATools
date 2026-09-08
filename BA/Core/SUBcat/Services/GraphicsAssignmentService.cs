using Autodesk.Revit.DB;
using BA.Core.Standards;
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
            if (e == null) return false; // <- CHANGED, Category is no longer checked here

            // GenericForm covers Extrusion, Blend, Revolution, Sweep, SweptBlend,
            // the standard family solid and void modeling classes. Form is the
            // older conceptual mass and adaptive component blend and loft class.
            // Element.Category mirrors the assigned subcategory for these types
            // and is null until one has actually been assigned, so Category must
            // never gate them out, that is exactly the case this filter exists
            // to surface. <- NEW block
            bool isKnownGeometryType =
                e is CurveElement ||
                e is GenericForm ||
                e is Form ||
                e is FreeFormElement ||
                e is FamilyInstance ||
                e is DirectShape;

            if (!isKnownGeometryType)
            {
                // Outside the known geometry types, Category is a legitimate
                // filter, an element with no category here is not family editor
                // geometry this tool understands.
                if (e.Category == null) return false;
                if (IsExcludedCategory(e)) return false;
                return e.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY) != null;
            }

            // IsExcludedCategory is null safe on Category, safe to call even
            // when the known geometry type currently has no category assigned.
            if (IsExcludedCategory(e)) return false;

            return true;
        }

        // ── Read current subcategory ──────────────────────────────────────────

        public static string GetSubcategoryName(Document doc, Element e)
        {
            if (e is CurveElement ce)
            {
                var ls = ce.LineStyle as GraphicsStyle;
                return ls?.GraphicsStyleCategory?.Name ?? string.Empty;
            }

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

                string currentSubName = GetSubcategoryName(doc, e);
                bool currentHasBaSub = BaSubcategoryRules.IsBaName(currentSubName);

                bool include = scope switch
                {
                    ApplyScope.All => true,
                    ApplyScope.AllSelected => row.IsSelected,
                    ApplyScope.AllWithNoSubcategory => !currentHasBaSub,
                    ApplyScope.AllButSelected => !row.IsSelected,
                    _ => false
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