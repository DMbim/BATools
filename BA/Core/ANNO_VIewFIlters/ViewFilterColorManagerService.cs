// File: BA.Core/ViewFilters/ViewFilterColorManagerService.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB.Visual;
using Color = Autodesk.Revit.DB.Color;
using View = Autodesk.Revit.DB.View;

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

        public static void ApplyFilterOverrides(Document doc, ElementId templateId, IList<FilterColorAssignment> assigns)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (assigns == null || assigns.Count == 0) return;

            var template = doc.GetElement(templateId) as View;
            if (template == null || !template.IsTemplate) return;

            var currentFilters = template.GetFilters() ?? new List<ElementId>();

            var solidFillPatternId = GetSolidFillPatternId(doc);

            foreach (var a in assigns)
            {
                if (a.FilterId == null || a.FilterId == ElementId.InvalidElementId)
                    continue;

                bool wasAlreadyOnTemplate = currentFilters.Contains(a.FilterId);

                if (!wasAlreadyOnTemplate)
                {
                    template.AddFilter(a.FilterId);
                    currentFilters = template.GetFilters();
                }

                var ogs = SafeOgs(() => template.GetFilterOverrides(a.FilterId));

                var patternId = ResolvePatternId(a.PatternId, solidFillPatternId);

                if (a.ProjR.HasValue && a.ProjG.HasValue && a.ProjB.HasValue)
                {
                    var projColor = new Color(a.ProjR.Value, a.ProjG.Value, a.ProjB.Value);
                    ogs.SetProjectionLineColor(projColor);
                    ogs.SetSurfaceForegroundPatternColor(projColor);
                    if (patternId != null)
                        ogs.SetSurfaceForegroundPatternId(patternId);
                }

                if (a.CutR.HasValue && a.CutG.HasValue && a.CutB.HasValue)
                {
                    var cutColor = new Color(a.CutR.Value, a.CutG.Value, a.CutB.Value);
                    ogs.SetCutLineColor(cutColor);
                    ogs.SetCutForegroundPatternColor(cutColor);
                    if (patternId != null)
                        ogs.SetCutForegroundPatternId(patternId);
                }

                template.SetFilterOverrides(a.FilterId, ogs);

                if (!wasAlreadyOnTemplate)
                {
                    template.SetFilterVisibility(a.FilterId, true);
                }
            }
        }

        public static (int Applied, int SkippedCategory, int SkippedNoMatch) ApplySelectionOverrides(
            Document doc, View view, ICollection<ElementId> selectedIds, ParameterColorRule rule)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            if (selectedIds == null || selectedIds.Count == 0)
                return (0, 0, 0);

            var solidFillPatternId = GetSolidFillPatternId(doc);

            int applied = 0;
            int skippedCategory = 0;
            int skippedNoMatch = 0;

            foreach (var id in selectedIds)
            {
                var el = doc.GetElement(id);
                if (el == null)
                {
                    skippedNoMatch++;
                    continue;
                }

                if (el.Category == null || el.Category.Id != rule.CategoryId)
                {
                    skippedCategory++;
                    continue;
                }

                if (!ParameterEnumerationService.TryMatchBucket(doc, el, rule, out var bucket))
                {
                    skippedNoMatch++;
                    continue;
                }

                var ogs = new OverrideGraphicSettings();
                var color = new Color(bucket.R, bucket.G, bucket.B);

                ogs.SetProjectionLineColor(color);
                ogs.SetCutLineColor(color);
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetCutForegroundPatternColor(color);

                var patternId = ResolvePatternId(bucket.FillPatternId, solidFillPatternId);
                if (patternId != null)
                {
                    ogs.SetSurfaceForegroundPatternId(patternId);
                    ogs.SetCutForegroundPatternId(patternId);
                }

                view.SetElementOverrides(id, ogs);
                applied++;
            }

            return (applied, skippedCategory, skippedNoMatch);
        }

        // New. Builds one legend row entry directly from a filter's live overrides
        // on a template, rather than from a stored ParameterColorRule. This is what
        // lets the View Template tab build a legend from an arbitrary mix of native
        // Revit filters and BA managed filters, since neither necessarily has a
        // ParameterColorRule behind it anymore by the time it's sitting on the
        // template. Color preference order: surface fill color, then projection
        // line color, then cut line color, first one found wins. Returns a fully
        // formed LegendEntry with black/no-pattern defaults if the filter carries
        // no color override at all, rather than throwing, so one badly configured
        // filter doesn't abort legend generation for the rest of the selection. // <- NEW
        public static LegendEntry BuildLegendEntryFromFilter(Document doc, View template, ElementId filterId)
        {
            if (doc == null || template == null || filterId == null || filterId == ElementId.InvalidElementId)
                return null;

            var pfe = doc.GetElement(filterId) as ParameterFilterElement;
            string label = pfe?.Name ?? "<filter>";

            var ogs = SafeOgs(() => template.GetFilterOverrides(filterId));

            Color color = null;

            try
            {
                if (ogs.SurfaceForegroundPatternColor != null && ogs.SurfaceForegroundPatternColor.IsValid)
                    color = ogs.SurfaceForegroundPatternColor;
            }
            catch { /* leave color null, try next source */ }

            if (color == null)
            {
                try
                {
                    if (ogs.ProjectionLineColor != null && ogs.ProjectionLineColor.IsValid)
                        color = ogs.ProjectionLineColor;
                }
                catch { /* leave color null, try next source */ }
            }

            if (color == null)
            {
                try
                {
                    if (ogs.CutLineColor != null && ogs.CutLineColor.IsValid)
                        color = ogs.CutLineColor;
                }
                catch { /* fall through to black default below */ }
            }

            byte r = color?.Red ?? 0;
            byte g = color?.Green ?? 0;
            byte b = color?.Blue ?? 0;

            ElementId patternId = ElementId.InvalidElementId;
            try { patternId = ogs.SurfaceForegroundPatternId ?? ElementId.InvalidElementId; }
            catch { /* leave InvalidElementId, LegendGenerationService falls back to solid */ }

            return new LegendEntry(label, r, g, b, patternId);
        }

        private static ElementId ResolvePatternId(ElementId requested, ElementId solidFallback)
        {
            if (requested != null && requested != ElementId.InvalidElementId)
                return requested;
            return solidFallback;
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id;
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
                return (null, null, null);
            }
        }
    }

    public sealed record FilterColorAssignment(
        ElementId FilterId,
        byte? CutR, byte? CutG, byte? CutB,
        byte? ProjR, byte? ProjG, byte? ProjB,
        ElementId PatternId = null
    );
}