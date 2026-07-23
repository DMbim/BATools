using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Exports one ViewSheet to a single PDF with a fully custom filename.
    /// Uses Combine = true with a single-sheet view list, which is the only
    /// way to get exact filename control per sheet, Revit's batch NamingRule
    /// mechanism only supports parameter-value tokens, not custom date
    /// formatting or literal templates. Must be called from a valid Revit
    /// API thread context (IExternalCommand.Execute or
    /// IExternalEventHandler.Execute), never directly from WPF UI code.
    /// </summary>
    public static class PdfExportService
    {
        public static SheetExportOutcome ExportSheet(Document doc, ViewSheet sheet, string folderPath, string fileNameWithoutExtension)
        {
            var outcome = new SheetExportOutcome
            {
                SheetNumber = sheet.SheetNumber,
                FolderPath = folderPath,
                FileName = fileNameWithoutExtension + ".pdf"
            };

            try
            {
                if (!sheet.CanBePrinted)
                {
                    outcome.Success = false;
                    outcome.ErrorMessage = "Sheet is not printable (CanBePrinted is false).";
                    return outcome;
                }

                Directory.CreateDirectory(folderPath);

                var options = new PDFExportOptions
                {
                    Combine = true,
                    FileName = fileNameWithoutExtension
                };

                var viewIds = new List<ElementId> { sheet.Id };

                var success = doc.Export(folderPath, viewIds, options);

                outcome.Success = success;

                if (!success)
                {
                    outcome.ErrorMessage = "Document.Export returned false.";
                }
            }
            catch (Exception ex)
            {
                outcome.Success = false;
                outcome.ErrorMessage = ex.Message;
                AppLogger.LogError($"PDF export failed for sheet {sheet.SheetNumber}", ex);
            }

            return outcome;
        }
    }
}
