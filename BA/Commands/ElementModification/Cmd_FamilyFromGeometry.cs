using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.BAApplication;
using Microsoft.Win32;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_FamilyFromGeometry : IExternalCommand
    {
        private const double MinSolidVolume = 1e-9;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BA", "Settings", "family_from_geometry.json");

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

            List<ElementId> sourceIds = uiDoc.Selection.GetElementIds().ToList();

            if (sourceIds.Count == 0)
            {
                try
                {
                    var refs = uiDoc.Selection.PickObjects(ObjectType.Element,
                        "Select elements to convert into a family geometry (Finish/Escape when done)");
                    sourceIds = refs.Select(r => r.ElementId).Distinct().ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (sourceIds.Count == 0)
            {
                TaskDialog.Show("Family From Geometry", "No elements selected.");
                return Result.Cancelled;
            }

            string templatePath = LoadOrPickTemplatePath();
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                message = "No generic model template selected.";
                return Result.Cancelled;
            }

            var geometryObjects = new List<GeometryObject>();

            foreach (var id in sourceIds)
            {
                var element = doc.GetElement(id);
                if (element == null) continue;

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
                    AppLogger.LogError($"Cmd_FamilyFromGeometry.Geometry [{id.Value}]", ex);
                    continue;
                }

                if (geomElement != null)
                {
                    CollectGeometry(geomElement, geometryObjects);
                }
            }

            if (geometryObjects.Count == 0)
            {
                message = "No usable solid or mesh geometry found in the selected elements.";
                return Result.Failed;
            }

            XYZ translation = ComputeCenteringTranslation(geometryObjects);
            var transform = Transform.CreateTranslation(translation);

            var transformedGeometry = new List<GeometryObject>();
            foreach (var geomObj in geometryObjects)
            {
                switch (geomObj)
                {
                    case Solid solid:
                        var movedSolid = SolidUtils.CreateTransformed(solid, transform);
                        if (movedSolid != null && movedSolid.Volume > MinSolidVolume)
                            transformedGeometry.Add(movedSolid);
                        break;

                    case Mesh mesh:
                        var movedMesh = mesh.get_Transformed(transform);
                        if (movedMesh != null)
                            transformedGeometry.Add(movedMesh);
                        break;
                }
            }

            if (transformedGeometry.Count == 0)
            {
                message = "Geometry transform produced no valid solids or meshes.";
                return Result.Failed;
            }

            Document familyDoc;
            try
            {
                familyDoc = uiApp.Application.NewFamilyDocument(templatePath);
            }
            catch (Exception ex)
            {
                message = $"Failed to create new family document: {ex.Message}";
                AppLogger.LogError("Cmd_FamilyFromGeometry.NewFamilyDocument", ex);
                return Result.Failed;
            }

            int createdCount = 0;

            using (var tx = new Transaction(familyDoc, "BA Create Freeform Geometry"))
            {
                tx.Start();
                try
                {
                    foreach (var geomObj in transformedGeometry)
                    {
                        FreeFormElement.Create(familyDoc, (Solid)geomObj);
                        createdCount++;
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = $"Failed to create freeform geometry in family document: {ex.Message}";
                    AppLogger.LogError("Cmd_FamilyFromGeometry.CreateFreeForm", ex);
                    return Result.Failed;
                }
            }

            string tempFamilyPath;
            try
            {
                tempFamilyPath = SaveFamilyToTempFile(familyDoc);
            }
            catch (Exception ex)
            {
                message = $"Freeform geometry was created but the family document could not be saved to a temporary file: {ex.Message}";
                AppLogger.LogError("Cmd_FamilyFromGeometry.SaveTemp", ex);
                return Result.Failed;
            }

            try
            {
                familyDoc.Close(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_FamilyFromGeometry.CloseFamilyDoc", ex);
                // Not fatal, continue to reopen attempt below.
            }

            try
            {
                uiApp.OpenAndActivateDocument(tempFamilyPath);
            }
            catch (Exception ex)
            {
                message = $"Family was saved but could not be reopened automatically: {ex.Message}\nFile location: {tempFamilyPath}";
                AppLogger.LogError("Cmd_FamilyFromGeometry.ReopenFamily", ex);
                return Result.Failed;
            }

            AppLogger.LogInfo($"Cmd_FamilyFromGeometry: created {createdCount} freeform solid(s)/mesh(es) from {sourceIds.Count} source element(s), reopened from {tempFamilyPath}.");
            TaskDialog.Show("Family From Geometry",
                $"Created {createdCount} freeform shape(s). The family document is now open, use Save As to move it to your library location.\nTemporary file: {tempFamilyPath}");

            return Result.Succeeded;
        }

        private static string SaveFamilyToTempFile(Document familyDoc)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BA_FamilyFromGeometry");
            Directory.CreateDirectory(tempDir);

            string tempPath = Path.Combine(tempDir,
                $"BA_Geometry_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.rfa");

            var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
            familyDoc.SaveAs(tempPath, saveOptions);
            return tempPath;
        }

        private static void CollectGeometry(GeometryElement geomElement, List<GeometryObject> target)
        {
            foreach (var geomObj in geomElement)
            {
                switch (geomObj)
                {
                    case Solid solid when solid.Volume > MinSolidVolume:
                        target.Add(solid);
                        break;

                    case Mesh mesh when mesh.NumTriangles > 0:
                        target.Add(mesh);
                        break;

                    case GeometryInstance instance:
                        var instGeom = instance.GetInstanceGeometry();
                        if (instGeom != null)
                            CollectGeometry(instGeom, target);
                        break;
                }
            }
        }

        private static XYZ ComputeCenteringTranslation(List<GeometryObject> geometryObjects)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool any = false;

            foreach (var geomObj in geometryObjects)
            {
                if (geomObj is not Solid solid || solid.Volume <= MinSolidVolume)
                    continue;

                var box = solid.GetBoundingBox();
                if (box == null) continue;

                foreach (var corner in GetBoxCorners(box))
                {
                    any = true;
                    minX = Math.Min(minX, corner.X);
                    minY = Math.Min(minY, corner.Y);
                    minZ = Math.Min(minZ, corner.Z);
                    maxX = Math.Max(maxX, corner.X);
                    maxY = Math.Max(maxY, corner.Y);
                    maxZ = Math.Max(maxZ, corner.Z);
                }
            }

            if (!any)
                return XYZ.Zero;

            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;

            return new XYZ(-centerX, -centerY, -minZ);
        }

        private static IEnumerable<XYZ> GetBoxCorners(BoundingBoxXYZ box)
        {
            var t = box.Transform;
            var min = box.Min;
            var max = box.Max;

            yield return t.OfPoint(new XYZ(min.X, min.Y, min.Z));
            yield return t.OfPoint(new XYZ(max.X, min.Y, min.Z));
            yield return t.OfPoint(new XYZ(min.X, max.Y, min.Z));
            yield return t.OfPoint(new XYZ(max.X, max.Y, min.Z));
            yield return t.OfPoint(new XYZ(min.X, min.Y, max.Z));
            yield return t.OfPoint(new XYZ(max.X, min.Y, max.Z));
            yield return t.OfPoint(new XYZ(min.X, max.Y, max.Z));
            yield return t.OfPoint(new XYZ(max.X, max.Y, max.Z));
        }

        private static string LoadOrPickTemplatePath()
        {
            string existing = LoadTemplatePath();
            if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
                return existing;

            var dialog = new OpenFileDialog
            {
                Title = "Select the Metric Generic Model family template",
                Filter = "Revit Family Template (*.rft)|*.rft",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
                return null;

            SaveTemplatePath(dialog.FileName);
            return dialog.FileName;
        }

        private static string LoadTemplatePath()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                string json = File.ReadAllText(SettingsPath);
                var data = JsonSerializer.Deserialize<TemplateSettings>(json);
                return data?.TemplatePath;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveTemplatePath(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var data = new TemplateSettings { TemplatePath = path };
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data));
            }
            catch
            {
                // Settings persistence must never block the command.
            }
        }

        private class TemplateSettings
        {
            public string TemplatePath { get; set; }
        }
    }
}