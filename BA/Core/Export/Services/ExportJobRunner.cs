using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Orchestrates one export job end to end: resolves the saved sheet set,
    /// resolves naming and folder templates per sheet, dispatches to
    /// PdfExportService or DwgExportService, and aggregates a per-sheet
    /// result list. One sheet failing does not abort the rest of the job.
    /// Must be called from a valid Revit API thread context, this performs
    /// no transactions itself (export calls are not transactional), so it is
    /// safe to call from either a manual command or the idling scheduler.
    /// </summary>
    public static class ExportJobRunner
    {
        public static ExportJobResult RunJob(Document doc, ExportJobSettings jobSettings, DateTime exportDate)
        {
            var result = new ExportJobResult
            {
                JobId = jobSettings.JobId,
                JobName = jobSettings.JobName,
                Format = jobSettings.Format,
                RunTimestamp = exportDate
            };

            List<ViewSheet> sheets;

            try
            {
                sheets = ResolveSheetsFromSet(doc, jobSettings.SheetSetName);
            }
            catch (Exception ex)
            {
                result.JobLevelError = ex.Message;
                AppLogger.LogError($"ExportJobRunner failed to resolve sheet set '{jobSettings.SheetSetName}' for job '{jobSettings.JobName}'", ex);
                return result;
            }

            if (sheets.Count == 0)
            {
                result.JobLevelError = $"Sheet set '{jobSettings.SheetSetName}' contains no sheets (or only non-sheet views).";
                return result;
            }

            if (string.IsNullOrWhiteSpace(jobSettings.OutputFolderTemplate))
            {
                result.JobLevelError = "Job has no OutputFolderTemplate configured.";
                return result;
            }

            // Read once per job run, not per sheet, avoids one JSON file read
            // per sheet for large sheet sets. Empty is valid here, it only
            // becomes an error if the template actually references {Revision}.
            var revisionParamName = NamingTemplateEngine.LoadCurrentRevisionParamName();

            foreach (var sheet in sheets)
            {
                result.Outcomes.Add(ExportOneSheet(doc, sheet, jobSettings, exportDate, revisionParamName));
            }

            AppLogger.LogInfo($"Export job '{jobSettings.JobName}' finished: {result.SuccessCount} succeeded, {result.FailureCount} failed.");

            return result;
        }

        private static SheetExportOutcome ExportOneSheet(Document doc, ViewSheet sheet, ExportJobSettings jobSettings, DateTime exportDate, string revisionParamName)
        {
            try
            {
                var fileName = NamingTemplateEngine.ResolveFileName(jobSettings.NamingTemplate, sheet, jobSettings, exportDate, revisionParamName);
                var folder = NamingTemplateEngine.ResolveFolder(jobSettings.OutputFolderTemplate, sheet, jobSettings, exportDate, revisionParamName);

                fileName = StripKnownExtension(fileName, jobSettings.Format);

                return jobSettings.Format == ExportFormat.Pdf
                    ? PdfExportService.ExportSheet(doc, sheet, folder, fileName)
                    : DwgExportService.ExportSheet(doc, sheet, folder, fileName, jobSettings.ExportSetupName);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Export job '{jobSettings.JobName}' failed to resolve naming for sheet {sheet.SheetNumber}", ex);

                return new SheetExportOutcome
                {
                    SheetNumber = sheet.SheetNumber,
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

        private static List<ViewSheet> ResolveSheetsFromSet(Document doc, string sheetSetName)
        {
            if (string.IsNullOrWhiteSpace(sheetSetName))
            {
                throw new InvalidOperationException("Job has no SheetSetName configured.");
            }

            var allSets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .ToList();

            var viewSheetSet = allSets.FirstOrDefault(vss => string.Equals(vss.Name, sheetSetName, StringComparison.OrdinalIgnoreCase));

            if (viewSheetSet == null)
            {
                var available = allSets.Select(vss => vss.Name).ToList();

                throw new InvalidOperationException(
                    $"No saved Sheet Set named '{sheetSetName}' was found. " +
                    $"Available: {(available.Count == 0 ? "(none)" : string.Join(", ", available))}");
            }

            var sheets = new List<ViewSheet>();

            foreach (View view in viewSheetSet.Views)
            {
                if (view is ViewSheet sheet)
                {
                    sheets.Add(sheet);
                }
            }

            return sheets;
        }
    }
}
