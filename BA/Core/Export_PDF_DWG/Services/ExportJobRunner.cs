using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Orchestrates one export job end to end. Branches early on
    /// SourceMode: Sheets resolves ViewSheets by sheet number and uses the
    /// sheet-mode naming tokens ({SheetNumber}/{SheetName}/{Revision}),
    /// Views resolves arbitrary Views by UniqueId and uses the view-mode
    /// tokens ({ViewName}/{ViewType}, no {Revision}). Within either mode,
    /// UseActiveViewOrSheet overrides the configured selection entirely
    /// and resolves against whatever is active in Revit right now instead,
    /// matching exactly what ExportUiBridge.HandleActiveViewOrSheetPreview
    /// already validates for preview, a mismatch (active view is a sheet
    /// in Views mode, or not a sheet in Sheets mode) fails the job with
    /// the same message preview would show rather than silently falling
    /// back to the configured selection. Sheets mode also bumps date/
    /// revision once, before any format runs, when the job has
    /// BumpDateRevisionOnRun set, via DateRevisionBumpService. Both modes
    /// then run every format enabled on the job (PDF, DWG, or both)
    /// against the resolved set, dispatching to PdfExportService.ExportView
    /// or DwgExportService.ExportView, which both accept any View, a
    /// ViewSheet included. One item failing does not abort the rest of
    /// that format's run, and one format failing does not stop the other
    /// from running. A configured sheet number or view UniqueId that no
    /// longer resolves is skipped and reported rather than failing the
    /// job. Must be called from a valid Revit API thread context; the
    /// export calls themselves are not transactional, but the date/
    /// revision bump opens and commits its own Transaction, so this is
    /// safe to call from either a manual command or the idling scheduler,
    /// both of which already run on that thread.
    /// </summary>
    public static class ExportJobRunner
    {
        /// <returns>
        /// One ExportJobResult per format enabled on the job. A job with
        /// both ExportPdf and ExportDwg set returns two results. A job
        /// level failure that happens before any format specific work
        /// starts (no sheets/views, no output folder, active view/sheet
        /// mismatch) is still reported once per enabled format, so
        /// callers can treat the return value uniformly without special
        /// casing the early failure path.
        /// </returns>
        public static List<ExportJobResult> RunJob(Document doc, ExportJobSettings jobSettings, DateTime exportDate, View? activeView)
        {
            var enabledFormats = GetEnabledFormats(jobSettings);

            if (enabledFormats.Count == 0)
            {
                return new List<ExportJobResult>
                {
                    NewResult(jobSettings, ExportFormat.Pdf, exportDate, "No export format is enabled on this job. Check PDF and/or DWG.")
                };
            }

            if (string.IsNullOrWhiteSpace(jobSettings.OutputFolderTemplate))
            {
                return enabledFormats.Select(f => NewResult(jobSettings, f, exportDate, "Job has no OutputFolderTemplate configured.")).ToList();
            }

            return jobSettings.SourceMode == ExportSourceMode.Views
                ? RunViewsMode(doc, jobSettings, exportDate, enabledFormats, activeView)
                : RunSheetsMode(doc, jobSettings, exportDate, enabledFormats, activeView);
        }

        private static List<ExportJobResult> RunSheetsMode(Document doc, ExportJobSettings jobSettings, DateTime exportDate, List<ExportFormat> enabledFormats, View? activeView)
        {
            List<ViewSheet> sheets;

            if (jobSettings.UseActiveViewOrSheet)
            {
                if (!(activeView is ViewSheet activeSheet))
                {
                    var mismatchMessage = activeView == null
                        ? "No active view/sheet is available to export against."
                        : "The active view is not a sheet. Switch to a sheet, or turn off 'Current View/Sheet Only' for this job.";

                    return enabledFormats.Select(f => NewResult(jobSettings, f, exportDate, mismatchMessage)).ToList();
                }

                sheets = new List<ViewSheet> { activeSheet };
            }
            else
            {
                List<string> missingSheetNumbers;

                try
                {
                    sheets = ResolveSelectedSheets(doc, jobSettings, out missingSheetNumbers);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"ExportJobRunner failed to resolve sheets for job '{jobSettings.JobName}'", ex);
                    return enabledFormats.Select(f => NewResult(jobSettings, f, exportDate, ex.Message)).ToList();
                }

                if (missingSheetNumbers.Count > 0)
                {
                    AppLogger.LogInfo($"Export job '{jobSettings.JobName}': {missingSheetNumbers.Count} configured sheet(s) no longer exist and were skipped: {string.Join(", ", missingSheetNumbers)}");
                }

                if (sheets.Count == 0)
                {
                    var message = missingSheetNumbers.Count > 0
                        ? $"None of the {missingSheetNumbers.Count} configured sheet(s) exist anymore in this document. Use 'Pick Sheets...' to reselect."
                        : "Job has no sheets selected. Use 'Pick Sheets...' to choose sheets first.";

                    return enabledFormats.Select(f => NewResult(jobSettings, f, exportDate, message)).ToList();
                }
            }

            // Bumped once per job run, before any format is exported,
            // regardless of how many formats are enabled, and regardless
            // of whether the sheet set came from UseActiveViewOrSheet or
            // the configured selection. A bump failure is logged and
            // swallowed here, it does not block export, matching the
            // "one thing failing doesn't abort the rest" approach used
            // everywhere else in this class.
            DateRevisionBumpOutcome? bumpOutcome = null;

            try
            {
                bumpOutcome = DateRevisionBumpService.BumpIfNeeded(doc, sheets, jobSettings);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Export job '{jobSettings.JobName}': date/revision bump failed", ex);
            }

            // Read once per job run, not per sheet or per format, avoids
            // repeated JSON file reads for large sheet sets. Empty is
            // valid here, it only becomes an error if the template
            // actually references {Revision}. Read after the bump above,
            // so if BumpDateRevisionOnRun just incremented the revision,
            // the {Revision} token in this run's file names reflects the
            // new value, not the pre-bump one, the Transaction commit
            // inside DateRevisionBumpService already regenerated the
            // document by the time this executes.
            var revisionParamName = NamingTemplateEngine.LoadCurrentRevisionParamName();

            var results = new List<ExportJobResult>();

            foreach (var format in enabledFormats)
            {
                var result = NewResult(jobSettings, format, exportDate, null);

                foreach (var sheet in sheets)
                {
                    result.Outcomes.Add(ExportOneSheet(doc, sheet, jobSettings, format, exportDate, revisionParamName));
                }

                AppLogger.LogInfo($"Export job '{jobSettings.JobName}' ({format}) finished: {result.SuccessCount} succeeded, {result.FailureCount} failed.");
                results.Add(result);
            }

            if (bumpOutcome != null && results.Count > 0)
            {
                results[0].DateRevisionBumpSummary = bumpOutcome.ToSummaryText();
            }

            return results;
        }

        private static List<ExportJobResult> RunViewsMode(Document doc, ExportJobSettings jobSettings, DateTime exportDate, List<ExportFormat> enabledFormats, View? activeView)
        {
            List<View> views;

            if (jobSettings.UseActiveViewOrSheet)
            {
                if (activeView == null || activeView.ViewType == ViewType.DrawingSheet || activeView.IsTemplate)
                {
                    var mismatchMessage = activeView == null
                        ? "No active view is available to export against."
                        : "The active view is a sheet or a view template, neither can be exported in Views mode.";

                    return enabledFormats.Select(f => NewResult(jobSettings, f, exportDate, mismatchMessage)).ToList();
                }

                views = new List<View> { activeView };
            }
            else
            {
                List<string> missingViewIds;

                try
                {
                    views = ResolveSelectedViews(doc, jobSettings, out missingViewIds);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"ExportJobRunner failed to resolve views for job '{jobSettings.JobName}'", ex);
                    return enabledFormats.Select(f => NewResult(jobSettings, f, exportDate, ex.Message)).ToList();
                }

                if (missingViewIds.Count > 0)
                {
                    AppLogger.LogInfo($"Export job '{jobSettings.JobName}': {missingViewIds.Count} configured view(s) no longer exist and were skipped.");
                }

                if (views.Count == 0)
                {
                    var message = missingViewIds.Count > 0
                        ? $"None of the {missingViewIds.Count} configured view(s) exist anymore in this document. Use 'Pick Views...' to reselect."
                        : "Job has no views selected. Use 'Pick Views...' to choose views first.";

                    return enabledFormats.Select(f => NewResult(jobSettings, f, exportDate, message)).ToList();
                }
            }

            var results = new List<ExportJobResult>();

            foreach (var format in enabledFormats)
            {
                var result = NewResult(jobSettings, format, exportDate, null);

                foreach (var view in views)
                {
                    result.Outcomes.Add(ExportOneView(doc, view, jobSettings, format, exportDate));
                }

                AppLogger.LogInfo($"Export job '{jobSettings.JobName}' ({format}) finished: {result.SuccessCount} succeeded, {result.FailureCount} failed.");
                results.Add(result);
            }

            return results;
        }

        private static List<ExportFormat> GetEnabledFormats(ExportJobSettings jobSettings)
        {
            var formats = new List<ExportFormat>();

            if (jobSettings.ExportPdf)
            {
                formats.Add(ExportFormat.Pdf);
            }

            if (jobSettings.ExportDwg)
            {
                formats.Add(ExportFormat.Dwg);
            }

            return formats;
        }

        private static ExportJobResult NewResult(ExportJobSettings jobSettings, ExportFormat format, DateTime exportDate, string? jobLevelError)
        {
            return new ExportJobResult
            {
                JobId = jobSettings.JobId,
                JobName = jobSettings.JobName,
                Format = format,
                RunTimestamp = exportDate,
                JobLevelError = jobLevelError ?? string.Empty
            };
        }

        private static SheetExportOutcome ExportOneSheet(
            Document doc,
            ViewSheet sheet,
            ExportJobSettings jobSettings,
            ExportFormat format,
            DateTime exportDate,
            string revisionParamName)
        {
            try
            {
                var fileName = NamingTemplateEngine.ResolveFileName(jobSettings.NamingTemplate, sheet, jobSettings, exportDate, revisionParamName);
                var folder = NamingTemplateEngine.ResolveFolder(jobSettings.OutputFolderTemplate, sheet, jobSettings, exportDate, revisionParamName);

                fileName = StripKnownExtension(fileName, format);

                return format == ExportFormat.Pdf
                    ? PdfExportService.ExportView(doc, sheet, folder, fileName, jobSettings.PdfSettings)
                    : DwgExportService.ExportView(doc, sheet, folder, fileName, jobSettings.DwgSettings);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Export job '{jobSettings.JobName}' ({format}) failed to resolve naming for sheet {sheet.SheetNumber}", ex);

                return new SheetExportOutcome
                {
                    SheetNumber = sheet.SheetNumber,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static SheetExportOutcome ExportOneView(
            Document doc,
            View view,
            ExportJobSettings jobSettings,
            ExportFormat format,
            DateTime exportDate)
        {
            try
            {
                var fileName = NamingTemplateEngine.ResolveFileNameForView(jobSettings.NamingTemplate, view, jobSettings, exportDate);
                var folder = NamingTemplateEngine.ResolveFolderForView(jobSettings.OutputFolderTemplate, view, jobSettings, exportDate);

                fileName = StripKnownExtension(fileName, format);

                return format == ExportFormat.Pdf
                    ? PdfExportService.ExportView(doc, view, folder, fileName, jobSettings.PdfSettings)
                    : DwgExportService.ExportView(doc, view, folder, fileName, jobSettings.DwgSettings);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Export job '{jobSettings.JobName}' ({format}) failed to resolve naming for view {view.Name}", ex);

                return new SheetExportOutcome
                {
                    SheetNumber = view.Name,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static string StripKnownExtension(string fileName, ExportFormat format)
        {
            var extension = format == ExportFormat.Pdf ? ".pdf" : ".dwg";

            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - extension.Length)
                : fileName;
        }

        private static List<ViewSheet> ResolveSelectedSheets(Document doc, ExportJobSettings jobSettings, out List<string> missingSheetNumbers)
        {
            missingSheetNumbers = new List<string>();

            if (jobSettings.SelectedSheetNumbers == null || jobSettings.SelectedSheetNumbers.Count == 0)
            {
                return new List<ViewSheet>();
            }

            var allSheetsByNumber = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .OfType<ViewSheet>()
                .GroupBy(s => s.SheetNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var resolved = new List<ViewSheet>();

            foreach (var sheetNumber in jobSettings.SelectedSheetNumbers)
            {
                if (allSheetsByNumber.TryGetValue(sheetNumber, out var sheet))
                {
                    resolved.Add(sheet);
                }
                else
                {
                    missingSheetNumbers.Add(sheetNumber);
                }
            }

            return resolved;
        }

        private static List<View> ResolveSelectedViews(Document doc, ExportJobSettings jobSettings, out List<string> missingViewIds)
        {
            missingViewIds = new List<string>();
            var resolved = new List<View>();

            if (jobSettings.SelectedViewUniqueIds == null || jobSettings.SelectedViewUniqueIds.Count == 0)
            {
                return resolved;
            }

            foreach (var uniqueId in jobSettings.SelectedViewUniqueIds)
            {
                if (doc.GetElement(uniqueId) is View view && !view.IsTemplate)
                {
                    resolved.Add(view);
                }
                else
                {
                    missingViewIds.Add(uniqueId);
                }
            }

            return resolved;
        }
    }
}