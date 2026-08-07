using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Exports one View (a ViewSheet in Sheets mode, or an arbitrary View
    /// in Views mode, ViewSheet derives from View so one method covers
    /// both) to a single PDF with a fully custom filename. Uses
    /// Combine = true with a single-view list, which is the only way to
    /// get exact filename control per item, Revit's batch NamingRule
    /// mechanism only supports parameter-value tokens, not custom date
    /// formatting or literal templates. Settings are applied directly to a
    /// fresh PDFExportOptions instance, no predefined setup involved.
    /// Must be called from a valid Revit API thread context
    /// (IExternalCommand.Execute or IExternalEventHandler.Execute), never
    /// directly from WPF UI code.
    /// </summary>
    public static class PdfExportService
    {
        /// <summary>
        /// SheetExportOutcome.SheetNumber holds view.Name when exporting a
        /// non-sheet view, not a real sheet number, reused rather than
        /// adding a second, near-identical outcome model. Every consumer
        /// of this outcome just displays that field, a view's name in that
        /// slot reads sensibly without any downstream change needed.
        /// </summary>
        public static SheetExportOutcome ExportView(
            Document doc,
            View view,
            string folderPath,
            string fileNameWithoutExtension,
            PdfSettings settings)
        {
            var outcome = new SheetExportOutcome
            {
                SheetNumber = view is ViewSheet sheet ? sheet.SheetNumber : view.Name,
                FolderPath = folderPath,
                FileName = fileNameWithoutExtension + ".pdf"
            };

            try
            {
                if (!view.CanBePrinted)
                {
                    outcome.Success = false;
                    outcome.ErrorMessage = "View is not printable (CanBePrinted is false).";
                    return outcome;
                }

                Directory.CreateDirectory(folderPath);

                settings = settings ?? new PdfSettings();

                var options = new PDFExportOptions
                {
                    Combine = true,
                    FileName = fileNameWithoutExtension,
                    ColorDepth = settings.ColorDepth,
                    ExportQuality = settings.ExportQuality,
                    ZoomType = settings.ZoomType,
                    AlwaysUseRaster = settings.AlwaysUseRaster,
                    HideCropBoundaries = settings.HideCropBoundaries,
                    HideScopeBoxes = settings.HideScopeBoxes,
                    HideReferencePlane = settings.HideReferencePlane,
                    ViewLinksInBlue = settings.ViewLinksInBlue
                };

                // ZoomPercentage is only meaningful when ZoomType is Zoom,
                // PDFExportOptions ignores it otherwise, confirmed from the
                // API docs, but only set it in that case anyway rather than
                // relying on Revit to ignore an irrelevant value.
                if (settings.ZoomType == ZoomType.Zoom)
                {
                    options.ZoomPercentage = settings.ZoomPercentage;
                }

                var viewIds = new List<ElementId> { view.Id };

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
                AppLogger.LogError($"PDF export failed for view {outcome.SheetNumber}", ex);
            }

            return outcome;
        }
    }
}