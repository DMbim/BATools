using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Overhead
{
    public static class OverheadGlobalService
    {
        public sealed class SetEnabledResult
        {
            public bool Enabled { get; set; }
            public bool SettingsSaved { get; set; }

            // Disable path only
            public int ProxiesDeleted { get; set; }
            public int OverridesCleared { get; set; }
            public OverheadVgService.HideEverywhereResult? VgHideResult { get; set; }

            // Enable path only
            public OverheadVgService.ShowEverywhereResult? VgShowResult { get; set; }
            public int ViewsAnalyzed { get; set; }
            public int AnalyzerOverriddenTotal { get; set; }
            public List<string> AnalyzerFailures { get; } = new();

            public override string ToString()
            {
                if (Enabled)
                {
                    var vg = VgShowResult != null ? "\n\n" + VgShowResult : "";
                    var fails = AnalyzerFailures.Count > 0
                        ? "\n\nAnalyzer failures:\n- " + string.Join("\n- ", AnalyzerFailures)
                        : "";

                    return
                        "Overhead proxy auto updater enabled.\n" +
                        $"Views analyzed: {ViewsAnalyzed}\n" +
                        $"Elements overridden: {AnalyzerOverriddenTotal}" +
                        vg + fails;
                }
                else
                {
                    var vg = VgHideResult != null ? "\n\n" + VgHideResult : "";

                    return
                        "Overhead proxy auto updater disabled.\n" +
                        $"Proxies deleted: {ProxiesDeleted}\n" +
                        $"Overrides cleared: {OverridesCleared}" +
                        vg;
                }
            }
        }

        /// <summary>
        /// Enables or disables the Overhead proxy system for the given document. This is
        /// the single, canonical implementation of enable/disable in this add in.
        /// OverheadDisableService and OverheadEnableService have been removed, both
        /// duplicated this logic and had drifted out of sync with each other and with
        /// this method before consolidation.
        ///
        /// Disable path:
        ///   Suppresses the DMU updater, deletes all proxy DetailCurves via brute force
        ///   scan, turns off BA_Overhead VG everywhere (recording exactly what was changed
        ///   via OverheadVisibilitySnapshotStore for later restore), and resets the actual
        ///   OverrideGraphicSettings on every element OverheadStateStore tracked from the
        ///   last analyzer run, not just its bookkeeping. Persists Enabled=false.
        ///
        /// Enable path:
        ///   Persists Enabled=true, refreshes DMU triggers with current settings, restores
        ///   BA_Overhead VG visibility exactly where the prior disable hid it (via the same
        ///   snapshot store), then runs OverheadAnalyzer on every floor plan view so
        ///   proxies exist immediately without requiring each element to be modified first.
        ///   Suppresses the DMU updater during this initial population pass to prevent
        ///   double processing.
        ///
        /// Must NOT be called inside an existing open Transaction, opens its own.
        /// </summary>
        public static SetEnabledResult SetEnabled(Document doc, bool enabled)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var result = new SetEnabledResult { Enabled = enabled };

            // Set the in-process static flag immediately so the updater stops firing
            // even before the transaction commits.
            OverheadProxyUpdater.Enabled = enabled;

            using var t = new Transaction(
                doc,
                enabled ? "BA: Enable Overhead Auto Proxy" : "BA: Disable Overhead Auto Proxy");

            t.Start();

            var s = OverheadSettingsStore.Load(doc, out _) ?? OverheadSettings.Default();
            s.Normalize();
            s.Enabled = enabled;
            OverheadSettingsStore.Save(doc, s);
            result.SettingsSaved = true;

            if (!enabled)
            {
                OverheadProxyUpdater.IsSuppressed = true;
                try
                {
                    result.ProxiesDeleted = ProxyStateStore.RemoveAllOverheadProxiesAllPlansBrute(doc);

                    result.VgHideResult = OverheadVgService.TurnOffBAOverheadEverywhere(doc);

                    result.OverridesCleared = OverheadStateStore.ClearOverridesAndStateAllPlans(doc);
                }
                finally
                {
                    OverheadProxyUpdater.IsSuppressed = false;
                }
            }
            else
            {
                // Restore VG visibility first, exactly where the last disable hid it. This
                // must happen before proxies are recreated below, since proxies drawn
                // while the category is still hidden are equally invisible, restoring
                // afterward achieves the same visible result but restoring first matches
                // the conceptual order, visibility state then content.
                OverheadProxyUpdater.IsSuppressed = true;
                try
                {
                    result.VgShowResult = OverheadVgService.TurnOnBAOverheadEverywhere(doc);
                }
                finally
                {
                    OverheadProxyUpdater.IsSuppressed = false;
                }

                // Refresh DMU triggers so the updater watches the correct categories.
                // This call acquires _syncLock internally, must not be inside another lock.
                OverheadProxyUpdater.RefreshTriggers(s);

                // Populate proxies immediately for all existing elements. Without this,
                // proxies only appear when elements are next modified. Suppress the DMU
                // updater during the population pass to prevent it from firing for each
                // DetailCurve created by the analyzer, which would cause recursive proxy
                // creation attempts.
                OverheadProxyUpdater.IsSuppressed = true;
                try
                {
                    var plans = ProxyStateStore.GetFloorPlans(doc);
                    result.ViewsAnalyzed = plans.Count;

                    foreach (var vp in plans)
                    {
                        try
                        {
                            var analysis = new OverheadAnalyzer(doc, vp, s).Run();
                            result.AnalyzerOverriddenTotal += analysis.OverriddenCount;
                        }
                        catch (Exception ex)
                        {
                            result.AnalyzerFailures.Add($"{vp.Name}: {ex.GetType().Name}: {ex.Message}");
                            System.Diagnostics.Trace.WriteLine(
                                $"[OverheadGlobalService] Enable: analyzer failed for " +
                                $"view {vp.Id}: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    OverheadProxyUpdater.IsSuppressed = false;
                }
            }

            t.Commit();

            return result;
        }
    }
}