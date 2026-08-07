using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Exports a single view (a sheet, or any other view, including one
    /// inside a family document) to JPEG or PNG. Shares one implementation
    /// for both formats since ImageExportOptions only differs between them
    /// by file type, and shares the same core between sheet export and
    /// family preview image export.
    ///
    /// Document.ExportImage returns void and throws specific exceptions on
    /// most failures (confirmed from the API docs: ArgumentException,
    /// FileAccessException, FileNotFoundException, InvalidOperationException,
    /// OptionalFunctionalityNotAvailableException). However there is a
    /// documented, reproducible report of ExportRange.SetOfViews completing
    /// with no exception and producing no file at all in some environments,
    /// while other confirmed working samples use the exact same API path
    /// successfully. Given that inconsistency, this does not trust the
    /// absence of an exception as proof of success, it verifies the output
    /// file actually exists on disk afterward and treats a missing file as
    /// a failure.
    ///
    /// A second defensive step covers filename uncertainty: it is not
    /// fully confirmed whether Revit uses FilePath exactly or appends a
    /// suffix when ExportRange is SetOfViews with a single view. This
    /// snapshots the target folder before export and, if the exact target
    /// filename is not found afterward, looks for a new file of the right
    /// extension that was not there before and renames it to the exact
    /// requested name, rather than assuming either naming behavior.
    ///
    /// Must be called from a valid Revit API thread context.
    /// </summary>
    public static class ImageExportService
    {
        public static SheetExportOutcome ExportSheet(
            Document doc,
            ViewSheet sheet,
            string folderPath,
            string fileNameWithoutExtension,
            ImageSettings settings,
            ExportFormat format)
        {
            var extension = GetExtension(format);

            var outcome = new SheetExportOutcome
            {
                SheetNumber = sheet.SheetNumber,
                FolderPath = folderPath,
                FileName = fileNameWithoutExtension + extension
            };

            var (success, errorMessage) = ExportViewCore(doc, sheet.Id, folderPath, fileNameWithoutExtension, settings, format);

            outcome.Success = success;
            outcome.ErrorMessage = errorMessage;

            return outcome;
        }

        /// <summary>
        /// Exports an arbitrary view, used for family preview images where
        /// there is no ViewSheet, just a View inside a family document.
        /// </summary>
        public static (bool Success, string ErrorMessage) ExportViewImage(
            Document doc,
            ElementId viewId,
            string folderPath,
            string fileNameWithoutExtension,
            ImageSettings settings,
            ExportFormat format)
        {
            return ExportViewCore(doc, viewId, folderPath, fileNameWithoutExtension, settings, format);
        }

        public static string GetExtension(ExportFormat format) => format == ExportFormat.Jpeg ? ".jpg" : ".png";

        private static (bool Success, string ErrorMessage) ExportViewCore(
            Document doc,
            ElementId viewId,
            string folderPath,
            string fileNameWithoutExtension,
            ImageSettings settings,
            ExportFormat format)
        {
            var extension = GetExtension(format);
            var fileType = format == ExportFormat.Jpeg ? ImageFileType.JPEGLossless : ImageFileType.PNG;

            try
            {
                Directory.CreateDirectory(folderPath);

                settings = settings ?? new ImageSettings();

                var targetPath = Path.Combine(folderPath, fileNameWithoutExtension + extension);

                var beforeFiles = new HashSet<string>(
                    Directory.GetFiles(folderPath, "*" + extension),
                    StringComparer.OrdinalIgnoreCase);

                var options = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = targetPath,
                    HLRandWFViewsFileType = fileType,
                    ShadowViewsFileType = fileType,
                    ImageResolution = settings.Resolution,
                    ZoomType = settings.ZoomType,
                    FitDirection = settings.FitDirection,
                    ShouldCreateWebSite = false
                };

                if (settings.ZoomType == ZoomFitType.FitToPage)
                {
                    options.PixelSize = settings.PixelSize;
                }
                else
                {
                    options.Zoom = settings.ZoomPercentage;
                }

                options.SetViewsAndSheets(new List<ElementId> { viewId });

                doc.ExportImage(options);

                if (File.Exists(targetPath))
                {
                    return (true, string.Empty);
                }

                var afterFiles = Directory.GetFiles(folderPath, "*" + extension);
                var newFile = afterFiles.FirstOrDefault(f => !beforeFiles.Contains(f));

                if (newFile != null)
                {
                    File.Move(newFile, targetPath);
                    return (true, string.Empty);
                }

                return (false, "Document.ExportImage completed without throwing, but no output file was found. " +
                    "This matches a documented Revit API inconsistency with ExportRange.SetOfViews in some environments.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"{format} export failed for view id {viewId}", ex);
                return (false, ex.Message);
            }
        }
    }
}
