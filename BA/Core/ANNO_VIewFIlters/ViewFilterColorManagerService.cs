// File: BA.Core/ViewFilters/ViewFilterColorManagerService.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB.Visual;

namespace BA.Core.ViewFilters
{
    public static class ViewFilterColorManagerService
    {
        public static ViewTemplateInfo[] GetViewTemplates(Document? doc)
        {
            if (doc == null) return Array.Empty<ViewTemplateInfo>();

            var templ = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v != null && v.IsTemplate)
                .OrderBy(v => v.Name)
                .Select(v => new ViewTemplateInfo(v.Id, v.Name, v.ViewType.ToString()))
                .ToArray();
            // Debugging
            System.Diagnostics.Debug.WriteLine($"Found {templ.Length} view templates:");
            foreach (var template in templ)
            {
                System.Diagnostics.Debug.WriteLine($"- Id: {template.Id}, Name: {template.Name}, ViewType: {template.ViewType}");
            }

            return templ;
        }

        public static FilterInfo[] GetTemplateFilters(Document? doc, ElementId templateId)
        {
            if (doc == null) return Array.Empty<FilterInfo>();

            var template = doc.GetElement(templateId) as View;
            if (template == null || !template.IsTemplate) return Array.Empty<FilterInfo>();

            var filterIds = template.GetFilters();
            if (filterIds == null || filterIds.Count == 0) return Array.Empty<FilterInfo>();

            var list = new List<FilterInfo>();

            foreach (var fid in filterIds)
            {
                var pfe = doc.GetElement(fid) as ParameterFilterElement;
                var name = pfe?.Name ?? "<missing filter>";

                var cats = pfe != null ? CategoriesToText(doc, pfe) : "—";
                bool vis = SafeBool(() => template.GetFilterVisibility(fid), true);

                var ogs = SafeOgs(() => template.GetFilterOverrides(fid));

                var cut = TryGetOgsColor(() => ogs.CutLineColor);
                var proj = TryGetOgsColor(() => ogs.ProjectionLineColor);

                list.Add(new FilterInfo(
                    fid, name, cats, vis,
                    cut.r, cut.g, cut.b,
                    proj.r, proj.g, proj.b));
            }

            return list.OrderBy(x => x.Name).ToArray();
        }

        public static void ApplyLineOverrides(Document doc, ElementId templateId, IList<FilterColorAssignment> assigns)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (assigns == null || assigns.Count == 0) return;

            var template = doc.GetElement(templateId) as View;
            if (template == null || !template.IsTemplate) return;

            var currentFilters = template.GetFilters() ?? new List<ElementId>();

            foreach (var a in assigns)
            {
                if (a.FilterId == null || a.FilterId == ElementId.InvalidElementId)
                    continue;

                if (!currentFilters.Contains(a.FilterId))
                {
                    // If you want to enforce “only existing filters”, remove this.
                    template.AddFilter(a.FilterId);
                    currentFilters = template.GetFilters();
                }

                var ogs = SafeOgs(() => template.GetFilterOverrides(a.FilterId));

                // Apply colors only if present (null => leave as-is)
                if (a.ProjR.HasValue && a.ProjG.HasValue && a.ProjB.HasValue)
                    ogs.SetProjectionLineColor(new Color(a.ProjR.Value, a.ProjG.Value, a.ProjB.Value));

                if (a.CutR.HasValue && a.CutG.HasValue && a.CutB.HasValue)
                    ogs.SetCutLineColor(new Color(a.CutR.Value, a.CutG.Value, a.CutB.Value));

                template.SetFilterOverrides(a.FilterId, ogs);
                template.SetFilterVisibility(a.FilterId, true);
            }
        }

        private static string CategoriesToText(Document doc, ParameterFilterElement pfe)
        {
            try
            {
                var ids = pfe.GetCategories();
                if (ids == null || ids.Count == 0) return "—";

                var names = ids
                    .Select(id => Category.GetCategory(doc, id)?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                if (names.Count == 0) return "—";
                return string.Join(", ", names.Take(3)) + (names.Count > 3 ? "…" : "");
            }
            catch
            {
                return "—";
            }
        }

        private static OverrideGraphicSettings SafeOgs(Func<OverrideGraphicSettings> getter)
        {
            try { return getter() ?? new OverrideGraphicSettings(); }
            catch { return new OverrideGraphicSettings(); }
        }

        private static bool SafeBool(Func<bool> getter, bool fallback)
        {
            try { return getter(); }
            catch { return fallback; }
        }

        private static (byte? r, byte? g, byte? b) TryGetOgsColor(Func<Color> getter)
        {
            try
            {
                var c = getter();
                if (c == null) return (null, null, null);
                return (c.Red, c.Green, c.Blue);
            }
            catch
            {
                // important: property getter can throw “invalid/uninitialized color”
                return (null, null, null);
            }
        }
    }
}
