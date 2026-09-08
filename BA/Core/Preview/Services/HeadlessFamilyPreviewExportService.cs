// Path: BA/Core/Content/Preview/HeadlessFamilyPreviewExportService.cs
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Content.Preview
{
    public sealed class PreviewExportResult
    {
        public string FamilyPath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generates a consistent, forced angle preview for a library family by
    /// exporting from a dedicated isometric view created inside the family
    /// document itself. Never places an instance and never touches a host
    /// project document, so host dependent placement types are not a
    /// limitation here the way they are for LoadedFamilyPreviewExportService.
    /// The family document is opened, worked on, and closed without saving,
    /// so nothing persists on disk except the exported images.
    /// Must run inside a Revit API context, on the main API thread.
    /// </summary>
    public static class HeadlessFamilyPreviewExportService
    {
        private const string PreviewViewName = "BA_PreviewExport_Iso";

        public static PreviewExportResult ExportPreview(Application app, string familyPath, bool overwriteExisting)
        {
            var result = new PreviewExportResult { FamilyPath = familyPath };

            if (string.IsNullOrWhiteSpace(familyPath))
            {
                result.Success = false;
                result.Message = "Family path is empty.";
                return result;
            }

            if (!File.Exists(familyPath))
            {
                result.Success = false;
                result.Message = "Family file does not exist.";
                return result;
            }

            if (!familyPath.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = "File is not an RFA family.";
                return result;
            }

            string outputPngPath = Path.ChangeExtension(familyPath, ".png");
            string outputJpgPath = Path.ChangeExtension(familyPath, ".jpg");

            if (!overwriteExisting && File.Exists(outputPngPath) && File.Exists(outputJpgPath))
            {
                result.Success = true;
                result.Message = "Skipped, PNG and JPG already exist.";
                return result;
            }

            Document? familyDoc = null;

            try
            {
                var openOptions = new OpenOptions { Audit = false };
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(familyPath);
                familyDoc = app.OpenDocumentFile(modelPath, openOptions);

                if (familyDoc == null)
                {
                    result.Success = false;
                    result.Message = "Failed to open family document.";
                    return result;
                }

                if (!familyDoc.IsFamilyDocument)
                {
                    result.Success = false;
                    result.Message = "Opened file is not a family document.";
                    return result;
                }

                View3D exportView = FindOrCreatePreviewView(familyDoc);

                ExportViewToImage(familyDoc, exportView, outputPngPath, ImageFileType.PNG);
                ExportViewToImage(familyDoc, exportView, outputJpgPath, ImageFileType.JPEGLossless);

                result.Success = true;
                result.Message = "Preview exported to PNG and JPG from a forced isometric view.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                if (familyDoc != null)
                {
                    try { familyDoc.Close(false); }
                    catch { }
                }
            }
        }

        private static View3D FindOrCreatePreviewView(Document familyDoc)
        {
            View3D? existing = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(PreviewViewName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            ViewFamilyType? viewFamilyType = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewFamilyType == null)
                throw new InvalidOperationException("Family document has no 3D view family type available.");

            using var tx = new Transaction(familyDoc, "BA Create Preview Export View");
            tx.Start();
            View3D created = View3D.CreateIsometric(familyDoc, viewFamilyType.Id);
            created.Name = PreviewViewName;
            tx.Commit();

            return created;
        }

        private static void ExportViewToImage(Document doc, View view, string outputImagePath, ImageFileType fileType)
        {
            string folder = Path.GetDirectoryName(outputImagePath) ?? string.Empty;
            string fileBase = Path.GetFileNameWithoutExtension(outputImagePath);
            string expectedExtension = Path.GetExtension(outputImagePath);

            if (string.IsNullOrWhiteSpace(folder))
                throw new InvalidOperationException("Output image folder could not be resolved.");

            Directory.CreateDirectory(folder);

            // Scoped to only the extension this call produces, this is the
            // fix for the bug found in FamilyPreviewExportService, where an
            // unscoped delete let the JPG pass erase the PNG pass's output.
            string normalizedExpected = expectedExtension.ToLowerInvariant();
            string[] extensionsToDelete = normalizedExpected == ".jpg"
                ? new[] { ".jpg", ".jpeg" }
                : new[] { normalizedExpected };

            foreach (string file in Directory.GetFiles(folder, fileBase + ".*"))
            {
                if (extensionsToDelete.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    try { File.Delete(file); }
                    catch { }
                }
            }

            var opts = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = Path.Combine(folder, fileBase),
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = fileType,
                ShadowViewsFileType = fileType,
                ImageResolution = ImageResolution.DPI_150,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 1200
            };

            opts.SetViewsAndSheets(new List<ElementId> { view.Id });
            doc.ExportImage(opts);

            var matches = Directory.GetFiles(folder, fileBase + ".*")
                .Where(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    return normalizedExpected == ".jpg" ? (ext == ".jpg" || ext == ".jpeg") : ext == normalizedExpected;
                })
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            string actual = matches.FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(actual) || !File.Exists(actual))
                throw new InvalidOperationException($"Revit did not produce the expected image file '{expectedExtension}'.");

            if (!actual.Equals(outputImagePath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(outputImagePath))
                    File.Delete(outputImagePath);
                File.Move(actual, outputImagePath);
            }
        }
    }
}