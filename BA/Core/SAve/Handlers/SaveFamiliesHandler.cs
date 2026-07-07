using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Families.Models;

namespace BA.Families.Handlers
{
    /// <summary>
    /// IExternalEventHandler that performs the family save loop.
    /// Runs on Revit's main thread — all Revit API calls are valid here.
    /// Progress callbacks are invoked via Dispatcher.BeginInvoke so the WPF
    /// pump can process between families and give partial UI updates.
    /// </summary>
    public class SaveFamiliesHandler : IExternalEventHandler
    {
        /// <summary>Full master list (includes unselected items). Set before raising.</summary>
        public IReadOnlyList<FamilyExportItem> ItemsToSave { get; set; }
            = Array.Empty<FamilyExportItem>();

        /// <summary>Options snapshot. Set before raising.</summary>
        public SaveFamiliesOptions Options { get; set; } = new();

        /// <summary>Fired after each item is finalized (Saved / Skipped / Error).</summary>
        public Action<FamilyExportItem>? OnItemCompleted { get; set; }

        /// <summary>Fired once all items have been processed.</summary>
        public Action? OnAllCompleted { get; set; }

        // ─── IExternalEventHandler ───────────────────────────────────────────────

        public string GetName() => "BA_SaveFamiliesHandler";

        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;

            foreach (FamilyExportItem item in ItemsToSave)
            {
                if (!item.IsSelected)
                    continue;

                // Mark as in-progress (fires on this thread — main thread)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.Status = FamilySaveStatus.Saving;
                    item.StatusMessage = null;
                });

                ProcessItem(doc, item);

                // Notify ViewModel after item is finalized
                FamilyExportItem captured = item;
                Application.Current.Dispatcher.BeginInvoke(() =>
                    OnItemCompleted?.Invoke(captured));
            }

            Application.Current.Dispatcher.BeginInvoke(() =>
                OnAllCompleted?.Invoke());
        }

        // ─── Core logic ──────────────────────────────────────────────────────────

        private void ProcessItem(Document doc, FamilyExportItem item)
        {
            try
            {
                if (doc.GetElement(item.FamilyId) is not Family family)
                {
                    Fail(item, "Element no longer exists in the document.");
                    return;
                }

                // IsEditable returns false for system families and in-place families.
                // Belt-and-suspenders: handler also catches InvalidOperationException below.
                if (!family.IsEditable)
                {
                    Skip(item, "Not an editable family (system or in-place).");
                    return;
                }

                string targetFolder = Options.OrganizeByCategory
                    ? Path.Combine(Options.OutputFolder, SanitizeFileName(item.CategoryName))
                    : Options.OutputFolder;

                if (!Directory.Exists(targetFolder))
                    Directory.CreateDirectory(targetFolder);

                string? targetPath = ResolveTargetPath(targetFolder, item.Name, Options.OverwriteMode);

                if (targetPath is null)
                {
                    Skip(item, "File already exists; overwrite mode is Skip.");
                    return;
                }

                Document familyDoc = doc.EditFamily(family);
                try
                {
                    var saveOpts = new SaveAsOptions
                    {
                        OverwriteExistingFile = true,
                        Compact = Options.CompactFile
                    };

                    // Resolve thumbnail view by name — use per-item override first,
                    // fall back to global option, fall back to no override.
                    string thumbnailName = !string.IsNullOrWhiteSpace(item.ThumbnailViewName)
                        ? item.ThumbnailViewName!
                        : Options.ThumbnailViewName ?? "{3D}";

                    ElementId? previewId = ResolveViewId(familyDoc, thumbnailName);
                    if (previewId != null)
                        saveOpts.PreviewViewId = previewId;  // <- CHANGED

                    familyDoc.SaveAs(targetPath, saveOpts);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.Status = FamilySaveStatus.Saved;
                        item.StatusMessage = targetPath;
                    });
                }
                finally
                {
                    // Always close — EditFamily opens a background document in Revit
                    familyDoc.Close(false);
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                Fail(item, $"Revit: {ex.Message}");
            }
            catch (Exception ex)
            {
                Fail(item, ex.Message);
            }
        }

        // ─── Path helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns null when the file exists and mode is Skip.
        /// </summary>
        private static string? ResolveTargetPath(string folder, string familyName, OverwriteMode mode)
        {
            string safeName = SanitizeFileName(familyName);
            string basePath = Path.Combine(folder, safeName + ".rfa");

            if (!File.Exists(basePath))
                return basePath;

            return mode switch
            {
                OverwriteMode.Overwrite => basePath,
                OverwriteMode.Skip => null,
                OverwriteMode.AddSuffix => ResolveWithSuffix(folder, safeName),
                _ => null
            };
        }

        private static string ResolveWithSuffix(string folder, string baseName)
        {
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(folder, $"{baseName}_{i:D2}.rfa");
                if (!File.Exists(candidate))
                    return candidate;
            }
            // Extreme fallback: timestamp
            return Path.Combine(folder, $"{baseName}_{DateTime.Now:yyyyMMddHHmmss}.rfa");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>
        /// Finds a view in the family document by name.
        /// Returns null if no match is found — caller falls back to Revit default.
        /// The match is case-insensitive to tolerate minor naming inconsistencies.
        /// </summary>
        private static ElementId? ResolveViewId(Document familyDoc, string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                return null;

            try
            {
                var view = new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .Cast<Autodesk.Revit.DB.View>()
                    .FirstOrDefault(v =>
                        !v.IsTemplate &&
                        string.Equals(v.Name, viewName.Trim(),
                            StringComparison.OrdinalIgnoreCase));

                return view?.Id;
            }
            catch
            {
                return null;
            }
        }

        // ─── Status helpers ───────────────────────────────────────────────────────

        private static void Fail(FamilyExportItem item, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                item.Status = FamilySaveStatus.Error;
                item.StatusMessage = message;
            });
        }

        private static void Skip(FamilyExportItem item, string reason)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                item.Status = FamilySaveStatus.Skipped;
                item.StatusMessage = reason;
            });
        }
    }
}