using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using BA.Core.Classification;   // ClassificationMode
// uses BA.Classification.ClassificationEngine + ClassificationRunResult + ClassificationReport

namespace BA.Classification
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_ClassifyElements : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc?.Document;

            if (doc == null)
            {
                TaskDialog.Show("BA Classification", "No active document.");
                return Result.Cancelled;
            }

            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show("BA Classification", "This tool works in project documents, not family documents.");
                return Result.Cancelled;
            }

            try
            {
                // 1) Pick Excel
                string excelPath = PickExcelFile();
                if (string.IsNullOrWhiteSpace(excelPath))
                    return Result.Cancelled;

                if (!File.Exists(excelPath))
                {
                    TaskDialog.Show("BA Classification", "Invalid Excel file path.");
                    return Result.Failed;
                }

                // 2) Mode
                var mode = AskMode();
                if (mode == ClassificationMode.Cancel)
                    return Result.Cancelled;

                // 3) Run engine
                var engine = new ClassificationEngine(excelPath);
                var result = engine.ClassifyTypes(doc, mode, writeTrace: true);

                // 4) Show summary + warnings + trace
                ShowReport(result.Report, result.Warnings, result.TraceCsvPath);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}\n\n{ex.StackTrace}";
                TaskDialog.Show("BA Classification - Error", message);
                return Result.Failed;
            }
        }

        private static string PickExcelFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                Title = "Select BA Classification Rules Excel File",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                RestoreDirectory = true
            };

            return dlg.ShowDialog() == true ? dlg.FileName : "";
        }

        /// <summary>
        /// Same behavior as your existing UI helper, but local to avoid project references.
        /// </summary>
        private static ClassificationMode AskMode()
        {
            var td = new TaskDialog("Classification Mode")
            {
                MainInstruction = "How should classification be applied?",
                MainContent =
                    "Choose whether to only fill empty BA_ classification fields, " +
                    "or overwrite all type values."
            };

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fill empty only (recommended)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Overwrite all");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var res = td.Show();
            return res switch
            {
                TaskDialogResult.CommandLink1 => ClassificationMode.FillEmptyOnly,
                TaskDialogResult.CommandLink2 => ClassificationMode.OverwriteAll,
                _ => ClassificationMode.Cancel
            };
        }

        private static void ShowReport(ClassificationReport r, List<string> warnings, string? traceCsvPath)
        {
            warnings ??= new List<string>();

            var lines = new List<string>
            {
                $"Total types in model: {r.TotalTypes}",
                $"Types considered: {r.ConsideredTypes}",
                $"Classified: {r.Classified}",
                "",
                $"Skipped (missing BA_ params): {r.SkippedMissingParameters}",
                $"Skipped (already classified): {r.SkippedAlreadyClassified}",
                $"Skipped (read-only / mismatch): {r.SkippedReadOnlyOrTypeMismatch}",
                $"No match found: {r.NoMatch}",
            };

            if (!string.IsNullOrWhiteSpace(traceCsvPath))
            {
                lines.Add("");
                lines.Add("Rule Trace CSV:");
                lines.Add(traceCsvPath);
            }

            if (warnings.Count > 0)
            {
                lines.Add("");
                lines.Add("Warnings:");
                foreach (var w in warnings.Take(12))
                    lines.Add(" - " + w);
                if (warnings.Count > 12)
                    lines.Add(" - ...");
            }

            if (r.ExamplesNoMatch.Count > 0)
            {
                lines.Add("");
                lines.Add("Examples (no match):");
                foreach (var ex in r.ExamplesNoMatch.Take(10))
                    lines.Add(" - " + ex);
            }

            if (r.ExamplesMissingParams.Count > 0)
            {
                lines.Add("");
                lines.Add("Examples (missing BA_ params on Type):");
                foreach (var ex in r.ExamplesMissingParams.Take(10))
                    lines.Add(" - " + ex);
            }

            TaskDialog.Show("BA Classification Report", string.Join(Environment.NewLine, lines));
        }
    }
}
