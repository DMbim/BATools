using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using bimBA.Core.Views.Palettes;

namespace bimBA.Cmds.Views
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_ApplyColorPalette : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;

            if (doc == null)
            {
                TaskDialog.Show("bimBA – Color Palettes", "No active document.");
                return Result.Failed;
            }

            try
            {
                // 1) Pick template
                var templates = ColorPaletteManager.GetAllViewTemplates(doc)
                    .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (templates.Count == 0)
                {
                    TaskDialog.Show("bimBA – Color Palettes", "No view templates found in this project.");
                    return Result.Cancelled;
                }

                var pickedTemplate = ColorPaletteManager.PickViewTemplateWithTaskDialog(templates);
                if (pickedTemplate == null)
                    return Result.Cancelled;

                // 2) Pick palette
                var palettes = ColorPaletteManager.GetPredefinedPalettes();
                if (palettes.Count == 0)
                {
                    TaskDialog.Show("bimBA – Color Palettes", "No palettes defined.");
                    return Result.Cancelled;
                }

                var pickedPalette = ColorPaletteManager.PickPaletteWithTaskDialog(palettes);
                if (pickedPalette == null)
                    return Result.Cancelled;

                // 3) Apply
                var report = ColorPaletteManager.ApplyPaletteToViewTemplate(doc, pickedTemplate.Id, pickedPalette);

                TaskDialog.Show("bimBA – Color Palettes",
                    $"Template: {pickedTemplate.Name}\n" +
                    $"Palette: {pickedPalette.Name}\n\n" +
                    $"Filters updated: {report.Updated}\n" +
                    $"Filters not found: {report.Missing}\n" +
                    $"Skipped (read-only/invalid): {report.Skipped}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("bimBA – Color Palettes (Error)", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
