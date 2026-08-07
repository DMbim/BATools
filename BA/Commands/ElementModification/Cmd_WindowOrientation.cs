// BA/Commands/Cmd_WindowOrientation.cs
using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core;


namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_WindowOrientation : IExternalCommand
    {
        private const string OrientationParameterName = "BA_Orientation";

        private const string SharedParamFilePath =
            @"S:\CAD\Autodesk Revit\BA_Resources\BA_Shared parameters\BA_SharedParametersWIP2.txt";

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

            var selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds.Count == 0)
            {
                TaskDialog.Show("Window Orientation", "Select one or more windows first.");
                return Result.Cancelled;
            }

            int updatedCount = 0;
            var skippedNotWindow = new List<string>();
            var skippedNoParameter = new List<string>();
            var skippedReadOnly = new List<string>();
            var skippedNoOrientation = new List<string>();

            using var tx = new Transaction(doc, "BA Window Orientation");
            tx.Start();

            try
            {
                // ---------------------------------------------------------- //
                //  Ensure BA_Orientation is bound to the Windows category.
                //  If it is not present we load it from the shared parameter
                //  file by name and create an instance binding.
                // ---------------------------------------------------------- //
                if (!EnsureParameterBound(doc, app, out string bindError))
                {
                    tx.RollBack();
                    message = bindError;
                    return Result.Failed;
                }

                // ---------------------------------------------------------- //
                //  Process selected elements
                // ---------------------------------------------------------- //
                foreach (var id in selectedIds)
                {
                    var element = doc.GetElement(id);
                    if (element == null) continue;

                    if (element is not FamilyInstance familyInstance ||
                        familyInstance.Category == null ||
                        familyInstance.Category.Id.Value != (long)BuiltInCategory.OST_Windows)
                    {
                        skippedNotWindow.Add(DescribeElement(element));
                        continue;
                    }

                    var orientationParam = familyInstance.LookupParameter(OrientationParameterName);
                    if (orientationParam == null)
                    {
                        skippedNoParameter.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    if (orientationParam.IsReadOnly)
                    {
                        skippedReadOnly.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    XYZ facing;
                    try
                    {
                        facing = familyInstance.FacingOrientation;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError(
                            $"Cmd_WindowOrientation.FacingOrientation [{familyInstance.Id.Value}]", ex);
                        skippedNoOrientation.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    if (facing == null || (facing.X == 0 && facing.Y == 0))
                    {
                        skippedNoOrientation.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    double degrees = VectorToDegreesFromNorth(facing);

                    if (!TryWriteOrientationValue(orientationParam, degrees))
                    {
                        skippedNoOrientation.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    updatedCount++;
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();

                message = $"Failed to write orientation values: {ex.Message}";
                AppLogger.LogError("Cmd_WindowOrientation.Execute", ex);
                return Result.Failed;
            }

            // ---------------------------------------------------------------- //
            //  Summary
            // ---------------------------------------------------------------- //
            var summary = new StringBuilder();
            summary.AppendLine($"Updated {updatedCount} window(s).");
            if (skippedNotWindow.Count > 0)
                summary.AppendLine($"{skippedNotWindow.Count} skipped — not a window.");
            if (skippedNoParameter.Count > 0)
                summary.AppendLine(
                    $"{skippedNoParameter.Count} skipped — '{OrientationParameterName}' " +
                    "parameter not found on instance. The family may not expose it.");
            if (skippedReadOnly.Count > 0)
                summary.AppendLine(
                    $"{skippedReadOnly.Count} skipped — '{OrientationParameterName}' is read-only.");
            if (skippedNoOrientation.Count > 0)
                summary.AppendLine(
                    $"{skippedNoOrientation.Count} skipped — could not resolve a facing direction.");

            AppLogger.LogInfo(
                $"Cmd_WindowOrientation: updated {updatedCount}, " +
                $"not window {skippedNotWindow.Count}, " +
                $"no param {skippedNoParameter.Count}, " +
                $"read only {skippedReadOnly.Count}, " +
                $"no orientation {skippedNoOrientation.Count}.");

            TaskDialog.Show("Window Orientation", summary.ToString());
            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ //
        //  PARAMETER BINDING
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Checks whether BA_Orientation is already bound to the Windows
        /// category as an instance parameter. If not, loads it from the
        /// shared parameter file by name and creates the binding.
        /// Must be called inside an active transaction.
        /// Returns true if the parameter is ready to use, false with an
        /// error message if the binding could not be established.
        /// </summary>
        private static bool EnsureParameterBound(
            Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            out string error)
        {
            error = string.Empty;

            var bindingMap = doc.ParameterBindings;
            var windowsCategory = doc.Settings.Categories
                .get_Item(BuiltInCategory.OST_Windows);

            // Walk existing bindings — look for a definition whose name
            // matches BA_Orientation that is already bound to Windows.
            var it = bindingMap.ForwardIterator();
            while (it.MoveNext())
            {
                var def = it.Key as Definition;
                if (def == null) continue;
                if (!def.Name.Equals(OrientationParameterName,
                        StringComparison.OrdinalIgnoreCase)) continue;

                // Definition found — check that Windows is in its category set.
                if (it.Current is InstanceBinding existing)
                {
                    foreach (Category c in existing.Categories)
                    {
                        if (c.Id.Value == (long)BuiltInCategory.OST_Windows)
                            return true; // already bound correctly
                    }

                    // Bound but Windows category missing — add it.
                    var cats = existing.Categories;
                    cats.Insert(windowsCategory);
                    bindingMap.ReInsert(def, existing);
                    return true;
                }
            }

            // Not bound at all — load from the shared parameter file.
            ExternalDefinition extDef;
            try
            {
                extDef = SharedParamUtils.FindExternalDefinitionByGuidOrName(
                    app,
                    SharedParamFilePath,
                    OrientationParameterName,
                    Guid.Empty);
            }
            catch (Exception ex)
            {
                error = $"Could not open shared parameter file:\n{ex.Message}\n\n" +
                        $"Expected path:\n{SharedParamFilePath}";
                AppLogger.LogError("Cmd_WindowOrientation.EnsureParameterBound", ex);
                return false;
            }

            if (extDef == null)
            {
                error = $"'{OrientationParameterName}' was not found in the shared " +
                        $"parameter file at:\n{SharedParamFilePath}\n\n" +
                        "Add the parameter to the file and retry.";
                return false;
            }

            // Build a category set containing only Windows.
            var categorySet = app.Create.NewCategorySet();
            categorySet.Insert(windowsCategory);

            var binding = app.Create.NewInstanceBinding(categorySet);

            bool inserted = bindingMap.Insert(extDef, binding,
                GroupTypeId.Data);

            if (!inserted)
            {
                error = $"Failed to bind '{OrientationParameterName}' to the " +
                        "Windows category. The parameter may already exist with " +
                        "a conflicting binding.";
                return false;
            }

            AppLogger.LogInfo(
                $"Cmd_WindowOrientation: bound '{OrientationParameterName}' " +
                "to Windows category from shared parameter file.");

            return true;
        }

        // ------------------------------------------------------------------ //
        //  GEOMETRY
        // ------------------------------------------------------------------ //

        private static double VectorToDegreesFromNorth(XYZ facing)
        {
            double angleRad = Math.Atan2(facing.X, facing.Y);
            double angleDeg = angleRad * 180.0 / Math.PI;
            if (angleDeg < 0) angleDeg += 360.0;
            return angleDeg;
        }

        // ------------------------------------------------------------------ //
        //  PARAMETER WRITE
        // ------------------------------------------------------------------ //

        private static bool TryWriteOrientationValue(Parameter orientationParam, double degrees)
        {
            try
            {
                if (orientationParam.StorageType == StorageType.String)
                {
                    string cardinal = DegreesToCardinal(degrees);
                    orientationParam.Set($"{degrees:F1} deg ({cardinal})");
                    return true;
                }

                if (orientationParam.StorageType == StorageType.Double)
                {
                    var dataType = orientationParam.Definition.GetDataType();
                    double valueToSet = dataType == SpecTypeId.Angle
                        ? UnitUtils.ConvertToInternalUnits(degrees, UnitTypeId.Degrees)
                        : degrees;

                    orientationParam.Set(valueToSet);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_WindowOrientation.TryWriteOrientationValue", ex);
                return false;
            }
        }

        private static string DegreesToCardinal(double degrees)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N" };
            int index = (int)Math.Round(degrees / 45.0);
            return directions[index];
        }

        private static string DescribeElement(Element element)
            => $"{element.Category?.Name ?? "Unknown"} (Id {element.Id.Value})";
    }
}