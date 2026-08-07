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
    /// both) to a single DWG. Must be called from a valid Revit API thread
    /// context.
    ///
    /// When DwgSettings.PredefinedSetupName is set, every other DwgSettings
    /// field is ignored, the loaded setup (via GetPredefinedOptions(), from
    /// a setup already built by hand in Revit's own "Modify DWG/DXF Export
    /// Setup" dialog) is used completely as-is, matching exactly how
    /// Revit's own native export dialog behaves when a predefined setup is
    /// selected. This is deliberate, not partial: a predefined setup
    /// carries every setting from that dialog, not just the layer table,
    /// Colors and PropOverrides included, and overwriting any of them
    /// silently breaks values that were specifically tuned for that
    /// setup. Confirmed as the exact cause of a real color mismatch: this
    /// tool's own ByLayer selection was overwriting a predefined setup's
    /// own ByEntity, producing wrong colors that the native exporter,
    /// using that setup untouched, did not reproduce.
    /// </summary>
    public static class DwgExportService
    {
        /// <summary>
        /// SheetExportOutcome.SheetNumber holds view.Name when exporting a
        /// non-sheet view, not a real sheet number, reused rather than
        /// adding a second, near-identical outcome model.
        /// </summary>
        public static SheetExportOutcome ExportView(
            Document doc,
            View view,
            string folderPath,
            string fileNameWithoutExtension,
            DwgSettings settings)
        {
            var outcome = new SheetExportOutcome
            {
                SheetNumber = view is ViewSheet sheet ? sheet.SheetNumber : view.Name,
                FolderPath = folderPath,
                FileName = fileNameWithoutExtension + ".dwg"
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

                settings = settings ?? new DwgSettings();

                DWGExportOptions options;

                if (string.IsNullOrWhiteSpace(settings.PredefinedSetupName))
                {
                    options = new DWGExportOptions
                    {
                        LayerMapping = settings.ResolveLayerMappingString(),
                        FileVersion = settings.FileVersion,
                        TargetUnit = settings.TargetUnit,
                        MergedViews = settings.MergedViews,
                        SharedCoords = settings.SharedCoords,
                        ExportingAreas = settings.ExportingAreas,
                        HideScopeBox = settings.HideScopeBox,
                        HideReferencePlane = settings.HideReferencePlane,
                        LineScaling = settings.LineScaling,
                        Colors = settings.Colors,
                        PropOverrides = settings.PropOverrides
                    };
                }
                else
                {
                    try
                    {
                        // Deliberately not touched further below. A
                        // predefined setup carries every setting from
                        // Revit's own "Modify DWG/DXF Export Setup"
                        // dialog, not just the layer table, Colors,
                        // PropOverrides, linetype scale, file version,
                        // all of it. Overwriting any of those here
                        // silently destroys values that were specifically
                        // tuned for that setup, confirmed as the exact
                        // cause of a real color mismatch: this tool's own
                        // ByLayer selection was overwriting BA_ACAD's own
                        // ByEntity, producing wrong colors that the native
                        // exporter, using BA_ACAD untouched, did not
                        // reproduce. Every other DwgSettings field is
                        // ignored whenever PredefinedSetupName is set.
                        options = DWGExportOptions.GetPredefinedOptions(doc, settings.PredefinedSetupName);
                    }
                    catch (Exception ex)
                    {
                        outcome.Success = false;
                        outcome.ErrorMessage = $"Could not load DWG export setup '{settings.PredefinedSetupName}': {ex.Message}. Check the setup still exists under Export Setups DWG/DXF.";
                        return outcome;
                    }
                }

                var viewIds = new List<ElementId> { view.Id };

                var success = doc.Export(folderPath, fileNameWithoutExtension, viewIds, options);

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
                AppLogger.LogError($"DWG export failed for view {outcome.SheetNumber}", ex);
            }

            return outcome;
        }
    }
}