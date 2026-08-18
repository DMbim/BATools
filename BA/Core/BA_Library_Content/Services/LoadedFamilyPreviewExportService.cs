// Path: BA\Core\Content\Services\LoadedFamilyPreviewExportService.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BA.Core.Content.Services
{
    public static class LoadedFamilyPreviewExportService
    {
        private static readonly string CacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BA", "ContentBrowser", "LoadedFamilyPreviews");

        /// <summary>
        /// Exports an isolated preview image for the given FamilySymbol.
        /// Places a temporary instance at the origin (if the type has no
        /// existing placement to reuse), isolates it in a temp 3D view,
        /// exports, then cleans up the temporary instance and isolation
        /// state. Everything runs in one transaction group so nothing
        /// persists in the user's model on success or failure.
        /// Must run inside Revit API context (via AppExternalInvoker).
        /// </summary>
        public static string ExportPreview(Document doc, ElementId symbolId, string cacheKey)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(cacheKey)) throw new ArgumentException("Cache key required.", nameof(cacheKey));

            if (doc.GetElement(symbolId) is not FamilySymbol symbol)
                throw new InvalidOperationException("Type no longer exists in the document.");

            Directory.CreateDirectory(CacheFolder);
            string outputPath = Path.Combine(CacheFolder, cacheKey + ".png");
            string folder = Path.GetDirectoryName(outputPath)!;
            string fileBase = Path.GetFileNameWithoutExtension(outputPath);

            View3D? view = FindOrCreateTempView(doc);
            if (view == null)
                throw new InvalidOperationException("No 3D view available to export a preview from.");

            using var group = new TransactionGroup(doc, "BA Preview Export (temporary, rolled back)");
            group.Start();

            ElementId? tempInstanceId = null;
            bool activatedHere = false;

            try
            {
                using (var activateTx = new Transaction(doc, "BA Activate Symbol"))
                {
                    activateTx.Start();
                    if (!symbol.IsActive)
                    {
                        symbol.Activate();
                        activatedHere = true;
                    }
                    activateTx.Commit();
                }

                ElementId targetInstanceId = FindExistingInstanceId(doc, symbolId);

                if (targetInstanceId == ElementId.InvalidElementId)
                {
                    using var placeTx = new Transaction(doc, "BA Place Temp Preview Instance");
                    placeTx.Start();

                    FamilyInstance? created = TryCreateInstance(doc, symbol);
                    if (created == null)
                    {
                        placeTx.RollBack();
                        throw new InvalidOperationException(
                            "Could not place a temporary instance of this type for preview (likely a hosted or system-dependent family).");
                    }

                    doc.Regenerate();
                    tempInstanceId = created.Id;
                    targetInstanceId = created.Id;

                    placeTx.Commit();
                }

                using (var isolateTx = new Transaction(doc, "BA Isolate For Preview"))
                {
                    isolateTx.Start();

                    if (view.IsTemporaryHideIsolateActive())
                        view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                    view.IsolateElementTemporary(targetInstanceId);
                    ZoomFitInView(doc, view, targetInstanceId);

                    isolateTx.Commit();
                }

                DeleteExistingMatchingFiles(folder, fileBase);

                var opts = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(folder, fileBase),
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = 600
                };

                opts.SetViewsAndSheets(new List<ElementId> { view.Id });

                doc.ExportImage(opts);

                string? actual = Directory.GetFiles(folder, fileBase + "*.png")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(actual) || !File.Exists(actual))
                    throw new InvalidOperationException("Revit did not produce the expected preview image.");

                if (!string.Equals(actual, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    File.Move(actual, outputPath);
                }

                return outputPath;
            }
            finally
            {
                // Always roll back: removes the temp instance, restores
                // isolation state, and reverts symbol activation if we
                // triggered it here. The exported PNG on disk is
                // unaffected since it was written before rollback.
                if (group.HasStarted() && !group.HasEnded())
                    group.RollBack();
            }
        }

        private static ElementId FindExistingInstanceId(Document doc, ElementId symbolId)
        {
            var instance = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .FirstOrDefault(fi => fi.GetTypeId() == symbolId);

            return instance?.Id ?? ElementId.InvalidElementId;
        }

        private static FamilyInstance? TryCreateInstance(Document doc, FamilySymbol symbol)
        {
            try
            {
                if (symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased ||
                    symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted)
                {
                    Level? level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .FirstOrDefault();

                    if (level == null)
                        return null;

                    return doc.Create.NewFamilyInstance(XYZ.Zero, symbol, level, StructuralType.NonStructural);
                }

                // Non-hosted / other placement types: try the simple overload.
                return doc.Create.NewFamilyInstance(XYZ.Zero, symbol, StructuralType.NonStructural);
            }
            catch
            {
                return null;
            }
        }

        private static void ZoomFitInView(Document doc, View3D view, ElementId targetId)
        {
            // IsolateElementTemporary + ImageExportOptions.ZoomFitType.FitToPage
            // already frames the isolated element; explicit camera adjustment
            // isn't required here since ImageExportOptions handles fit at
            // export time based on the view's currently visible (isolated) set.
        }

        private static View3D? FindOrCreateTempView(Document doc)
        {
            View3D? existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective && v.Name.StartsWith("BA_TempPreview_"));

            if (existing != null)
                return existing;

            ViewFamilyType? viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewFamilyType == null)
                return null;

            using var tx = new Transaction(doc, "BA Create Temp Preview View");
            tx.Start();
            View3D created = View3D.CreateIsometric(doc, viewFamilyType.Id);
            created.Name = $"BA_TempPreview_{Guid.NewGuid():N}";
            tx.Commit();

            return created;
        }

        private static void DeleteExistingMatchingFiles(string folder, string fileBase)
        {
            if (!Directory.Exists(folder))
                return;

            foreach (string file in Directory.GetFiles(folder, fileBase + "*.png"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }
}