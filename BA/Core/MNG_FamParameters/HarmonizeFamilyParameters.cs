using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;


namespace BA.Core
{

    /// <summary>
    /// Orchestrates the harmonization over a set of ParameterPreview decisions.
    /// Keeps the external signature used by HarmonizerEventHandler.
    /// </summary>
    public static class HarmonizeFamilyParameters
    {
        public static void Execute(UIApplication uiapp, Document doc, List<ParameterPreview> decisions, StringBuilder log)
        {
            if (uiapp == null || doc == null)
            {
                TaskDialog.Show("Family Harmonizer", "Invalid Revit context.");
                return;
            }

            if (!doc.IsFamilyDocument)
            {
                TaskDialog.Show("Family Harmonizer", "Open a Family Document to harmonize parameters.");
                return;
            }

            if (decisions == null || decisions.Count == 0)
            {
                log?.AppendLine("No decisions to process.");
                return;
            }

            FamilyManager fm = doc.FamilyManager;
            if (fm == null)
            {
                TaskDialog.Show("Family Harmonizer", "FamilyManager is not available.");
                return;
            }

            int processed = 0;
            int replaced = 0;
            int deleted = 0;
            int renamed = 0;
            int skipped = 0;

            using (var tg = new TransactionGroup(doc, "Harmonize Family Parameters"))
            {
                tg.Start();

                foreach (var d in decisions)
                {
                    processed++;

                    string action = d.EffectiveAction;

                    switch (action)
                    {
                        case "Keep":
                            log?.AppendLine($"KEEP: '{d.Name}'");
                            skipped++;
                            continue;

                        case "Delete":
                            {
                                using (var t = new Transaction(doc, $"Delete '{d.Name}'"))
                                {
                                    t.Start();
                                    try
                                    {
                                        var fp = fm.GetParameters()
                                                   .FirstOrDefault(p => p.Definition.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase));

                                        if (fp == null)
                                        {
                                            log?.AppendLine($"DELETE: '{d.Name}' not found, skipped.");
                                            skipped++;
                                            t.RollBack();
                                            continue;
                                        }

                                        bool ok = FamilyParamUtils.RemoveParameterSafe(doc, fm, fp, log);
                                        if (ok) { deleted++; t.Commit(); }
                                        else { skipped++; t.RollBack(); }
                                    }
                                    catch (Exception ex)
                                    {
                                        log?.AppendLine($"Error deleting '{d.Name}': {ex.Message}");
                                        t.RollBack();
                                        skipped++;
                                    }
                                }
                                continue;
                            }

                        case "Replace":
                            {
                                using (var t = new Transaction(doc, $"Replace '{d.Name}'"))
                                {
                                    t.Start();
                                    try
                                    {
                                        var fpBefore = fm.GetParameters()
                                                          .FirstOrDefault(p => p.Definition.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase));

                                        if (fpBefore != null)
                                        {
                                            var associations = FamilyParamUtils.FindDimensionAssociations(doc, fpBefore);
                                            if (associations.Count > 0)
                                            {
                                                log?.AppendLine($"NOTE: '{d.Name}' labels {associations.Count} dimension(s) before replace:");
                                                foreach (var a in associations)
                                                    log?.AppendLine($"   {a}");
                                                log?.AppendLine($"   After replace, re-check these dimensions: the new shared parameter '{d.TargetName}' may need to be re-applied as the label manually.");
                                            }
                                        }

                                        bool ok = FamilyParamUtils.TryReplaceByName(uiapp, fm, d, log);
                                        if (ok) { replaced++; t.Commit(); }
                                        else { skipped++; t.RollBack(); }
                                    }
                                    catch (Exception ex)
                                    {
                                        log?.AppendLine($"Error on '{d.Name}': {ex.Message}");
                                        t.RollBack();
                                        skipped++;
                                    }
                                }
                                continue;
                            }

                        case "Rename":
                            {
                                using (var t = new Transaction(doc, $"Rename '{d.Name}'"))
                                {
                                    t.Start();
                                    try
                                    {
                                        var fp = fm.GetParameters()
                                                   .FirstOrDefault(p => p.Definition.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase));

                                        if (fp == null)
                                        {
                                            log?.AppendLine($"RENAME: '{d.Name}' not found, skipped.");
                                            skipped++;
                                            t.RollBack();
                                            continue;
                                        }

                                        bool ok = FamilyParamUtils.RenameParameterSafe(doc, fm, fp, d.TargetName, log);
                                        if (ok) { renamed++; t.Commit(); }
                                        else { skipped++; t.RollBack(); }
                                    }
                                    catch (Exception ex)
                                    {
                                        log?.AppendLine($"Error renaming '{d.Name}': {ex.Message}");
                                        t.RollBack();
                                        skipped++;
                                    }
                                }
                                continue;
                            }

                        default:
                            log?.AppendLine($"UNKNOWN ACTION '{action}' for '{d.Name}', skipped.");
                            skipped++;
                            continue;
                    }
                }

                tg.Assimilate();
            }

            log?.AppendLine($"Processed: {processed}, Replaced: {replaced}, Deleted: {deleted}, Renamed: {renamed}, Skipped: {skipped}");
        }
    }

}
