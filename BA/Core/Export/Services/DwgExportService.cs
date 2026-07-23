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
    /// Exports one ViewSheet to a single DWG using a predefined named export
    /// setup from the document (Manage tab, Additional Settings, Export
    /// Setups DWG/DXF). Must be called from a valid Revit API thread context.
    /// </summary>
    public static class DwgExportService
    {
        public static SheetExportOutcome ExportSheet(Document doc, ViewSheet sheet, string folderPath, string fileNameWithoutExtension, string exportSetupName)
        {
            var outcome = new SheetExportOutcome
            {
                SheetNumber = sheet.SheetNumber,
                FolderPath = folderPath,
                FileName = fileNameWithoutExtension + ".dwg"
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

                DWGExportOptions options;

                if (string.IsNullOrWhiteSpace(exportSetupName))
                {
                    options = new DWGExportOptions();
                }
                else
                {
                    var setupNames = BaseExportOptions.GetPredefinedSetupNames(doc);
                    var matchedName = setupNames.FirstOrDefault(n => string.Equals(n, exportSetupName, StringComparison.OrdinalIgnoreCase));

                    if (matchedName == null)
                    {
                        outcome.Success = false;
                        outcome.ErrorMessage = $"DWG export setup '{exportSetupName}' was not found in this document. " +
                            $"Available setups: {(setupNames.Count == 0 ? "(none)" : string.Join(", ", setupNames))}";
                        return outcome;
                    }

                    options = DWGExportOptions.GetPredefinedOptions(doc, matchedName);
                }

                var viewIds = new List<ElementId> { sheet.Id };

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
                AppLogger.LogError($"DWG export failed for sheet {sheet.SheetNumber}", ex);
            }

            return outcome;
        }
    }
}
