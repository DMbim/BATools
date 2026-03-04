using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Overhead
{
    public static class OverheadVgService
    {
        public sealed class HideEverywhereResult
        {
            public long CategoryId { get; set; }

            public int TemplatesTargeted { get; set; }
            public int TemplatesChanged { get; set; }
            public int TemplatesFailed { get; set; }

            public int ViewsWithoutTemplateTargeted { get; set; }
            public int ViewsWithoutTemplateChanged { get; set; }
            public int ViewsWithoutTemplateFailed { get; set; }

            public int NotApplicableSkipped { get; set; }

            public List<string> FailSamples { get; } = new();

            public override string ToString()
            {
                return
                    $"BA_Overhead CategoryId: {CategoryId}\n\n" +
                    $"Templates targeted: {TemplatesTargeted}\n" +
                    $"Templates changed: {TemplatesChanged}\n" +
                    $"Templates failed: {TemplatesFailed}\n\n" +
                    $"Views (no template) targeted: {ViewsWithoutTemplateTargeted}\n" +
                    $"Views (no template) changed: {ViewsWithoutTemplateChanged}\n" +
                    $"Views (no template) failed: {ViewsWithoutTemplateFailed}\n\n" +
                    $"Not applicable skipped: {NotApplicableSkipped}\n" +
                    (FailSamples.Count > 0 ? "\nSamples:\n- " + string.Join("\n- ", FailSamples) : "");
            }
        }

        /// <summary>
        /// Turns OFF Model Categories -> Lines -> BA_Overhead everywhere.
        /// - If view has template: change template (so it doesn't revert).
        /// - If view has no template: change view.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static HideEverywhereResult TurnOffBAOverheadEverywhere(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var gs = LineStyleLookup.FindOverhead(doc);
            if (gs == null)
                throw new InvalidOperationException("BA_Overhead line style not found (FindOverhead returned null).");

            var catId = gs.GraphicsStyleCategory.Id;

            var res = new HideEverywhereResult { CategoryId = catId.Value };

            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v != null && !IsInternalOrBrowser(v))
                .ToList();

            // Collect template ids used by views
            var templateIds = new HashSet<ElementId>();
            var viewsWithoutTemplate = new List<View>();

            foreach (var v in allViews)
            {
                if (v.IsTemplate) continue;

                var tid = v.ViewTemplateId;
                if (tid != ElementId.InvalidElementId && tid.Value != 0)
                    templateIds.Add(tid);
                else
                    viewsWithoutTemplate.Add(v);
            }

            var templates = templateIds
                .Select(id => doc.GetElement(id) as View)
                .Where(t => t != null && t.IsTemplate)
                .Cast<View>()
                .ToList();

            res.TemplatesTargeted = templates.Count;

            foreach (var t in templates)
            {
                TryHideCategory(t, catId, res, label: $"{t.Name} [TEMPLATE]");
            }

            res.ViewsWithoutTemplateTargeted = viewsWithoutTemplate.Count;

            foreach (var v in viewsWithoutTemplate)
            {
                TryHideCategory(v, catId, res, label: $"{v.Name} [{v.ViewType}]");
            }

            return res;
        }

        private static void TryHideCategory(View v, ElementId catId, HideEverywhereResult res, string label)
        {
            try
            {
                if (!v.CanCategoryBeHidden(catId))
                {
                    res.NotApplicableSkipped++;
                    return;
                }

                if (v.GetCategoryHidden(catId))
                {
                    // already hidden; treat as success (optional)
                    if (v.IsTemplate) res.TemplatesChanged++;
                    else res.ViewsWithoutTemplateChanged++;
                    return;
                }

                v.SetCategoryHidden(catId, true);

                if (v.IsTemplate) res.TemplatesChanged++;
                else res.ViewsWithoutTemplateChanged++;
            }
            catch (Exception ex)
            {
                if (v.IsTemplate) res.TemplatesFailed++;
                else res.ViewsWithoutTemplateFailed++;

                if (res.FailSamples.Count < 12)
                    res.FailSamples.Add($"{label}: {ex.GetType().Name}");
            }
        }

        private static bool IsInternalOrBrowser(View v)
        {
            try { if (v.ViewType == ViewType.Internal) return true; } catch { }
            try { if (v.ViewType == ViewType.ProjectBrowser) return true; } catch { }
            return false;
        }
    }
}