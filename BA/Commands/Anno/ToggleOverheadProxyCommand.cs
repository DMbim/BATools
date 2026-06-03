// ============================================================
// FILE: BA/Commands/ToggleOverheadProxyCommand.cs
// ============================================================
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Overhead;
using BA.IssueReporter.Services;
using BA.UI.Overhead;

namespace BA.Commands
{
    /// <summary>
    /// Toggles the Overhead proxy auto-updater on or off for the active document.
    /// Reads the persisted state from OverheadSettingsStore to determine the current
    /// toggle direction. Falls back to the in-process static f lag if settings are absent.
    ///
    /// Ribbon wiring in BaApplication:
    ///   pushButton.SetAvailabilityClassName(typeof(OverheadProxyCommandAvailability).FullName);
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleOverheadProxyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uidoc.Document;
            var uiapp = commandData.Application;
            var view = doc.ActiveView as ViewPlan;
            if (view == null || view.ViewType != ViewType.FloorPlan)
            {
                TaskDialog.Show("Overhead Auto-Dash", "Active view must be a Floor Plan.");
                return Result.Cancelled;
            }
            try
            {
                var settings = OverheadSettingsStore.Load(doc, out _);

                // Hold SHIFT to open the settings UI
                bool openSettings = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift)
                                    == System.Windows.Input.ModifierKeys.Shift;

                if (openSettings)
                {
                    var dlg = new OverheadConfigDialog(uiapp,settings, doc);
                    if (dlg.ShowDialog() == true && dlg.ResultSettings != null)
                    {
                        settings = dlg.ResultSettings;

                        // Save project settings INSIDE a transaction
                        using (var t = new Transaction(doc, "OAD: Save Settings"))
                        {
                            t.Start();
                            OverheadSettingsStore.Save(doc, settings);
                            t.Commit();
                        }

                        // Refresh updater triggers to watch the selected categories
                        OverheadProxyUpdater.RefreshTriggers(settings);
                    }
                    else
                    {
                        return Result.Cancelled;
                    }
                }
                bool currentlyEnabled = settings?.Enabled ?? false;
                bool targetEnabled = !currentlyEnabled;

                OverheadGlobalService.SetEnabled(doc, targetEnabled);

                TaskDialog.Show(
                    "BA Overhead Proxies",
                    targetEnabled
                        ? "Overhead proxy auto-updater enabled.\nProxies have been generated for all floor plan views."
                        : "Overhead proxy auto-updater disabled.\nAll proxies have been removed.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // Surfaces the real exception instead of Revit's generic wrapper.
                // Remove this catch once the root cause is fixed.
                var inner = ex.InnerException;
                TaskDialog.Show("BA Overhead — Exception",
                    $"Type:    {ex.GetType().FullName}\n\n" +
                    $"Message: {ex.Message}\n\n" +
                    (inner != null ? $"Inner:   {inner.Message}\n\n" : "") +
                    $"Stack:\n{ex.StackTrace}");

                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Controls ribbon button availability for ToggleOverheadProxyCommand.
    /// Uses the static in-process flag — NOT an ES lookup — so IsCommandAvailable
    /// is cheap enough to be called on every Revit idle tick.
    ///
    /// The button is unavailable when no document is open.
    /// It is always available when a document is open regardless of current state
    /// (toggle direction is handled inside the command itself).
    /// </summary>
    public class OverheadProxyCommandAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selectedCategories)
        {
            // Available whenever there is an active project document open.
            // Sheet/family documents excluded — overhead proxies only apply to project views.
            var doc = app.ActiveUIDocument?.Document;
            return doc != null && !doc.IsFamilyDocument;
        }
    }
}