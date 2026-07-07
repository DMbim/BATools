using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA_Tools.ScheduleExporter.Services;
using BA_Tools.ScheduleExporter.ViewModels;
using BA_Tools.ScheduleExporter.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace BA_Tools.ScheduleExporter.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ExportScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                List<ViewSchedule> allSchedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(s => !s.IsTemplate
                             && !s.Definition.IsKeySchedule
                             && !s.Definition.IsMaterialTakeoff)
                    .OrderBy(s => s.Name)
                    .ToList();

                if (allSchedules.Count == 0)
                {
                    TaskDialog.Show("BA Schedule Exporter",
                        "No exportable schedules found in the current document.");
                    return Result.Cancelled;
                }

                ViewSchedule activeSchedule = uiDoc.ActiveView as ViewSchedule;

                var vm = new ExportScheduleViewModel(allSchedules, activeSchedule);
                var window = new ExportScheduleWindow(vm);
                SetRevitAsOwner(window, uiApp);
                window.ShowDialog();

                if (!vm.UserConfirmed)
                    return Result.Cancelled;

                ViewSchedule targetSchedule = vm.GetEffectiveSchedule();
                if (targetSchedule == null)
                {
                    message = "No schedule selected.";
                    return Result.Failed;
                }

                string outputPath = vm.OutputFilePath;

                var reader = new ScheduleReaderService(doc);
                var (fields, rows) = reader.ReadSchedule(targetSchedule);

                var exporter = new ExcelExportService();
                exporter.Export(outputPath, targetSchedule.Name, fields, rows);

                var td = new TaskDialog("BA Schedule Exporter")
                {
                    MainInstruction = "Export complete.",
                    MainContent = $"{rows.Count} row(s) written to:\n{outputPath}",
                    CommonButtons = TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open file",
                    "Open the exported Excel workbook now.");

                if (td.Show() == TaskDialogResult.CommandLink1)
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(outputPath) { UseShellExecute = true });

                return Result.Succeeded;
            }
            catch (NotSupportedException ex)
            {
                TaskDialog.Show("BA Schedule Exporter — Unsupported Schedule", ex.Message);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"BA Schedule Exporter failed: {ex.Message}";
                return Result.Failed;
            }
        }

        private static void SetRevitAsOwner(Window window, UIApplication uiApp)
        {
            var handle = uiApp.MainWindowHandle;
            if (handle == IntPtr.Zero) return;
            new WindowInteropHelper(window).Owner = handle;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ImportScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                string filePath = PromptForFile();
                if (filePath == null)
                    return Result.Cancelled;

                string scheduleName;
                try
                {
                    scheduleName = ExcelImportService.GetScheduleNameFromFile(filePath);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("BA Schedule Import",
                        $"Could not read the Excel file:\n{ex.Message}");
                    return Result.Cancelled;
                }

                List<ViewSchedule> allSchedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(s => !s.IsTemplate
                             && !s.Definition.IsKeySchedule
                             && !s.Definition.IsMaterialTakeoff)
                    .OrderBy(s => s.Name)
                    .ToList();

                ViewSchedule targetSchedule = allSchedules
                    .FirstOrDefault(s => string.Equals(
                        s.Name, scheduleName, StringComparison.OrdinalIgnoreCase));

                if (targetSchedule == null)
                {
                    if (allSchedules.Count == 0)
                    {
                        TaskDialog.Show("BA Schedule Import",
                            "No importable schedules found in the current document.");
                        return Result.Cancelled;
                    }

                    var pickerVm = new SchedulePickerViewModel(scheduleName, allSchedules);
                    var pickerWindow = new SchedulePickerWindow(pickerVm);
                    SetRevitAsOwner(pickerWindow, uiApp);
                    pickerWindow.ShowDialog();

                    if (!pickerVm.UserConfirmed || pickerVm.SelectedSchedule == null)
                        return Result.Cancelled;

                    targetSchedule = pickerVm.SelectedSchedule;
                }

                var reader = new ScheduleReaderService(doc);
                List<Models.ScheduleFieldMeta> fields;

                try
                {
                    var (f, _) = reader.ReadSchedule(targetSchedule);
                    fields = f;
                }
                catch (NotSupportedException ex)
                {
                    TaskDialog.Show("BA Schedule Import — Unsupported Schedule", ex.Message);
                    return Result.Cancelled;
                }

                var importer = new ExcelImportService();
                List<Models.ImportRowData> importRows;

                try
                {
                    var (_, rows) = importer.Import(filePath, fields);
                    importRows = rows;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("BA Schedule Import",
                        $"Failed to parse the Excel file:\n{ex.Message}");
                    return Result.Cancelled;
                }

                if (importRows.Count == 0)
                {
                    TaskDialog.Show("BA Schedule Import",
                        "The Excel file contains no data rows.");
                    return Result.Cancelled;
                }

                var compareService = new ImportCompareService(doc);
                var compareResult = compareService.Compare(fields, importRows);

                var previewVm = new ImportPreviewViewModel(compareResult);
                var previewWindow = new ImportPreviewWindow(previewVm);
                SetRevitAsOwner(previewWindow, uiApp);
                previewWindow.ShowDialog();

                if (!previewVm.UserConfirmed)
                    return Result.Cancelled;

                Models.WriteResult writeResult;
                try
                {
                    var writer = new ParameterWriteService(doc);
                    writeResult = writer.WriteAll(fields, compareResult);
                }
                catch (InvalidOperationException ex)
                {
                    TaskDialog.Show("BA Schedule Import — Transaction Failed", ex.Message);
                    return Result.Failed;
                }

                var resultVm = new ImportResultViewModel(writeResult);
                var resultWindow = new ImportResultWindow(resultVm);
                SetRevitAsOwner(resultWindow, uiApp);
                resultWindow.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"BA Schedule Import failed unexpectedly: {ex.Message}";
                return Result.Failed;
            }
        }

        private static string PromptForFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select BA Schedule Export File",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                Multiselect = false
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static void SetRevitAsOwner(Window window, UIApplication uiApp)
        {
            var handle = uiApp.MainWindowHandle;
            if (handle == IntPtr.Zero) return;
            new WindowInteropHelper(window).Owner = handle;
        }
    }
}
