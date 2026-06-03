using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using BA_Tools.ScheduleExporter.Services;
using BA_Tools.ScheduleExporter.ViewModels;
using BA_Tools.ScheduleExporter.Views;

namespace BA_Tools.ScheduleExporter.Commands
{
    /// <summary>
    /// Ribbon command: Import parameter values from a BA-exported Excel file back into Revit.
    ///
    /// TRANSACTION MODE:
    ///   Manual — the transaction is opened and committed inside ParameterWriteService.WriteAll().
    ///   This command itself does not open a transaction. Do not change this to ReadWrite or
    ///   Automatic; doing so would wrap the entire Execute() in a transaction that would be
    ///   uncommitted when the WPF dialogs are open, which is unsafe.
    ///
    /// FLOW:
    ///   1. OpenFileDialog — user picks the .xlsx file.
    ///   2. Read the schedule name from the sheet name.
    ///   3. Try to find a matching ViewSchedule by name (exact, case-insensitive).
    ///   4. If no match: show SchedulePickerWindow — user selects a schedule manually.
    ///   5. If picker is cancelled: abort.
    ///   6. Read current schedule data (fields only — rows needed for field metadata).
    ///   7. Parse Excel import rows.
    ///   8. Compare imported data against live document.
    ///   9. Show ImportPreviewWindow — user reviews and confirms.
    ///  10. If confirmed: write parameters via ParameterWriteService.
    ///  11. Show ImportResultWindow.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ImportScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document   doc   = uiDoc.Document;

            try
            {
                // ── Step 1: File picker ───────────────────────────────────────────
                string filePath = PromptForFile();
                if (filePath == null)
                    return Result.Cancelled;

                // ── Step 2: Read schedule name from Excel ─────────────────────────
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

                // ── Step 3: Collect all schedules, attempt name match ─────────────
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

                // ── Step 4: If no match, let user pick ────────────────────────────
                if (targetSchedule == null)
                {
                    if (allSchedules.Count == 0)
                    {
                        TaskDialog.Show("BA Schedule Import",
                            "No importable schedules found in the current document.");
                        return Result.Cancelled;
                    }

                    var pickerVm     = new SchedulePickerViewModel(scheduleName, allSchedules);
                    var pickerWindow = new SchedulePickerWindow(pickerVm);
                    SetRevitAsOwner(pickerWindow);
                    pickerWindow.ShowDialog();

                    if (!pickerVm.UserConfirmed || pickerVm.SelectedSchedule == null)
                        return Result.Cancelled;

                    targetSchedule = pickerVm.SelectedSchedule;
                }

                // ── Step 5: Read current schedule field definitions ───────────────
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

                // ── Step 6: Parse Excel import rows ───────────────────────────────
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

                // ── Step 7: Compare against live document ─────────────────────────
                var compareService = new ImportCompareService(doc);
                var compareResult  = compareService.Compare(fields, importRows);

                // ── Step 8: Preview dialog ────────────────────────────────────────
                var previewVm     = new ImportPreviewViewModel(compareResult);
                var previewWindow = new ImportPreviewWindow(previewVm);
                SetRevitAsOwner(previewWindow);
                previewWindow.ShowDialog();

                if (!previewVm.UserConfirmed)
                    return Result.Cancelled;

                // ── Step 9: Write parameters ──────────────────────────────────────
                Models.WriteResult writeResult;
                try
                {
                    var writer = new ParameterWriteService(doc);
                    writeResult = writer.WriteAll(fields, compareResult);
                }
                catch (InvalidOperationException ex)
                {
                    // Transaction was rolled back — surface the error clearly
                    TaskDialog.Show("BA Schedule Import — Transaction Failed", ex.Message);
                    return Result.Failed;
                }

                // ── Step 10: Result dialog ────────────────────────────────────────
                var resultVm     = new ImportResultViewModel(writeResult);
                var resultWindow = new ImportResultWindow(resultVm);
                SetRevitAsOwner(resultWindow);
                resultWindow.ShowDialog();

                return writeResult.FailureCount == 0
                    ? Result.Succeeded
                    : Result.Succeeded; // Partial failures are still Result.Succeeded;
                                        // errors were shown in the result dialog.
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
                Title       = "Select BA Schedule Export File",
                Filter      = "Excel Workbook (*.xlsx)|*.xlsx",
                Multiselect = false
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static void SetRevitAsOwner(Window window)
        {
            IntPtr revitHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (revitHandle == IntPtr.Zero) return;
            new WindowInteropHelper(window).Owner = revitHandle;
        }
    }
}
