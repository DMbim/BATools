// BA/Commands/Cmd_RevealHost.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.BAApplication;

namespace BA.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_RevealHost : IExternalCommand
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

            // ---------------------------------------------------------------- //
            //  Pick exactly one element.
            // ---------------------------------------------------------------- //
            Reference pickedRef;
            try
            {
                pickedRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    "Pick a dimension, hosted element, or stacked wall segment");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            var element = doc.GetElement(pickedRef.ElementId);
            if (element == null)
            {
                message = "Selected element could not be resolved.";
                return Result.Failed;
            }

            // ---------------------------------------------------------------- //
            //  Resolve host / references.
            // ---------------------------------------------------------------- //
            var targetIds = new HashSet<ElementId>();

            switch (element)
            {
                case Dimension dimension:
                    CollectDimensionReferences(dimension, targetIds);
                    break;

                case FamilyInstance familyInstance:
                    if (familyInstance.Host != null)
                        targetIds.Add(familyInstance.Host.Id);
                    break;

                case Wall wall:
                    if (wall.StackedWallOwnerId != ElementId.InvalidElementId)
                        targetIds.Add(wall.StackedWallOwnerId);
                    break;
            }

            targetIds.Remove(element.Id);

            if (targetIds.Count == 0)
            {
                TaskDialog.Show("Reveal Host",
                    $"No host or reference found for this " +
                    $"{element.Category?.Name ?? "element"}.");
                return Result.Succeeded;
            }

            // ---------------------------------------------------------------- //
            //  Select and zoom to results.
            // ---------------------------------------------------------------- //
            try
            {
                uiDoc.Selection.SetElementIds(targetIds);
                uiDoc.ShowElements(targetIds);
            }
            catch (Exception ex)
            {
                message = $"Failed to select or show referenced elements: {ex.Message}";
                AppLogger.LogError("Cmd_RevealHost.Execute", ex);
                return Result.Failed;
            }

            AppLogger.LogInfo(
                $"Cmd_RevealHost: revealed {targetIds.Count} reference(s) " +
                $"for element {element.Id.Value}.");

            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ //
        //  HELPERS
        // ------------------------------------------------------------------ //

        private static void CollectDimensionReferences(
            Dimension dimension,
            HashSet<ElementId> targetIds)
        {
            var references = dimension.References;
            if (references == null) return;

            foreach (Reference reference in references)
            {
                if (reference == null) continue;
                var id = reference.ElementId;
                if (id != null && id != ElementId.InvalidElementId)
                    targetIds.Add(id);
            }
        }
    }
}