using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Content.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Content.Services
{
    public sealed class FamilyPreviewExportService
    {
        private readonly UIApplication _uiApp;

        public FamilyPreviewExportService(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
        }

        public IReadOnlyList<ContentPreviewExportItemResult> ExportPreviews(
            IEnumerable<string> familyPaths,
            bool overwriteExisting)
        {
            if (familyPaths == null)
                throw new ArgumentNullException(nameof(familyPaths));

            var results = new List<ContentPreviewExportItemResult>();

            foreach (string familyPath in familyPaths
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                results.Add(ExportSinglePreview(familyPath, overwriteExisting));
            }

            return results;
        }

        public ContentPreviewExportItemResult ExportSinglePreview(string familyPath, bool overwriteExisting)
        {
            var result = new ContentPreviewExportItemResult
            {
                FamilyPath = familyPath
            };

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

            string outputPngPath = BuildOutputImagePath(familyPath, "png");
            string outputJpgPath = BuildOutputImagePath(familyPath, "jpg");

            result.OutputImagePath = outputPngPath;

            if (!overwriteExisting && File.Exists(outputPngPath) && File.Exists(outputJpgPath))
            {
                result.Success = true;
                result.Message = "Skipped because PNG and JPG previews already exist.";
                return result;
            }

            Document? familyDoc = null;

            try
            {
                var openOptions = new OpenOptions
                {
                    Audit = false
                };

                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(familyPath);
                familyDoc = _uiApp.Application.OpenDocumentFile(modelPath, openOptions);

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

                Autodesk.Revit.DB.View exportView = FindBestExportView(familyDoc);

                ExportViewToImage(
                    doc: familyDoc,
                    view: exportView,
                    outputImagePath: outputPngPath,
                    fileType: ImageFileType.PNG);

                ExportViewToImage(
                    doc: familyDoc,
                    view: exportView,
                    outputImagePath: outputJpgPath,
                    fileType: ImageFileType.JPEGLossless);

                result.Success = true;
                result.Message = $"Preview exported from view '{exportView.Name}' to PNG and JPG.";
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
                    try
                    {
                        familyDoc.Close(false);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string BuildOutputImagePath(string familyPath, string extensionWithoutDot)
        {
            string dir = Path.GetDirectoryName(familyPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(familyPath);
            return Path.Combine(dir, $"{baseName}.{extensionWithoutDot}");
        }

        private static Autodesk.Revit.DB.View FindBestExportView(Autodesk.Revit.DB.Document doc)
        {
            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(IsUsableExportView)
                .ToList();

            if (allViews.Count == 0)
                throw new InvalidOperationException("Family contains no usable non-template views.");

            View3D? v3 = allViews
                .OfType<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.ThreeD);

            if (v3 != null)
                return v3;

            View? plan = allViews.FirstOrDefault(v =>
                v.ViewType == ViewType.FloorPlan ||
                v.ViewType == ViewType.CeilingPlan);

            if (plan != null)
                return plan;

            View? elevOrSection = allViews.FirstOrDefault(v =>
                v.ViewType == ViewType.Elevation ||
                v.ViewType == ViewType.Section);

            if (elevOrSection != null)
                return elevOrSection;

            View? drafting = allViews.FirstOrDefault(v => v.ViewType == ViewType.DraftingView);
            if (drafting != null)
                return drafting;

            return allViews.First();
        }

        private static bool IsUsableExportView(Autodesk.Revit.DB.View view)
        {
            if (view == null)
                return false;

            if (view.IsTemplate)
                return false;

            if (view.ViewType == ViewType.Schedule)
                return false;

            if (view.ViewType == ViewType.ProjectBrowser)
                return false;

            if (view.ViewType == ViewType.SystemBrowser)
                return false;

            if (view.ViewType == ViewType.Internal)
                return false;

            if (view.ViewType == ViewType.Report)
                return false;

            if (view.ViewType == ViewType.CostReport)
                return false;

            if (view.ViewType == ViewType.LoadsReport)
                return false;

            if (view.ViewType == ViewType.PanelSchedule)
                return false;

            if (view.ViewType == ViewType.Rendering)
                return false;

            if (view.ViewType == ViewType.Walkthrough)
                return false;

            return true;
        }

        private static void ExportViewToImage(
            Document doc,
                Autodesk.Revit.DB.View view,
            string outputImagePath,
            ImageFileType fileType)
        {
            string folder = Path.GetDirectoryName(outputImagePath) ?? string.Empty;
            string fileBase = Path.GetFileNameWithoutExtension(outputImagePath);
            string expectedExtension = Path.GetExtension(outputImagePath);

            if (string.IsNullOrWhiteSpace(folder))
                throw new InvalidOperationException("Output image folder could not be resolved.");

            Directory.CreateDirectory(folder);
            DeleteExistingMatchingImageFiles(folder, fileBase);

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

            string actual = FindExportedImageFile(folder, fileBase, expectedExtension);
            if (string.IsNullOrWhiteSpace(actual) || !File.Exists(actual))
                throw new InvalidOperationException($"Revit did not produce the expected image file '{expectedExtension}'.");

            if (!actual.Equals(outputImagePath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(outputImagePath))
                    File.Delete(outputImagePath);

                File.Move(actual, outputImagePath);
            }
        }

        private static void DeleteExistingMatchingImageFiles(string folder, string fileBase)
        {
            if (!Directory.Exists(folder))
                return;

            foreach (string file in Directory.GetFiles(folder, fileBase + ".*"))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string FindExportedImageFile(string folder, string fileBase, string expectedExtension)
        {
            string normalizedExpected = expectedExtension.ToLowerInvariant();

            var matches = Directory.GetFiles(folder, fileBase + ".*")
                .Where(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (normalizedExpected == ".jpg")
                        return ext == ".jpg" || ext == ".jpeg";

                    return ext == normalizedExpected;
                })
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            return matches.FirstOrDefault() ?? string.Empty;
        }
    }
}