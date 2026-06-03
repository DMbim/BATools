using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.Core.Overhead;
using System;

namespace BA.App.Overhead
{
    public static class OverheadToggleController
    {
        private static bool _pendingDisableCleanup;

        public static void Initialize(UIControlledApplication app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Idling += OnIdling;
        }

        public static void RequestDisableCleanup()
        {
            _pendingDisableCleanup = true;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (!_pendingDisableCleanup) return;

            var uiapp = sender as UIApplication;
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;

            if (doc == null) return;

            try
            {
                _pendingDisableCleanup = false;

                using (var t = new Transaction(doc, "BA: Disable Overhead (Cleanup)"))
                {
                    t.Start();

                    // Persist per-doc disabled
                    var s = OverheadSettingsStore.Load(doc, out bool migrate) ?? OverheadSettings.Default();
                    s.Normalize();
                    s.Enabled = false;
                    OverheadSettingsStore.Save(doc, s);

                    // Suppress updater during deletes
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
            }
            catch
            {
                // If anything failed (doc busy), try again on next Idling
                _pendingDisableCleanup = true;
            }
        }
    }
}