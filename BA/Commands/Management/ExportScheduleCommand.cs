using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA_Tools.ScheduleExporter.Services;
using BA_Tools.ScheduleExporter.ViewModels;
using BA_Tools.ScheduleExporter.Views;

namespace BA_Tools.ScheduleExporter.Commands
{
    /// <summary>
    /// Ribbon command: Export a Revit schedule to a colour-coded Excel workbook.
    ///
    /// TRANSACTION MODE:
    ///   ReadOnly — this command never writes to the document.
    ///   All Revit API access (schedule collection, element reads) happens inside Execute().
    ///   The WPF dialog is shown after data is collected; no API calls cross the UI boundary.
    ///
    /// FLOW:
    ///   1. Collect all ViewSchedules in the document.
    ///   2. Determine the active schedule (if the active view is a ViewSchedule).
    ///   3. Open ExportScheduleWindow for user to pick source and output path.
    ///   4. If confirmed: read schedule data, write Excel file.
    ///   5. Show success/failure TaskDialog.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ExportScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document   doc   = uiDoc.Document;

            try
            {
                // Collect all non-template, non-key, non-material-takeoff schedules
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

                // Detect active schedule view
                ViewSchedule activeSchedule = uiDoc.ActiveView as ViewSchedule;

                var vm     = new ExportScheduleViewModel(allSchedules, activeSchedule);
                var window = new ExportScheduleWindow(vm);
                SetRevitAsOwner(window);
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

                // Read schedule data inside the active transaction context (ReadOnly)
                var reader = new ScheduleReaderService(doc);
                var (fields, rows) = reader.ReadSchedule(targetSchedule);

                // Write Excel file — no Revit API involved from this point
                var exporter = new ExcelExportService();
                exporter.Export(outputPath, targetSchedule.Name, fields, rows);

                var td = new TaskDialog("BA Schedule Exporter")
                {
                    MainInstruction = "Export complete.",
                    MainContent = $"{rows.Count} row(s) written to:\n{outputPath}",
                    CommonButtons = TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Open file",
                    "Open the exported Excel workbook now.");

                TaskDialogResult tdResult = td.Show();
                if (tdResult == TaskDialogResult.CommandLink1)
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

        private static void SetRevitAsOwner(Window window)
        {
            IntPtr revitHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (revitHandle == IntPtr.Zero) return;
            new WindowInteropHelper(window).Owner = revitHandle;
        }
    }
}
