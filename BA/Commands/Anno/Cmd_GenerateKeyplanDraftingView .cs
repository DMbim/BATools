using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace BA.Keyplan
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class Cmd_GenerateKeyplanDraftingView : IExternalCommand
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
                KeyplanGenerationOptions options = KeyplanGenerationOptions.CreateDefault();

                KeyplanGenerationResult result = KeyplanDraftingViewService.Generate(doc, options);

                if (result == null)
                {
                    message = "Keyplan generation returned no result.";
                    return Result.Failed;
                }

                if (result.GeneratedViewId != ElementId.InvalidElementId)
                {
                    View generatedView = doc.GetElement(result.GeneratedViewId) as View;
                    if (generatedView != null)
                    {
                        uiDoc.ActiveView = generatedView;
                    }
                }

                TaskDialog.Show(
                    "Keyplan",
                    "Keyplan drafting view generated successfully.\n\n" +
                    $"Source view: {result.SourceViewName}\n" +
                    $"Target view: {result.TargetViewName}\n" +
                    $"Deleted existing target elements: {result.DeletedElementCount}\n" +
                    $"Copied view-specific elements: {result.CopiedViewSpecificCount}\n" +
                    $"Recreated model/area curves as detail curves: {result.RecreatedCurveCount}\n" +
                    $"Skipped unsupported curves: {result.SkippedCurveCount}\n" +
                    $"Applied template: {(result.AppliedTemplateName ?? "<none>")}");

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
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