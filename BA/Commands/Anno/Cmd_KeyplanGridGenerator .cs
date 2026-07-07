using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
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

                KeyplanLevelPickerWindow levelPicker = new KeyplanLevelPickerWindow(doc)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                bool? pickerResult = levelPicker.ShowDialog();

                if (pickerResult != true || levelPicker.SelectedOption == null)
                    return Result.Cancelled;

                KeyplanLevelOption selected = levelPicker.SelectedOption;

                CurveLoop outerLoop = selected.OuterLoop;
                if (outerLoop == null)
                {
                    // Should not happen — picker only allows IsReady options — but guard anyway.
                    TaskDialog.Show("Keyplan Grid", "Selected level has no resolvable area boundary.");
                    return Result.Cancelled;
                }

                KeyplanGridViewModel vm = KeyplanGridViewModel.CreateDefault();
                vm.SourceViewName = selected.SourceView?.Name ?? string.Empty;
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
                Autodesk.Revit.UI.TaskDialog.Show("Keyplan Grid - Error", ex.ToString());
                return Result.Failed;
            }
        }
    }
}