// File: BA_Tools/UI/BimHub/Services/BimHubHealthService.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI.BimHub.Models;
using System;

namespace BA.UI.BimHub.Services
{
    /// <summary>
    /// Collects live project health data for the hub header card.
    /// Called inside RevitExternalInvoker.Run() — Revit API access is valid here.
    ///
    /// STUB: Replace each section comment with real data source when available.
    /// The method must never throw — all sections are individually try/catched so
    /// a failure in one metric does not blank the entire card.
    /// </summary>
    public static class BimHubHealthService
    {
        public static BimHubHealthSnapshot Collect(UIApplication uiApp)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));

            var doc = uiApp.ActiveUIDocument?.Document;

            int paramsLoaded = 0;
            int qaWarnings = 0;
            int qaErrors = 0;
            string templateVersion = "—";

            // ── Params loaded ────────────────────────────────────────────────
            // STUB: Replace with real shared parameter count from your
            // parameter manager / BA_SharedParameters source.
            // Example real implementation:
            //   paramsLoaded = new FilteredElementCollector(doc)
            //       .OfClass(typeof(SharedParameterElement))
            //       .GetElementCount();
            try
            {
                if (doc != null)
                {
                    paramsLoaded = new FilteredElementCollector(doc)
                        .OfClass(typeof(SharedParameterElement))
                        .GetElementCount();
                }
            }
            catch { /* non-critical — leave at 0 */ }

            // ── QA warnings / errors ─────────────────────────────────────────
            // STUB: Replace with real QA Center scan results when QA Center
            // exposes a static LastResult or IQaScanResult interface.
            // For now: hardcoded zeros so card renders cleanly.
            try
            {
                // TODO: qaWarnings = QaCenterService.LastResult?.WarningCount ?? 0;
                // TODO: qaErrors   = QaCenterService.LastResult?.ErrorCount   ?? 0;
                qaWarnings = 0;
                qaErrors = 0;
            }
            catch { }

            // ── Template version ─────────────────────────────────────────────
            // STUB: Replace with real template version lookup.
            // Example: read a project parameter BA_TemplateVersion from ProjectInfo.
            try
            {
                if (doc != null)
                {
                    var projInfo = doc.ProjectInformation;
                    var param = projInfo?.LookupParameter("BA_TemplateVersion");
                    if (param != null && param.HasValue)
                        templateVersion = param.AsString() ?? "—";
                }
            }
            catch { }

            return new BimHubHealthSnapshot
            {
                ParamsLoaded = paramsLoaded,
                QaWarnings = qaWarnings,
                QaErrors = qaErrors,
                TemplateVersion = templateVersion,
                CheckedAt = DateTime.Now,
            };
        }
    }
}