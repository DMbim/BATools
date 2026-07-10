using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_WindowOrientation : IExternalCommand
    {
        private const string OrientationParameterName = "BA_Orientation";

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

            using (var tx = new Transaction(doc, "BA Window Orientation"))
            {
                tx.Start();
                try
                {
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
                            AppLogger.LogError($"Cmd_WindowOrientation.FacingOrientation [{familyInstance.Id.Value}]", ex);
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
                    tx.RollBack();
                    message = $"Failed to write orientation values: {ex.Message}";
                    AppLogger.LogError("Cmd_WindowOrientation.Execute", ex);
                    return Result.Failed;
                }
            }

            var summary = new StringBuilder();
            summary.AppendLine($"Updated {updatedCount} window(s).");
            if (skippedNotWindow.Count > 0)
                summary.AppendLine($"{skippedNotWindow.Count} skipped, not a window.");
            if (skippedNoParameter.Count > 0)
                summary.AppendLine($"{skippedNoParameter.Count} skipped, no '{OrientationParameterName}' parameter found.");
            if (skippedReadOnly.Count > 0)
                summary.AppendLine($"{skippedReadOnly.Count} skipped, '{OrientationParameterName}' is read only there.");
            if (skippedNoOrientation.Count > 0)
                summary.AppendLine($"{skippedNoOrientation.Count} skipped, could not resolve a facing direction.");

            AppLogger.LogInfo($"Cmd_WindowOrientation: updated {updatedCount}, not window {skippedNotWindow.Count}, no param {skippedNoParameter.Count}, read only {skippedReadOnly.Count}, no orientation {skippedNoOrientation.Count}.");
            TaskDialog.Show("Window Orientation", summary.ToString());
            return Result.Succeeded;
        }

        private static double VectorToDegreesFromNorth(XYZ facing)
        {
            double angleRad = Math.Atan2(facing.X, facing.Y);
            double angleDeg = angleRad * 180.0 / Math.PI;
            if (angleDeg < 0) angleDeg += 360.0;
            return angleDeg;
        }

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
        {
            return $"{element.Category?.Name ?? "Unknown"} (Id {element.Id.Value})";
        }
    }
}