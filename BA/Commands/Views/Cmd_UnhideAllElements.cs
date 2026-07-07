using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_UnhideAllElements : IExternalCommand
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

            List<ElementId> hiddenIds = new List<ElementId>();

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                foreach (var element in collector)
                {
                    if (element.OwnerViewId != ElementId.InvalidElementId &&
                        element.OwnerViewId != view.Id)
                    {
                        continue;
                    }

                    bool isHidden;
                    bool canBeHidden;

                    try
                    {
                        isHidden = element.IsHidden(view);
                        canBeHidden = element.CanBeHidden(view);
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        continue;
                    }

                    if (isHidden && canBeHidden)
                    {
                        hiddenIds.Add(element.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                message = $"Failed to collect hidden elements: {ex.Message}";
                AppLogger.LogError("Cmd_UnhideAllElements.Collect", ex);
                return Result.Failed;
            }

            if (hiddenIds.Count == 0)
            {
                TaskDialog.Show("Unhide Elements", "No permanently hidden elements found in the active view.");
                return Result.Succeeded;
            }

            using (var tx = new Transaction(doc, "BA Unhide All Elements"))
            {
                tx.Start();
                try
                {
                    view.UnhideElements(hiddenIds);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = $"Failed to unhide elements: {ex.Message}";
                    AppLogger.LogError("Cmd_UnhideAllElements.UnhideElements", ex);
                    return Result.Failed;
                }
            }

            AppLogger.LogInfo($"Unhid {hiddenIds.Count} element(s) in view '{view.Name}'.");
            TaskDialog.Show("Unhide Elements", $"Unhid {hiddenIds.Count} element(s).");
            return Result.Succeeded;
        }
    }
}