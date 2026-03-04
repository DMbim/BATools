using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
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
                TaskDialog.Show("Overhead Auto-Dash", "Active view must be a Floor Plan.");
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

            bool openSettings = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift)
                                == System.Windows.Input.ModifierKeys.Shift;

            if (openSettings)
            {
                var dlg = new OverheadConfigDialog(uiapp, settings, doc);
                RevitWindowHelper.SetOwnerToRevit(dlg, uiapp);

                if (dlg.ShowDialog() != true)
                    return Result.Cancelled;

                // ✅ If user clicked "Turn Off" -> do it here, in a real Revit transaction context
                if (dlg.DisableRequested)
                {
                    try
                    {
                        using (var t = new Transaction(doc, "OAD: Disable Overhead"))
                        {
                            t.Start();

                            // your already-found good method:
                            // must be in-transaction
                            var disable = OverheadDisableService.Disable(
                                doc,
                                turnOffVgEverywhere: true,
                                clearAnalyzerOverrides: true);

                            t.Commit();

                            TaskDialog.Show("BA", disable.ToString());
                            return Result.Succeeded;
                        }
                    }
                    catch (Exception ex)
                    {
                        message = ex.ToString();
                        return Result.Failed;
                    }
                }

                // ✅ Otherwise user pressed OK -> save settings here
                if (dlg.SaveRequested && dlg.ResultSettings != null)
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
            }

            // ✅ If disabled, don't run analyzer
            if (!settings.Enabled)
            {
                TaskDialog.Show("Overhead Auto-Dash", "Overhead is disabled.");
                return Result.Cancelled;
            }

            AnalysisResult result;
            try
            {
                using (var t = new Transaction(doc, "OAD: Apply Overhead"))
                {
                    t.Start();
                    var analyzer = new OverheadAnalyzer(doc, view, settings);
                    result = analyzer.Run();
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }

            TaskDialog.Show(
                "Overhead Auto-Dash",
                $"Processed view: {view.Name}\n" +
                $"Overridden: {result.OverriddenCount}\n" +
                $"CutZ: {result.CutZmm:F0} mm\n" +
                $"TopZ: {result.TopZmm:F0} mm");

            return Result.Succeeded;
        }
    }
}