// File: BA/Commands/Cmd_WriteElementIds.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Parameters;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_WriteElementIds : IExternalCommand
    {
        private const string ElementIdParameterName = "BA_Element_ID";

        /// <summary>
        /// Categories excluded even though some may also report CategoryType.Model. This is the
        /// actual contract for "physical 3D element" on this command, not an assumption about how
        /// the API happens to classify these categories.
        /// </summary>
        private static readonly HashSet<BuiltInCategory> ExcludedCategories = new()
        {
            BuiltInCategory.OST_Rooms,
            BuiltInCategory.OST_Areas,
            BuiltInCategory.OST_Grids,
            BuiltInCategory.OST_Levels
        };

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
            var app = uiApp.Application;

            // Step 1: fail fast if BA_Element_ID is not defined in the shared parameter file,
            // same pre check pattern as Cmd_GetVolume. Uses the identical lookup EnsureBound
            // performs internally, so this pre check cannot disagree with what happens later.
            try
            {
                var def = SharedParameterFileReader.FindExternalDefinitionByName(
                    app, SharedParamPaths.WIP2, ElementIdParameterName);

                if (def == null)
                {
                    message =
                        $"'{ElementIdParameterName}' was not found anywhere in the shared " +
                        $"parameter file at:\n{SharedParamPaths.WIP2}\n\n" +
                        "Add the parameter to the file and retry.";
                    TaskDialog.Show("Write Element IDs: Setup Error", message);
                    return Result.Failed;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Write Element IDs: Setup Error", ex.Message);
                AppLogger.LogError("Cmd_WriteElementIds.FindExternalDefinitionByName", ex);
                return Result.Failed;
            }

            // Step 2: collect every physical model element in the project. WhereElementIsNotElementType
            // excludes type objects, WhereElementIsViewIndependent excludes elements owned by a
            // specific view (annotation, view specific detail items and similar). Category.CategoryType
            // == Model narrows to physical categories, then the exclusion list removes Rooms, Areas,
            // Grids, and Levels explicitly, per your confirmed scope.
            var candidates = new List<Element>();
            var categoriesPresent = new Dictionary<long, Category>();

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent();

            foreach (Element element in collector)
            {
                Category category = element.Category;
                if (category == null) continue;
                if (category.CategoryType != CategoryType.Model) continue;
                if (ExcludedCategories.Contains(category.BuiltInCategory)) continue;

                candidates.Add(element);
                categoriesPresent[category.Id.Value] = category;
            }

            if (candidates.Count == 0)
            {
                message = "No matching 3D elements found in the project.";
                TaskDialog.Show("Write Element IDs", message);
                return Result.Cancelled;
            }

            // Step 3: this command has no per element selection step like Cmd_GetVolume's
            // PickObjects, it touches the whole project in one pass and always overwrites per your
            // answer, so a summary count in front of the write is a cheap safety net.
            TaskDialogResult confirm = TaskDialog.Show(
                "Write Element IDs",
                $"This will write '{ElementIdParameterName}' on {candidates.Count} element(s) " +
                $"across {categoriesPresent.Count} categories, overwriting any existing value. " +
                "Continue?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

            if (confirm != TaskDialogResult.Yes)
                return Result.Cancelled;

            int updatedCount = 0;
            var skippedNoParameter = new List<string>();
            var skippedReadOnly = new List<string>();

            using var writeTx = new Transaction(doc, "BA Write Element IDs");
            writeTx.Start();

            try
            {
                // createIfMissing is deliberately false, same reasoning as Cmd_GetVolume: the
                // Step 1 pre check above already guarantees the definition exists. If it did not,
                // letting EnsureBound create it would default to a String spec type by way of
                // CreateExternalDefinition_String, which we do not want triggered silently here.
                SharedParameterBindingService.EnsureBound(
                    app,
                    doc,
                    SharedParamPaths.WIP2,
                    ElementIdParameterName,
                    Guid.Empty,
                    null,
                    isInstance: true,
                    categories: categoriesPresent.Values.ToList(),
                    createIfMissing: false);

                foreach (Element element in candidates)
                {
                    Parameter idParam = element.LookupParameter(ElementIdParameterName);
                    if (idParam == null)
                    {
                        skippedNoParameter.Add(DescribeElement(element));
                        continue;
                    }

                    if (idParam.IsReadOnly)
                    {
                        skippedReadOnly.Add(DescribeElement(element));
                        continue;
                    }

                    idParam.Set(element.Id.Value.ToString());
                    updatedCount++;
                }

                writeTx.Commit();
            }
            catch (Exception ex)
            {
                if (writeTx.GetStatus() == TransactionStatus.Started)
                    writeTx.RollBack();

                message = $"Failed to bind or write '{ElementIdParameterName}': {ex.Message}";
                AppLogger.LogError("Cmd_WriteElementIds.Execute", ex);
                TaskDialog.Show("Write Element IDs: Error", message);
                return Result.Failed;
            }

            var summary = new StringBuilder();
            summary.AppendLine($"Updated {updatedCount} element(s).");

            if (skippedNoParameter.Count > 0)
                summary.AppendLine(
                    $"{skippedNoParameter.Count} skipped, '{ElementIdParameterName}' not found " +
                    "on instance. The family may not expose it.");
            if (skippedReadOnly.Count > 0)
                summary.AppendLine(
                    $"{skippedReadOnly.Count} skipped, '{ElementIdParameterName}' is read only.");

            AppLogger.LogInfo(
                $"Cmd_WriteElementIds: updated {updatedCount}, " +
                $"no param {skippedNoParameter.Count}, " +
                $"read only {skippedReadOnly.Count}.");

            TaskDialog.Show("Write Element IDs", summary.ToString());
            return Result.Succeeded;
        }

        private static string DescribeElement(Element element)
            => $"{element.Category?.Name ?? "Unknown"} (Id {element.Id.Value})";
    }
}