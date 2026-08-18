// BA/Commands/Cmd_ToggleGhostMarkupVisibility.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.GhostMarkup;

namespace BA.Commands
{
    /// <summary>
    /// Manual stopgap for the native Print dialog gap, there is no public
    /// Revit API event that fires before File Print, so exports triggered
    /// outside of ExportJobRunner (Ctrl+P) are not covered automatically.
    /// Run this before a manual print to hide, run it again after to
    /// restore. Unlike the export path, this is a real persistent hide via
    /// a normal committed Transaction, not a rollback scope, because the
    /// hidden state has to survive across the print dialog and potentially
    /// a save.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_ToggleGhostMarkupVisibility : IExternalCommand
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
                message = "No active graphical view.";
                return Result.Failed;
            }

            try
            {
                var ghostIds = FindGhostMarkupElementIds(doc, view.Id);

                if (ghostIds.Count == 0)
                {
                    TaskDialog.Show("Ghost Markup", "No ghost markup elements found in the active view.");
                    return Result.Succeeded;
                }

                var currentlyHiddenIds = ghostIds
                    .Where(id =>
                    {
                        var element = doc.GetElement(id);
                        return element != null && element.IsHidden(view);
                    })
                    .ToList();

                var shouldShow = currentlyHiddenIds.Count == ghostIds.Count;

                using (var tx = new Transaction(doc, shouldShow ? "Show ghost markup" : "Hide ghost markup"))
                {
                    tx.Start();

                    if (shouldShow)
                    {
                        view.UnhideElements(ghostIds);
                    }
                    else
                    {
                        var idsToHide = ghostIds.Except(currentlyHiddenIds).ToList();
                        view.HideElements(idsToHide);
                    }

                    tx.Commit();
                }

                AppLogger.LogInfo($"Ghost markup visibility toggled in view {view.Name}, now {(shouldShow ? "visible" : "hidden")}.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Ghost markup visibility toggle failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static List<ElementId> FindGhostMarkupElementIds(Document doc, ElementId viewId)
        {
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_TextNotes,
                BuiltInCategory.OST_Lines,
                BuiltInCategory.OST_DetailComponents
            };

            var multiCategoryFilter = new ElementMulticategoryFilter(categories);

            return new FilteredElementCollector(doc)
                .WherePasses(multiCategoryFilter)
                .WhereElementIsNotElementType()
                .Where(e => e.OwnerViewId == viewId)
                .Where(e => GhostMarkupDetector.IsGhostMarkup(e, doc))
                .Select(e => e.Id)
                .ToList();
        }
    }
}