using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Overhead
{
    public static class OverheadVgService
    {
        public sealed class HideEverywhereResult
        {
            public long CategoryId { get; set; }

            public int TemplatesTargeted { get; set; }
            public int TemplatesChanged { get; set; }
            public int TemplatesAlreadyInState { get; set; }
            public int TemplatesFailed { get; set; }

            public int ViewsWithoutTemplateTargeted { get; set; }
            public int ViewsWithoutTemplateChanged { get; set; }
            public int ViewsWithoutTemplateAlreadyInState { get; set; }
            public int ViewsWithoutTemplateFailed { get; set; }

            public int NotApplicableSkipped { get; set; }

            // Ids that were actually transitioned from visible to hidden by this run.
            // Only these get persisted for restore. Views/templates already hidden are
            // deliberately excluded, see TryHideCategory.
            public List<ElementId> TemplatesChangedIds { get; } = new();
            public List<ElementId> ViewsWithoutTemplateChangedIds { get; } = new();

            public List<string> FailSamples { get; } = new();

            public override string ToString()
            {
                return
                    $"BA_Overhead CategoryId: {CategoryId}\n\n" +
                    $"Templates targeted: {TemplatesTargeted}\n" +
                    $"Templates changed: {TemplatesChanged}\n" +
                    $"Templates already hidden: {TemplatesAlreadyInState}\n" +
                    $"Templates failed: {TemplatesFailed}\n\n" +
                    $"Views (direct) targeted: {ViewsWithoutTemplateTargeted}\n" +
                    $"Views (direct) changed: {ViewsWithoutTemplateChanged}\n" +
                    $"Views (direct) already hidden: {ViewsWithoutTemplateAlreadyInState}\n" +
                    $"Views (direct) failed: {ViewsWithoutTemplateFailed}\n\n" +
                    $"Not applicable skipped: {NotApplicableSkipped}\n" +
                    (FailSamples.Count > 0 ? "\nSamples:\n- " + string.Join("\n- ", FailSamples) : "");
            }
        }

        public sealed class ShowEverywhereResult
        {
            public long CategoryId { get; set; }

            public int TemplatesTargeted { get; set; }
            public int TemplatesChanged { get; set; }
            public int TemplatesAlreadyInState { get; set; }
            public int TemplatesFailed { get; set; }

            public int ViewsWithoutTemplateTargeted { get; set; }
            public int ViewsWithoutTemplateChanged { get; set; }
            public int ViewsWithoutTemplateAlreadyInState { get; set; }
            public int ViewsWithoutTemplateFailed { get; set; }

            public int NotApplicableSkipped { get; set; }
            public int MissingOrInvalidSkipped { get; set; }

            public List<string> FailSamples { get; } = new();

            public override string ToString()
            {
                return
                    $"BA_Overhead CategoryId: {CategoryId}\n\n" +
                    $"Templates targeted: {TemplatesTargeted}\n" +
                    $"Templates restored: {TemplatesChanged}\n" +
                    $"Templates already visible: {TemplatesAlreadyInState}\n" +
                    $"Templates failed: {TemplatesFailed}\n\n" +
                    $"Views (direct) targeted: {ViewsWithoutTemplateTargeted}\n" +
                    $"Views (direct) restored: {ViewsWithoutTemplateChanged}\n" +
                    $"Views (direct) already visible: {ViewsWithoutTemplateAlreadyInState}\n" +
                    $"Views (direct) failed: {ViewsWithoutTemplateFailed}\n\n" +
                    $"Not applicable skipped: {NotApplicableSkipped}\n" +
                    $"Missing or invalid since disable, skipped: {MissingOrInvalidSkipped}\n" +
                    (FailSamples.Count > 0 ? "\nSamples:\n- " + string.Join("\n- ", FailSamples) : "");
            }
        }

        /// <summary>
        /// Turns OFF Model Categories -> Lines -> BA_Overhead everywhere.
        ///
        /// A view having ViewTemplateId set does NOT mean the template governs model
        /// category visibility for that view. Each view template independently decides,
        /// per setting, which parameters it "includes" as controlled. Whether model
        /// category overrides specifically are included is exposed via
        /// View.GetNonControlledTemplateParameterIds() on the template element,
        /// containing BuiltInParameter.VIS_GRAPHICS_MODEL when NOT included. If a view's
        /// template does not include that setting, the view's own override is what is
        /// actually in effect, and it must be mutated directly, exactly like a view with
        /// no template at all. Treating every templated view as "mutate the template" was
        /// the root cause of BA_Overhead staying hidden in views whose template does not
        /// control model categories, since neither disabling nor re enabling ever touched
        /// that view's own setting.
        ///
        /// Records exactly which templates/views were transitioned from visible to hidden
        /// by this call and persists that set via OverheadVisibilitySnapshotStore, so
        /// TurnOnBAOverheadEverywhere can restore precisely those later.
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

            var templateIds = new HashSet<ElementId>();
            var directTargetViews = new List<View>();

            // Cache per template id, several views commonly share one template, no need
            // to call GetNonControlledTemplateParameterIds() more than once per template.
            var controlsModelCategoriesCache = new Dictionary<long, bool>();

            foreach (var v in allViews)
            {
                if (v.IsTemplate) continue;

                var tid = v.ViewTemplateId;

                if (tid == ElementId.InvalidElementId || tid.Value == 0)
                {
                    directTargetViews.Add(v);
                    continue;
                }

                var template = doc.GetElement(tid) as View;
                if (template == null || !template.IsTemplate)
                {
                    // Stale/invalid template reference, fall back to direct.
                    directTargetViews.Add(v);
                    continue;
                }

                if (!controlsModelCategoriesCache.TryGetValue(tid.Value, out bool controls))
                {
                    controls = TemplateControlsModelCategories(template);
                    controlsModelCategoriesCache[tid.Value] = controls;
                }

                if (controls)
                    templateIds.Add(tid);
                else
                    directTargetViews.Add(v);
            }

            var templates = templateIds
                .Select(id => doc.GetElement(id) as View)
                .Where(t => t != null && t.IsTemplate)
                .Cast<View>()
                .ToList();

            res.TemplatesTargeted = templates.Count;

            foreach (var t in templates)
                TryHideCategory(t, catId, res, label: $"{t.Name} [TEMPLATE]");

            res.ViewsWithoutTemplateTargeted = directTargetViews.Count;

            foreach (var v in directTargetViews)
                TryHideCategory(v, catId, res, label: $"{v.Name} [{v.ViewType}]");

            OverheadVisibilitySnapshotStore.Save(doc, res.TemplatesChangedIds, res.ViewsWithoutTemplateChangedIds);

            return res;
        }

        /// <summary>
        /// Turns ON Model Categories -> Lines -> BA_Overhead in exactly the templates and
        /// views that TurnOffBAOverheadEverywhere previously hid, as recorded in
        /// OverheadVisibilitySnapshotStore. Anything hidden independently of the disable
        /// operation is left untouched. Clears the snapshot afterward regardless of
        /// partial failures, since it is single use per disable/enable cycle.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static ShowEverywhereResult TurnOnBAOverheadEverywhere(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var gs = LineStyleLookup.FindOverhead(doc);
            if (gs == null)
                throw new InvalidOperationException("BA_Overhead line style not found (FindOverhead returned null).");

            var catId = gs.GraphicsStyleCategory.Id;

            var res = new ShowEverywhereResult { CategoryId = catId.Value };

            var (templateIds, viewIds) = OverheadVisibilitySnapshotStore.Load(doc);

            res.TemplatesTargeted = templateIds.Count;
            res.ViewsWithoutTemplateTargeted = viewIds.Count;

            foreach (var tid in templateIds)
            {
                var t = doc.GetElement(tid) as View;
                if (t == null || !t.IsTemplate)
                {
                    res.MissingOrInvalidSkipped++;
                    continue;
                }

                TryShowCategory(t, catId, res, label: $"{t.Name} [TEMPLATE]");
            }

            foreach (var vid in viewIds)
            {
                var v = doc.GetElement(vid) as View;
                if (v == null || v.IsTemplate)
                {
                    res.MissingOrInvalidSkipped++;
                    continue;
                }

                TryShowCategory(v, catId, res, label: $"{v.Name} [{v.ViewType}]");
            }

            OverheadVisibilitySnapshotStore.Clear(doc);

            return res;
        }

        /// <summary>
        /// True if the given template (an Element for which View.IsTemplate is true)
        /// includes BuiltInParameter.VIS_GRAPHICS_MODEL, meaning V/G Overrides Model
        /// Categories, among its controlled parameters. If the check itself fails for any
        /// reason, defaults to true (controlled), which reproduces the previous, safer
        /// behavior of mutating the template rather than silently doing nothing.
        /// </summary>
        private static bool TemplateControlsModelCategories(View template)
        {
            try
            {
                var nonControlled = template.GetNonControlledTemplateParameterIds();
                long vgModelId = (long)BuiltInParameter.VIS_GRAPHICS_MODEL;
                return !nonControlled.Any(id => id.Value == vgModelId);
            }
            catch
            {
                return true;
            }
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
                    // Already hidden before this run touched it. Not our transition,
                    // must not be recorded for restore, or re enabling would force this
                    // category visible in a view/template that had it off for reasons
                    // unrelated to this add in.
                    if (v.IsTemplate) res.TemplatesAlreadyInState++;
                    else res.ViewsWithoutTemplateAlreadyInState++;
                    return;
                }

                v.SetCategoryHidden(catId, true);

                if (v.IsTemplate)
                {
                    res.TemplatesChanged++;
                    res.TemplatesChangedIds.Add(v.Id);
                }
                else
                {
                    res.ViewsWithoutTemplateChanged++;
                    res.ViewsWithoutTemplateChangedIds.Add(v.Id);
                }
            }
            catch (Exception ex)
            {
                if (v.IsTemplate) res.TemplatesFailed++;
                else res.ViewsWithoutTemplateFailed++;

                if (res.FailSamples.Count < 12)
                    res.FailSamples.Add($"{label}: {ex.GetType().Name}");
            }
        }

        private static void TryShowCategory(View v, ElementId catId, ShowEverywhereResult res, string label)
        {
            try
            {
                if (!v.CanCategoryBeHidden(catId))
                {
                    res.NotApplicableSkipped++;
                    return;
                }

                if (!v.GetCategoryHidden(catId))
                {
                    if (v.IsTemplate) res.TemplatesAlreadyInState++;
                    else res.ViewsWithoutTemplateAlreadyInState++;
                    return;
                }

                v.SetCategoryHidden(catId, false);

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