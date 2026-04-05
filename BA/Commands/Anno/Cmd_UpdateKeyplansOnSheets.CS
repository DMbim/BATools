using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace BA.Keyplan
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class Cmd_UpdateKeyplansOnSheets : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                KeyplanSheetUpdateOptions options = KeyplanSheetUpdateOptions.CreateDefault();

                // IMPORTANT:S:\CAD\Autodesk Revit\BA_Templates\BA_Templates_v26\WIP
                // point this at your real office shared parameter file
                options.SharedParameterFilePath = @"S:\CAD\Autodesk Revit\BA_Templates\BA_Templates_v26\WIP\BA_SharedParametersWIP2.txt";

                KeyplanSheetUpdateResult result = KeyplanSheetUpdateService.UpdateSheets(uiApp, doc, options);

                TaskDialog.Show(
                    "Keyplan",
                    "Keyplan update finished.\n\n" +
                    $"Sheets scanned: {result.TotalSheets}\n" +
                    $"Sheets with zone value: {result.SheetsWithZone}\n" +
                    $"Views created: {result.CreatedViews}\n" +
                    $"Views updated: {result.UpdatedViews}\n" +
                    $"Viewports placed: {result.PlacedViewports}\n" +
                    $"Viewports moved: {result.MovedViewports}\n" +
                    $"Sheets skipped: {result.SkippedSheets}\n" +
                    $"Errors: {result.Errors}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("Keyplan - Error", ex.ToString());
                return Result.Failed;
            }
        }
    }
}