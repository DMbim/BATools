// ============================================================
// FILE: BA/Core/ANNO_Overhead/OverheadGlobalService.cs
// ============================================================
using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace BA.Core.Overhead
{
    public static class OverheadGlobalService
    {
        /// <summary>
        /// Enables or disables the Overhead proxy system for the given document.
        ///
        /// Disable path:
        ///   Suppresses the DMU updater, deletes all proxy DetailCurves via brute-force
        ///   scan, clears VG overrides everywhere, and clears OverheadStateStore for
        ///   all floor plan views. Persists Enabled=false to ES.
        ///
        /// Enable path:
        ///   Persists Enabled=true, refreshes DMU triggers with current settings, then
        ///   runs OverheadAnalyzer on every floor plan view so proxies exist immediately
        ///   without requiring each element to be modified first. Suppresses the DMU
        ///   updater during this initial population pass to prevent double-processing.
        ///
        /// Must NOT be called inside an existing open Transaction — opens its own.
        /// </summary>
        public static void SetEnabled(Document doc, bool enabled)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

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

            if (!enabled)
            {
                OverheadProxyUpdater.IsSuppressed = true;
                try
                {
                    // Delete all proxy DetailCurves across every floor plan view.
                    ProxyStateStore.RemoveAllOverheadProxiesAllPlansBrute(doc);

                    // Turn off the BA_Overhead line style category visibility everywhere.
                    OverheadVgService.TurnOffBAOverheadEverywhere(doc);

                    // Clear OverheadStateStore for ALL floor plans.
                    // Bug in previous version: only cleared doc.ActiveView.Id.
                    foreach (var vp in ProxyStateStore.GetFloorPlans(doc))
                    {
                        try { OverheadStateStore.ClearForView(doc, vp.Id); }
                        catch { /* best-effort; view may not have state */ }
                    }
                }
                finally
                {
                    OverheadProxyUpdater.IsSuppressed = false;
                }
            }
            else
            {
                // Refresh DMU triggers so the updater watches the correct categories.
                // This call acquires _syncLock internally — must not be inside another lock.
                OverheadProxyUpdater.RefreshTriggers(s);

                // Populate proxies immediately for all existing elements.
                // Without this, proxies only appear when elements are next modified.
                // Suppress the DMU updater during the population pass to prevent it from
                // firing for each DetailCurve created by the analyzer (which would cause
                // recursive proxy creation attempts).
                OverheadProxyUpdater.IsSuppressed = true;
                try
                {
                    foreach (var vp in ProxyStateStore.GetFloorPlans(doc))
                    {
                        try
                        {
                            new OverheadAnalyzer(doc, vp, s).Run();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine(
                                $"[OverheadGlobalService] Enable: analyzer failed for " +
                                $"view {vp.Id}: {ex.Message}");
                            // Continue to remaining views.
                        }
                    }
                }
                finally
                {
                    OverheadProxyUpdater.IsSuppressed = false;
                }
            }

            t.Commit();
        }
    }
}