using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Windows;
using BA.UI.KeyplanGrid;

namespace BA.KeyplanGrid
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class Cmd_KeyplanGridGenerator : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;
                Document doc = uiDoc.Document;

                View sourceView = KeyplanViewService.FindViewByName(doc, "X.NP_Keyplan") ?? doc.ActiveView;
                if (sourceView == null)
                {
                    TaskDialog.Show("Keyplan Grid", "No source view was found.");
                    return Result.Cancelled;
                }

                CurveLoop outerLoop = KeyplanAreaSourceService.GetLargestOuterLoopFromView(doc, sourceView);
                if (outerLoop == null)
                {
                    TaskDialog.Show(
                        "Keyplan Grid",
                        "No valid rentable area boundary loop was found.\n\n" +
                        "Open the rentable area plan and make sure at least one real Area element exists.");
                    return Result.Cancelled;
                }

                KeyplanGridViewModel vm = KeyplanGridViewModel.CreateDefault();
                vm.SourceViewName = sourceView.Name;
                vm.LoadInitialPreview(outerLoop);

                KeyplanGridWindow window = new KeyplanGridWindow(doc, vm)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                window.ShowDialog();

                // Return Succeeded if the user generated anything so that
                // Revit keeps the committed filled-region transactions.
                // Result.Cancelled would cause Revit to roll them back.
                return vm.LastGenerationResult != null
                    ? Result.Succeeded
                    : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("Keyplan Grid - Error", ex.ToString());
                return Result.Failed;
            }
        }
    }
}