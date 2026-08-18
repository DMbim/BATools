using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using BA.Core.Classification;
using BA.Classification;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.UI.Classification
{
    public static class ClassificationUiUtils
    {
        public static ClassificationMode AskMode()
        {
            var td = new TaskDialog("Classification Mode")
            {
                MainInstruction = "How should classification be applied?",
                MainContent = "Choose whether to only fill empty BA_ classification fields, " +
                                  "or overwrite all type values."
            };

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Fill empty only (recommended)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Overwrite all");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var res = td.Show();

            return res switch
            {
                TaskDialogResult.CommandLink1 => ClassificationMode.FillEmptyOnly,
                TaskDialogResult.CommandLink2 => ClassificationMode.OverwriteAll,
                _ => ClassificationMode.Cancel
            };
        }

        public static void ShowReport(ClassificationReport r, List<string> skippedRules)
        {
            var lines = new List<string>
            {
                $"Total types in model: {r.TotalTypes}",
                $"Types considered (categories with rules): {r.ConsideredTypes}",
                $"Classified: {r.Classified}",
                "",
                $"Skipped (no category): {r.SkippedNoCategory}",
                $"Skipped (no rules for category): {r.SkippedNoRulesForCategory}",
                $"Skipped (missing BA_ parameters): {r.SkippedMissingParameters}",
                $"Skipped (already classified): {r.SkippedAlreadyClassified}",
                $"Skipped (read-only / type mismatch): {r.SkippedReadOnlyOrTypeMismatch}",
                $"No match found: {r.NoMatch}",
            };

            if (skippedRules.Count > 0)
            {
                lines.Add("");
                lines.Add("Rules skipped (category not resolved):");
                lines.AddRange(skippedRules.Take(15).Select(s => " - " + s));
                if (skippedRules.Count > 15) lines.Add(" - ...");
            }

            if (r.ExamplesNoMatch.Count > 0)
            {
                lines.Add("");
                lines.Add("Examples (no match):");
                lines.AddRange(r.ExamplesNoMatch.Take(10).Select(s => " - " + s));
            }

            if (r.ExamplesMissingParams.Count > 0)
            {
                lines.Add("");
                lines.Add("Examples (missing BA_ params on Type):");
                lines.AddRange(r.ExamplesMissingParams.Take(10).Select(s => " - " + s));
            }

            TaskDialog.Show("Classification Report", string.Join(Environment.NewLine, lines));
        }
    }
}
