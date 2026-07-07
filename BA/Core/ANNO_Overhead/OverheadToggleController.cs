using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.Core.Overhead;
using System;

namespace BA.App.Overhead
{
    public static class OverheadToggleController
    {
        private enum PendingAction
        {
            None,
            Disable,
            Enable
        }

        // A single pending action slot instead of two independent booleans. If the
        // user flips the toggle twice before Idling fires (disable then enable, or
        // the reverse), this always reflects the last requested target state and
        // there is no scenario where both branches could run against a stale first
        // request in the same or a following tick.
        private static PendingAction _pending = PendingAction.None;

        public static void Initialize(UIControlledApplication app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            app.Idling += OnIdling;
        }

        public static void RequestDisableCleanup()
        {
            _pending = PendingAction.Disable;
        }

        public static void RequestEnableCleanup()
        {
            _pending = PendingAction.Enable;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            var action = _pending;
            if (action == PendingAction.None) return;

            var uiapp = sender as UIApplication;
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return;

            // Claim the pending action up front. If the transaction below throws
            // (document busy, no active view, etc.) it is restored in the catch
            // block so the next Idling tick retries the same target state instead
            // of silently dropping the request.
            _pending = PendingAction.None;

            try
            {
                switch (action)
                {
                    case PendingAction.Disable:
                        RunDisable(doc);
                        break;
                    case PendingAction.Enable:
                        RunEnable(doc);
                        break;
                }
            }
            catch
            {
                _pending = action;
            }
        }

        private static void RunDisable(Document doc)
        {
            using var t = new Transaction(doc, "BA: Disable Overhead (Cleanup)");
            t.Start();

            var s = OverheadSettingsStore.Load(doc, out bool migrate) ?? OverheadSettings.Default();
            s.Normalize();
            s.Enabled = false;
            OverheadSettingsStore.Save(doc, s);

            OverheadProxyUpdater.IsSuppressed = true;
            try
            {
                ProxyStateStore.RemoveAllOverheadProxiesAllPlansBrute(doc);
                OverheadVgService.TurnOffBAOverheadEverywhere(doc);
                doc.Regenerate();
            }
            finally
            {
                OverheadProxyUpdater.IsSuppressed = false;
            }

            t.Commit();
        }

        private static void RunEnable(Document doc)
        {
            using var t = new Transaction(doc, "BA: Enable Overhead (Restore)");
            t.Start();

            var s = OverheadSettingsStore.Load(doc, out bool migrate) ?? OverheadSettings.Default();
            s.Normalize();
            s.Enabled = true;
            OverheadSettingsStore.Save(doc, s);

            // Suppressed for symmetry with RunDisable. VG visibility changes do not
            // themselves feed OverheadProxyUpdater triggers today, but Regenerate()
            // can still process other queued document changes, and this keeps the
            // enable path safe if the updater's trigger set is ever broadened later.
            OverheadProxyUpdater.IsSuppressed = true;
            try
            {
                OverheadVgService.TurnOnBAOverheadEverywhere(doc);
                doc.Regenerate();
            }
            finally
            {
                OverheadProxyUpdater.IsSuppressed = false;
            }

            t.Commit();
        }
    }
}