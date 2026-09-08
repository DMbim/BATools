// BA/Commands/Cmd_GetVolume.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.BAApplication;
using BA.Core.Parameters;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_GetVolume : IExternalCommand
    {
        private const string VolumeParameterName = "BA_Volume";
        private const double MinSolidVolume = 1e-9;

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
            //  Step 1: Fail fast if BA_Volume isn't even defined in the shared
            //  parameter file, before bothering the user with a pick loop.
            //  Uses FindExternalDefinitionByName directly, the same lookup
            //  SharedParameterBindingService.EnsureBound performs internally, so
            //  this pre-check can't disagree with what actually happens later.
            //  Read-only against the SP file, no document transaction needed.
            // ---------------------------------------------------------------- //
            try
            {
                var def = SharedParameterFileReader.FindExternalDefinitionByName(
                    app, SharedParamPaths.WIP2, VolumeParameterName);

                if (def == null)
                {
                    message =
                        $"'{VolumeParameterName}' was not found anywhere in the shared " +
                        $"parameter file at:\n{SharedParamPaths.WIP2}\n\n" +
                        "Add the parameter to the file and retry.";
                    TaskDialog.Show("Get Volume — Setup Error", message);
                    return Result.Failed;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Get Volume — Setup Error", ex.Message);
                AppLogger.LogError("Cmd_GetVolume.FindExternalDefinitionByName", ex);
                return Result.Failed;
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
            //  Step 3: Collect selected elements and their distinct categories.
            //  Keyed by ElementId.Value to dedupe, no custom IEqualityComparer
            //  needed for that.
            // ---------------------------------------------------------------- //
            var selectedElements = new List<Element>();
            var selectedCategories = new Dictionary<long, Category>();

            foreach (var r in refs)
            {
                var el = doc.GetElement(r.ElementId);
                if (el == null) continue;
                selectedElements.Add(el);

                if (el.Category != null)
                    selectedCategories[el.Category.Id.Value] = el.Category;
            }

            if (selectedCategories.Count == 0)
            {
                message = "None of the selected elements have a category. Nothing to bind or write.";
                TaskDialog.Show("Get Volume", message);
                return Result.Cancelled;
            }

            int updatedCount = 0;
            var skippedNoParameter = new List<string>();
            var skippedReadOnly = new List<string>();
            var skippedNoGeometry = new List<string>();

            using var writeTx = new Transaction(doc, "BA Get Volume");
            writeTx.Start();

            try
            {
                // One call covering every category present in the selection. EnsureBound
                // (the multi-category overload) merges categories into the existing binding
                // if one exists, or creates it fresh if not, atomically, with its own
                // capture/restore data-safety net around the Remove+Insert fallback path.
                // If this throws, nothing has been written (guaranteed by that method's own
                // design), so the whole transaction rolls back and no partial state exists.
                //
                // createIfMissing is deliberately false: if it created the definition, it
                // would create it as a String parameter (CreateExternalDefinition_String),
                // which is the wrong spec type for a volume value. The Step 1 pre-check
                // above already guarantees the definition exists before we get here.
                SharedParameterBindingService.EnsureBound(
                    app,
                    doc,
                    SharedParamPaths.WIP2,
                    VolumeParameterName,
                    Guid.Empty,
                    null,
                    isInstance: true,
                    categories: selectedCategories.Values.ToList(),
                    createIfMissing: false);

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

                message = $"Failed to bind or write '{VolumeParameterName}': {ex.Message}";
                AppLogger.LogError("Cmd_GetVolume.Execute", ex);
                TaskDialog.Show("Get Volume — Error", message);
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
}