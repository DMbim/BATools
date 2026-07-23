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

                KeyplanLevelPickerWindow levelPicker = new KeyplanLevelPickerWindow(uiDoc)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                bool? pickerResult = levelPicker.ShowDialog();

                if (pickerResult != true || levelPicker.SelectedOption == null)
                {
                    // IMPORTANT: return Succeeded, not Cancelled. The user may have
                    // created views, boundary lines, or Area elements while the
                    // picker was open (setup workflow). Result.Cancelled would make
                    // Revit roll back ALL transactions committed during this
                    // command, silently deleting that work.
                    return Result.Succeeded;
                }

                KeyplanLevelOption selected = levelPicker.SelectedOption;

                CurveLoop outerLoop = selected.OuterLoop;
                if (outerLoop == null)
                {
                    TaskDialog.Show("Keyplan Grid", "Selected level has no resolvable area boundary.");
                    return Result.Succeeded;
                }

                KeyplanGridViewModel vm = KeyplanGridViewModel.CreateDefault();
                vm.SourceViewName = selected.SourceView?.Name ?? string.Empty;
                vm.LoadInitialPreview(doc, outerLoop, selected.Level);

                KeyplanGridWindow window = new KeyplanGridWindow(doc, vm)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                window.ShowDialog();

                return Result.Succeeded;
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