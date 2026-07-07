using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Overhead;
using BA.UI.Helpers;
using BA.UI.Overhead;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Commands.Anno
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_OverheadAutoDash : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uiapp = data.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;

            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            if (doc.ActiveView is not ViewPlan view || view.ViewType != ViewType.FloorPlan)
            {
                TaskDialog.Show("Overhead Auto Dash", "Active view must be a Floor Plan.");
                return Result.Cancelled;
            }

            bool migrate;
            var settings = OverheadSettingsStore.Load(doc, out migrate) ?? OverheadSettings.Default();
            settings.Normalize();

            if (migrate)
            {
                using (var t = new Transaction(doc, "OAD: Migrate Overhead Settings"))
                {
                    t.Start();
                    OverheadSettingsStore.Save(doc, settings);
                    t.Commit();
                }
            }

            var dlg = new OverheadConfigDialog(uiapp, settings, doc);
            RevitWindowHelper.SetOwnerToRevit(dlg, uiapp);

            if (dlg.ShowDialog() != true)
                return Result.Cancelled;

            // User clicked "Turn Off". Routed through OverheadGlobalService.SetEnabled,
            // which opens its own transaction, no transaction wrapping here.
            if (dlg.DisableRequested)
            {
                try
                {
                    var result = OverheadGlobalService.SetEnabled(doc, false);
                    TaskDialog.Show("BA", result.ToString());
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("OAD Disable", ex);
                    TaskDialog.Show("BA - Disable Overhead failed", ex.ToString());
                    message = ex.ToString();
                    return Result.Failed;
                }
            }

            // User clicked "Turn On". Same pattern, symmetric.
            if (dlg.EnableRequested)
            {
                try
                {
                    var result = OverheadGlobalService.SetEnabled(doc, true);
                    TaskDialog.Show("BA", result.ToString());
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("OAD Enable", ex);
                    TaskDialog.Show("BA - Enable Overhead failed", ex.ToString());
                    message = ex.ToString();
                    return Result.Failed;
                }
            }

            // User pressed OK, save settings
            if (dlg.SaveRequested && dlg.ResultSettings != null)
            {
                try
                {
                    settings = dlg.ResultSettings;
                    settings.Normalize();

                    using (var t = new Transaction(doc, "OAD: Save Settings"))
                    {
                        t.Start();
                        OverheadSettingsStore.Save(doc, settings);
                        t.Commit();
                    }

                    OverheadProxyUpdater.RefreshTriggers(settings);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("OAD Save Settings", ex);
                    TaskDialog.Show("BA - Save Settings failed", ex.ToString());
                    message = ex.ToString();
                    return Result.Failed;
                }
            }

            // If disabled, don't run analyzer
            if (!settings.Enabled)
            {
                TaskDialog.Show("Overhead Auto Dash", "Overhead is disabled.");
                return Result.Cancelled;
            }

            AnalysisResult result2;
            try
            {
                using (var t = new Transaction(doc, "OAD: Apply Overhead"))
                {
                    t.Start();
                    var analyzer = new OverheadAnalyzer(doc, view, settings);
                    result2 = analyzer.Run();
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OAD Apply Overhead", ex);
                TaskDialog.Show("BA - Apply Overhead failed", ex.ToString());
                message = ex.ToString();
                return Result.Failed;
            }

            TaskDialog.Show(
                "Overhead Auto Dash",
                $"Processed view: {view.Name}\n" +
                $"Overridden: {result2.OverriddenCount}\n" +
                $"CutZ: {result2.CutZmm:F0} mm\n" +
                $"TopZ: {result2.TopZmm:F0} mm");

            return Result.Succeeded;
        }
    }
}