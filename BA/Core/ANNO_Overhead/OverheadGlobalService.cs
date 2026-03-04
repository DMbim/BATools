using Autodesk.Revit.DB;
using System;

namespace BA.Core.Overhead
{
    public static class OverheadGlobalService
    {
        public static void SetEnabled(Document doc, bool enabled)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            OverheadProxyUpdater.Enabled = enabled;

            using (var t = new Transaction(doc, enabled ?
                "BA: Enable Overhead Auto Proxy" :
                "BA: Disable Overhead Auto Proxy"))
            {
                t.Start();

                var s = OverheadSettingsStore.Load(doc, out bool migrate) ?? OverheadSettings.Default();
                s.Enabled = enabled;
                OverheadSettingsStore.Save(doc, s);

                if (!enabled)
                {
                    OverheadProxyUpdater.Suppress = true;
                    try
                    {
                        ProxyStateStore.RemoveAllOverheadProxiesAllPlansBrute(doc);
                        OverheadVgService.TurnOffBAOverheadEverywhere(doc);
                        OverheadStateStore.ClearForView(doc, doc.ActiveView.Id);
                    }
                    finally
                    {
                        OverheadProxyUpdater.Suppress = false;
                    }
                }

                t.Commit();
            }
        }
    }
}