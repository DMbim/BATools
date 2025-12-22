using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using BA.Core.Overhead;
using BA.UI.Overhead;

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

            var settings = OverheadSettingsStore.Load(doc) ?? OverheadSettings.Default();

            bool openSettings = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift)
                                == System.Windows.Input.ModifierKeys.Shift;

            if (openSettings)
            {
                var dlg = new OverheadConfigDialog(settings, doc) { Owner = System.Windows.Application.Current?.MainWindow };
                if (dlg.ShowDialog() != true)
                    return Result.Cancelled;

                settings = dlg.ResultSettings ?? settings;
                settings.Normalize();

                using (var t = new Transaction(doc, "OAD: Save Settings"))
                {
                    t.Start();
                    OverheadSettingsStore.Save(doc, settings);
                    t.Commit();
                }

                OverheadProxyUpdater.RefreshTriggers(settings);
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
                message = ex.Message;
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
