using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BA.Core.Overhead
{
    public static class OverheadDisableService
    {
        public sealed class DisableResult
        {
            public bool SettingsSaved { get; set; }
            public int ProxiesDeleted { get; set; }
            public int OverridesCleared { get; set; }
            public OverheadVgService.HideEverywhereResult? VgResult { get; set; }

            public override string ToString()
            {
                var vg = VgResult != null ? "\n\n" + VgResult : "";
                return
                    $"Overhead disabled.\n" +
                    $"Proxies deleted: {ProxiesDeleted}\n" +
                    $"Overrides cleared: {OverridesCleared}" +
                    vg;
            }
        }

        /// <summary>
        /// Disable overhead system-wide:
        /// - Persist Enabled=false
        /// - Delete proxies (all non-template floor plans)
        /// - Turn off VG (templates + non-template views)
        /// - Clear analyzer overrides (best-effort, based on OverheadStateStore)
        ///
        /// MUST be called inside an open Transaction.
        /// </summary>
        public static DisableResult Disable(Document doc, bool turnOffVgEverywhere = true, bool clearAnalyzerOverrides = true)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var result = new DisableResult();

            // 1) Persist disabled state
            var s = OverheadSettingsStore.Load(doc, out bool migrate) ?? OverheadSettings.Default();
            s.Normalize();
            s.Enabled = false;
            OverheadSettingsStore.Save(doc, s);
            result.SettingsSaved = true;

            // 2) Suppress updater during cleanup
            OverheadProxyUpdater.IsSuppressed = true; try
            {
                // 3) Delete proxies
                result.ProxiesDeleted = ProxyStateStore.RemoveAllOverheadProxiesAllPlansBrute(doc);

                // 4) Clear analyzer overrides (if you used OverheadAnalyzer)
                if (clearAnalyzerOverrides)
                    result.OverridesCleared = ClearAnalyzerOverridesAllPlans(doc);

                // 5) Turn off VG checkbox everywhere (optional)
                if (turnOffVgEverywhere)
                    result.VgResult = OverheadVgService.TurnOffBAOverheadEverywhere(doc);

                // Ensure model is consistent before leaving TX
                doc.Regenerate();
            }
            finally
            {
                OverheadProxyUpdater.IsSuppressed = false;
            }

            return result;
        }

        /// <summary>
        /// Clears overrides set by OverheadAnalyzer.
        /// Uses OverheadStateStore (last-run ids per view) to avoid scanning everything.
        /// MUST be called inside an open Transaction.
        /// </summary>
        private static int ClearAnalyzerOverridesAllPlans(Document doc)
        {
            int cleared = 0;

            var plans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(vp => vp.ViewType == ViewType.FloorPlan && !vp.IsTemplate)
                .ToList();

            var empty = new OverrideGraphicSettings();

            foreach (var vp in plans)
            {
                IList<ElementId> last;
                try
                {
                    last = OverheadStateStore.GetLastRunIds(doc, vp.Id);
                }
                catch
                {
                    continue;
                }

                if (last == null || last.Count == 0)
                    continue;

                foreach (var id in last)
                {
                    try
                    {
                        if (doc.GetElement(id) == null) continue;
                        vp.SetElementOverrides(id, empty);
                        cleared++;
                    }
                    catch
                    {
                        // best-effort
                    }
                }

                try { OverheadStateStore.ClearForView(doc, vp.Id); } catch { }
            }

            return cleared;
        }
    }
}