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
                int skipped = 0;

                using (var tg = new TransactionGroup(doc, "Harmonize Family Parameters"))
                {
                    tg.Start();

                    foreach (var d in decisions)
                    {
                        processed++;

                        // Only act on rows that are mapped to Replace/Map Shared/etc.
                        // Assuming UI sets Action string: "Keep", "Replace", etc. We treat NOT 'Keep' as a request to replace.
                        if (string.Equals(d.Action, "Keep", StringComparison.OrdinalIgnoreCase))
                        {
                            log?.AppendLine($"KEEP: '{d.Name}'");
                            skipped++;
                            continue;
                        }

                        using (var t = new Transaction(doc, $"Replace '{d.Name}'"))
                        {
                            t.Start();
                            try
                            {
                                bool ok = FamilyParamUtils.TryReplaceByName(uiapp, fm, d, log);
                                if (ok) replaced++;
                                else skipped++;
                                t.Commit();
                            }
                            catch (Exception ex)
                            {
                                log?.AppendLine($"Error on '{d.Name}': {ex.Message}");
                                t.RollBack();
                                skipped++;
                            }
                        }
                    }

                    tg.Assimilate();
                }

                log?.AppendLine($"Processed: {processed}, Replaced: {replaced}, Skipped: {skipped}");
            }
        }
    

}
