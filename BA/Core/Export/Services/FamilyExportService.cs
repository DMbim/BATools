using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Exports one Family to a standalone RFA file via Document.EditFamily,
    /// optionally followed by a preview image rendered from a matching
    /// view inside that family document. The temporary family document is
    /// always closed in a finally block, whether the export succeeded,
    /// failed partway, or threw, this must never leave a family document
    /// open in the background. Must be called from a valid Revit API
    /// thread context.
    /// </summary>
    public static class FamilyExportService
    {
        public static FamilyExportOutcome ExportFamily(Document doc, string familyUniqueId, FamilyExportSettings settings)
        {
            var element = doc.GetElement(familyUniqueId);

            if (!(element is Family family))
            {
                return new FamilyExportOutcome
                {
                    FamilyName = familyUniqueId,
                    Skipped = true,
                    SkippedReason = "Family could not be resolved, it may have been deleted since the picker was opened."
                };
            }

            var outcome = new FamilyExportOutcome
            {
                FamilyName = family.Name ?? string.Empty,
                CategoryName = family.FamilyCategory?.Name ?? string.Empty
            };

            // Documented way to check before calling EditFamily, rather
            // than relying on catching the exception it throws for system
            // and in-place families.
            if (!family.IsEditable)
            {
                outcome.Skipped = true;
                outcome.SkippedReason = "System or in-place family, cannot be exported as a standalone RFA.";
                return outcome;
            }

            string targetFolder;

            try
            {
                targetFolder = settings.GroupByCategory
                    ? Path.Combine(settings.OutputFolder, SanitizeForFileSystem(outcome.CategoryName))
                    : settings.OutputFolder;

                Directory.CreateDirectory(targetFolder);
            }
            catch (Exception ex)
            {
                outcome.RfaSuccess = false;
                outcome.RfaErrorMessage = $"Could not create output folder: {ex.Message}";
                return outcome;
            }

            var safeFileName = SanitizeForFileSystem(outcome.FamilyName);
            var rfaPath = Path.Combine(targetFolder, safeFileName + ".rfa");

            if (settings.SkipExistingFiles && File.Exists(rfaPath))
            {
                outcome.Skipped = true;
                outcome.SkippedReason = "RFA already exists at the target path, skipped because Skip Existing Files is enabled.";
                return outcome;
            }

            Document familyDoc = null;

            try
            {
                familyDoc = doc.EditFamily(family);

                if (familyDoc == null || !familyDoc.IsFamilyDocument)
                {
                    outcome.RfaSuccess = false;
                    outcome.RfaErrorMessage = "Document.EditFamily did not return a valid family document.";
                    return outcome;
                }

                var saveAsOptions = new SaveAsOptions { OverwriteExistingFile = true };
                familyDoc.SaveAs(rfaPath, saveAsOptions);

                outcome.RfaSuccess = true;
                outcome.RfaPath = rfaPath;

                if (settings.ExportPreviewImage)
                {
                    ExportPreviewImage(familyDoc, outcome, targetFolder, safeFileName, settings);
                }
            }
            catch (Exception ex)
            {
                outcome.RfaSuccess = false;
                outcome.RfaErrorMessage = ex.Message;
                AppLogger.LogError($"Family export failed for '{outcome.FamilyName}'", ex);
            }
            finally
            {
                // Must always run, whether SaveAs succeeded, failed, or
                // threw. A family document left open in the background is
                // exactly the failure mode this guards against.
                if (familyDoc != null)
                {
                    try
                    {
                        familyDoc.Close(false);
                    }
                    catch (Exception closeEx)
                    {
                        AppLogger.LogError($"Failed to close temporary family document for '{outcome.FamilyName}'", closeEx);
                    }
                }
            }

            return outcome;
        }

        private static void ExportPreviewImage(
            Document familyDoc,
            FamilyExportOutcome outcome,
            string targetFolder,
            string safeFileName,
            FamilyExportSettings settings)
        {
            outcome.ImageAttempted = true;

            var candidateViews = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .ToList();

            View matchedView = null;

            foreach (var preferredName in settings.PreferredImageViewNames ?? new System.Collections.Generic.List<string>())
            {
                matchedView = candidateViews.FirstOrDefault(v => string.Equals(v.Name, preferredName, StringComparison.OrdinalIgnoreCase));

                if (matchedView != null)
                {
                    break;
                }
            }

            if (matchedView == null)
            {
                outcome.ImageSuccess = false;
                var namesTried = string.Join(", ", settings.PreferredImageViewNames ?? new System.Collections.Generic.List<string>());
                outcome.ImageErrorMessage = $"None of the preferred view names were found in this family: {namesTried}. The RFA still exported successfully.";
                return;
            }

            var (success, errorMessage) = ImageExportService.ExportViewImage(
                familyDoc,
                matchedView.Id,
                targetFolder,
                safeFileName,
                settings.ImageSettings,
                settings.ImageFormat);

            outcome.ImageSuccess = success;
            outcome.ImageErrorMessage = errorMessage;

            if (success)
            {
                outcome.ImagePath = Path.Combine(targetFolder, safeFileName + ImageExportService.GetExtension(settings.ImageFormat));
            }
        }

        private static string SanitizeForFileSystem(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Unnamed";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());

            return string.IsNullOrWhiteSpace(sanitized) ? "Unnamed" : sanitized.Trim();
        }
    }
}
