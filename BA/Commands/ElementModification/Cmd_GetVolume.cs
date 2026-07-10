using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;

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
            var selectedIds = uiDoc.Selection.GetElementIds();

            if (selectedIds.Count == 0)
            {
                TaskDialog.Show("Get Volume", "Select one or more elements first.");
                return Result.Cancelled;
            }

            int updatedCount = 0;
            var skippedNoParameter = new List<string>();
            var skippedReadOnly = new List<string>();
            var skippedNoGeometry = new List<string>();

            using (var tx = new Transaction(doc, "BA Get Volume"))
            {
                tx.Start();
                try
                {
                    foreach (var id in selectedIds)
                    {
                        var element = doc.GetElement(id);
                        if (element == null) continue;

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
                        {
                            volume = GetGeometryVolume(element);
                        }

                        if (volume <= MinSolidVolume)
                        {
                            skippedNoGeometry.Add(DescribeElement(element));
                            continue;
                        }

                        volumeParam.Set(volume);
                        updatedCount++;
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = $"Failed to write volume values: {ex.Message}";
                    AppLogger.LogError("Cmd_GetVolume.Execute", ex);
                    return Result.Failed;
                }
            }

            var summary = new StringBuilder();
            summary.AppendLine($"Updated {updatedCount} element(s).");
            if (skippedNoParameter.Count > 0)
                summary.AppendLine($"{skippedNoParameter.Count} skipped, no '{VolumeParameterName}' parameter on that category.");
            if (skippedReadOnly.Count > 0)
                summary.AppendLine($"{skippedReadOnly.Count} skipped, '{VolumeParameterName}' is read only there.");
            if (skippedNoGeometry.Count > 0)
                summary.AppendLine($"{skippedNoGeometry.Count} skipped, no solid geometry found.");

            AppLogger.LogInfo($"Cmd_GetVolume: updated {updatedCount}, no param {skippedNoParameter.Count}, read only {skippedReadOnly.Count}, no geometry {skippedNoGeometry.Count}.");
            TaskDialog.Show("Get Volume", summary.ToString());
            return Result.Succeeded;
        }

        private static double GetBuiltInVolume(Element element)
        {
            var param = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (param != null && param.HasValue && param.StorageType == StorageType.Double)
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
                AppLogger.LogError($"Cmd_GetVolume.GetGeometryVolume [{element.Id.Value}]", ex);
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

        private static string DescribeElement(Element element)
        {
            return $"{element.Category?.Name ?? "Unknown"} (Id {element.Id.Value})";
        }
    }
}