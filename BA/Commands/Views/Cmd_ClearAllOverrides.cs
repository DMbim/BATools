using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_ClearAllOverrides : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var view = doc.ActiveView;
            if (view == null || view.IsTemplate)
            {
                message = "Active view is invalid or is a view template.";
                return Result.Failed;
            }

            if (!view.AreGraphicsOverridesAllowed())
            {
                message = "The active view does not support element graphic overrides.";
                return Result.Failed;
            }

            List<ElementId> targetIds;
            try
            {
                // CHANGED: document-wide collector instead of a view-scoped one.
                // FilteredElementCollector(doc, view.Id) silently excludes any
                // element hidden in the view (by element, category, filter, or
                // temporary hide/isolate) — already a confirmed pitfall in this
                // codebase (title block detection hit the same issue). That
                // caused hidden elements to be skipped from targetIds entirely,
                // undercounting both Cleared and Skipped.
                //
                // e.OwnerViewId == InvalidElementId  -> model elements, not
                //   owned by any specific view, always eligible.
                // e.OwnerViewId == view.Id           -> view-specific annotation
                //   elements (text notes, filled regions, detail lines, etc.)
                //   that belong to exactly this view.
                targetIds = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .Where(e => e.OwnerViewId == ElementId.InvalidElementId
                             || e.OwnerViewId == view.Id)
                    .Select(e => e.Id)
                    .ToList();
            }
            catch (Exception ex)
            {
                message = $"Failed to collect elements: {ex.Message}";
                AppLogger.LogError("Cmd_ClearAllOverrides.Collect", ex);
                return Result.Failed;
            }

            if (targetIds.Count == 0)
            {
                TaskDialog.Show("Clear Overrides", "No elements found for the active view.");
                return Result.Succeeded;
            }

            var defaultOverrides = new OverrideGraphicSettings();
            int clearedCount = 0;
            int skippedCount = 0;

            using (var tx = new Transaction(doc, "BA Clear All Overrides"))
            {
                tx.Start();
                try
                {
                    foreach (var id in targetIds)
                    {
                        try
                        {
                            view.SetElementOverrides(id, defaultOverrides);
                            clearedCount++;
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException)
                        {
                            // Element is not applicable to this view (e.g. not
                            // visible/relevant to the view's category rules,
                            // or from a different document context).
                            skippedCount++;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = $"Failed to clear overrides: {ex.Message}";
                    AppLogger.LogError("Cmd_ClearAllOverrides.SetOverrides", ex);
                    return Result.Failed;
                }
            }

            AppLogger.LogInfo(
                $"Cleared overrides on {clearedCount} element(s) in view '{view.Name}' ({skippedCount} skipped).");
            TaskDialog.Show("Clear Overrides",
                $"Cleared overrides on {clearedCount} element(s).\n{skippedCount} skipped (unsupported).");

            return Result.Succeeded;
        }
    }
}