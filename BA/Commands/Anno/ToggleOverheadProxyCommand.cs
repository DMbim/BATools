using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Overhead;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Commands
{
    /// <summary>
    /// Pure quick toggle. Flips the Overhead proxy auto updater on or off for the active
    /// document in a single click, no dialog. Settings access lives exclusively in
    /// Cmd_OverheadAutoDash now, the Shift modifier settings gate previously present here
    /// has been removed since it duplicated that command's responsibility.
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

            if (doc.ActiveView is not ViewPlan view || view.ViewType != ViewType.FloorPlan)
            {
                TaskDialog.Show("Overhead Auto Dash", "Active view must be a Floor Plan.");
                return Result.Cancelled;
            }

            try
            {
                var settings = OverheadSettingsStore.Load(doc, out _) ?? OverheadSettings.Default();
                settings.Normalize();

                bool currentlyEnabled = settings.Enabled;
                bool targetEnabled = !currentlyEnabled;

                var result = OverheadGlobalService.SetEnabled(doc, targetEnabled);

                TaskDialog.Show("BA Overhead Proxies", result.ToString());

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException;
                TaskDialog.Show("BA Overhead Exception",
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
    /// Controls ribbon button availability for ToggleOverheadProxyCommand. Uses the static
    /// in-process flag rather than an ES lookup so IsCommandAvailable is cheap enough to be
    /// called on every Revit idle tick.
    /// </summary>
    public class OverheadProxyCommandAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selectedCategories)
        {
            var doc = app.ActiveUIDocument?.Document;
            return doc != null && !doc.IsFamilyDocument;
        }
    }
}