// BA/Commands/Cmd_GetVolume.cs
using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.BAApplication;
using BA.Core;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_GetVolume : IExternalCommand
    {
        private const string VolumeParameterName = "BA_Volume";
        private const double MinSolidVolume = 1e-9;

        private const string SharedParamFilePath =
            @"S:\CAD\Autodesk Revit\BA_Resources\BA_Shared parameters\BA_SharedParametersWIP2";

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

            // ---------------------------------------------------------------- //
            //  Step 1: Ensure BA_Volume is bound before asking the user to pick.
            //  We need a transaction for the binding but we commit it immediately
            //  so the parameter is registered before the pick loop starts.
            //  The write transaction runs separately after selection.
            // ---------------------------------------------------------------- //
            using (var bindTx = new Transaction(doc, "BA — Bind BA_Volume"))
            {
                bindTx.Start();

                // Pass an empty category set here — EnsureParameterBound will
                // skip the category-augmentation path and only create the
                // binding with a broad set if the parameter is absent entirely.
                // We will add any missing categories during the write pass.
                if (!EnsureParameterBoundPreSelection(doc, app, out string bindError))
                {
                    bindTx.RollBack();
                    TaskDialog.Show("Get Volume — Setup Error", bindError);
                    return Result.Failed;
                }

                bindTx.Commit();
            }

            // ---------------------------------------------------------------- //
            //  Step 2: Prompt user to select elements.
            // ---------------------------------------------------------------- //
            IList<Reference> refs;
            try
            {
                refs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    "Select elements to compute volume — press Finish when done");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (refs == null || refs.Count == 0)
                return Result.Cancelled;

            // ---------------------------------------------------------------- //
            //  Step 3: Collect selected elements and ensure their categories
            //  are included in the binding, then write volumes.
            // ---------------------------------------------------------------- //
            var selectedElements = new List<Element>();
            var selectedCategories = new HashSet<Category>(new CategoryIdComparer());

            foreach (var r in refs)
            {
                var el = doc.GetElement(r.ElementId);
                if (el == null) continue;
                selectedElements.Add(el);
                if (el.Category != null)
                    selectedCategories.Add(el.Category);
            }

            int updatedCount = 0;
            var skippedNoParameter = new List<string>();
            var skippedReadOnly = new List<string>();
            var skippedNoGeometry = new List<string>();

            using var writeTx = new Transaction(doc, "BA Get Volume");
            writeTx.Start();

            try
            {
                // Add any newly encountered categories to the existing binding.
                if (!EnsureParameterBoundForCategories(doc, app, selectedCategories,
                        out string catError))
                {
                    writeTx.RollBack();
                    message = catError;
                    return Result.Failed;
                }

                foreach (var element in selectedElements)
                {
                    var volumeParam = element.LookupParameter(VolumeParameterName);
                    if (volumeParam == null)
                    {
                        skippedNoParameter.Add(DescribeElement(element));
                        continue;
                    }

                    if (volumeParam.IsReadOnly)
                    {
                        skippedReadOnly.Add(DescribeElement(element));
                        continue;
                    }

                    double volume = GetBuiltInVolume(element);

                    if (volume <= MinSolidVolume)
                        volume = GetGeometryVolume(element);

                    if (volume <= MinSolidVolume)
                    {
                        skippedNoGeometry.Add(DescribeElement(element));
                        continue;
                    }

                    volumeParam.Set(volume);
                    updatedCount++;
                }

                writeTx.Commit();
            }
            catch (Exception ex)
            {
                if (writeTx.GetStatus() == TransactionStatus.Started)
                    writeTx.RollBack();

                message = $"Failed to write volume values: {ex.Message}";
                AppLogger.LogError("Cmd_GetVolume.Execute", ex);
                return Result.Failed;
            }

            // ---------------------------------------------------------------- //
            //  Summary
            // ---------------------------------------------------------------- //
            var summary = new StringBuilder();
            summary.AppendLine($"Updated {updatedCount} element(s).");
            if (skippedNoParameter.Count > 0)
                summary.AppendLine(
                    $"{skippedNoParameter.Count} skipped — '{VolumeParameterName}' " +
                    "not found on instance. The family may not expose it.");
            if (skippedReadOnly.Count > 0)
                summary.AppendLine(
                    $"{skippedReadOnly.Count} skipped — '{VolumeParameterName}' is read-only.");
            if (skippedNoGeometry.Count > 0)
                summary.AppendLine(
                    $"{skippedNoGeometry.Count} skipped — no solid geometry found.");

            AppLogger.LogInfo(
                $"Cmd_GetVolume: updated {updatedCount}, " +
                $"no param {skippedNoParameter.Count}, " +
                $"read only {skippedReadOnly.Count}, " +
                $"no geometry {skippedNoGeometry.Count}.");

            TaskDialog.Show("Get Volume", summary.ToString());
            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ //
        //  PARAMETER BINDING — pre-selection pass
        //  Called before the pick loop. If BA_Volume is already bound,
        //  returns true immediately. If not bound, loads from the shared
        //  parameter file and creates a binding with an empty category set
        //  (Revit accepts this; categories are added in the write pass).
        // ------------------------------------------------------------------ //
        private static bool EnsureParameterBoundPreSelection(
            Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            out string error)
        {
            error = string.Empty;
            var bindingMap = doc.ParameterBindings;

            // Check if already bound.
            var it = bindingMap.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key is Definition def &&
                    def.Name.Equals(VolumeParameterName, StringComparison.OrdinalIgnoreCase) &&
                    it.Current is InstanceBinding)
                    return true;
            }

            // Not bound — load from shared parameter file.
            ExternalDefinition extDef;
            try
            {
                extDef = SharedParamUtils.FindExternalDefinitionByGuidOrName(
                    app,
                    SharedParamFilePath,
                    VolumeParameterName,
                    Guid.Empty);
            }
            catch (Exception ex)
            {
                error = $"Could not open shared parameter file:\n{ex.Message}\n\n" +
                        $"Expected path:\n{SharedParamFilePath}";
                AppLogger.LogError("Cmd_GetVolume.EnsureParameterBoundPreSelection", ex);
                return false;
            }

            if (extDef == null)
            {
                error = $"'{VolumeParameterName}' was not found in the shared " +
                        $"parameter file at:\n{SharedParamFilePath}\n\n" +
                        "Add the parameter to the file and retry.";
                return false;
            }

            // Create binding with an initially empty category set.
            // Categories are added after selection in EnsureParameterBoundForCategories.
            var ftId = extDef.GetDataType();
            var categorySet = app.Create.NewCategorySet();
            var binding = app.Create.NewInstanceBinding(categorySet);
            bool inserted = bindingMap.Insert(extDef, binding, ftId);

            if (!inserted)
            {
                // Insert can return false if it already existed — not an error.
                AppLogger.LogInfo(
                    "Cmd_GetVolume: Insert returned false during pre-selection bind " +
                    "(parameter may already be partially bound). Proceeding.");
            }
            else
            {
                AppLogger.LogInfo(
                    $"Cmd_GetVolume: pre-selection bound '{VolumeParameterName}' " +
                    "from shared parameter file.");
            }

            return true;
        }

        // ------------------------------------------------------------------ //
        //  PARAMETER BINDING — post-selection category augmentation
        //  Adds any categories from the selection that are not yet in the
        //  existing binding. Must be called inside an active transaction.
        // ------------------------------------------------------------------ //
        private static bool EnsureParameterBoundForCategories(
            Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            IEnumerable<Category> requiredCategories,
            out string error)
        {
            error = string.Empty;
            var bindingMap = doc.ParameterBindings;

            Definition existingDef = null;
            InstanceBinding existingBinding = null;

            var it = bindingMap.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key is Definition def &&
                    def.Name.Equals(VolumeParameterName, StringComparison.OrdinalIgnoreCase) &&
                    it.Current is InstanceBinding ib)
                {
                    existingDef = def;
                    existingBinding = ib;
                    break;
                }
            }

            if (existingBinding == null)
            {
                // Should not happen after the pre-selection pass but guard anyway.
                error = $"'{VolumeParameterName}' binding disappeared unexpectedly.";
                return false;
            }

            bool modified = false;
            foreach (var cat in requiredCategories)
            {
                bool alreadyPresent = false;
                foreach (Category bound in existingBinding.Categories)
                {
                    if (bound.Id.Value == cat.Id.Value)
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    existingBinding.Categories.Insert(cat);
                    modified = true;
                }
            }

            if (modified)
                bindingMap.ReInsert(existingDef!, existingBinding);

            return true;
        }

        // ------------------------------------------------------------------ //
        //  VOLUME EXTRACTION
        // ------------------------------------------------------------------ //

        private static double GetBuiltInVolume(Element element)
        {
            var param = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (param != null && param.HasValue &&
                param.StorageType == StorageType.Double)
            {
                double value = param.AsDouble();
                if (value > MinSolidVolume) return value;
            }

            return 0.0;
        }

        private static double GetGeometryVolume(Element element)
        {
            var options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geomElement;
            try
            {
                geomElement = element.get_Geometry(options);
            }
            catch (Exception ex)
            {
                AppLogger.LogError(
                    $"Cmd_GetVolume.GetGeometryVolume [{element.Id.Value}]", ex);
                return 0.0;
            }

            return geomElement == null ? 0.0 : SumSolidVolume(geomElement);
        }

        private static double SumSolidVolume(GeometryElement geomElement)
        {
            double total = 0.0;

            foreach (var geomObj in geomElement)
            {
                switch (geomObj)
                {
                    case Solid solid when solid.Volume > MinSolidVolume:
                        total += solid.Volume;
                        break;

                    case GeometryInstance instance:
                        var instGeom = instance.GetInstanceGeometry();
                        if (instGeom != null)
                            total += SumSolidVolume(instGeom);
                        break;
                }
            }

            return total;
        }

        // ------------------------------------------------------------------ //
        //  HELPERS
        // ------------------------------------------------------------------ //

        private static string DescribeElement(Element element)
            => $"{element.Category?.Name ?? "Unknown"} (Id {element.Id.Value})";
    }

    // ---------------------------------------------------------------------- //
    //  Category equality comparer keyed on ElementId value
    // ---------------------------------------------------------------------- //
    internal sealed class CategoryIdComparer : IEqualityComparer<Category>
    {
        public bool Equals(Category? x, Category? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Id.Value == y.Id.Value;
        }

        public int GetHashCode(Category obj)
            => obj.Id.Value.GetHashCode();
    }
}