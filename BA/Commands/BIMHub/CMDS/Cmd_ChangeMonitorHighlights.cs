using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using BA.Core;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ChangeMonitorClearHighlights : ExternalCommand
    {
        public override void Execute()
        {
            try
            {
                var doc = Document;
                if (doc == null)
                {
                    TaskDialog.Show("Change Monitor", "No active document is available.");
                    return;
                }

                using (var t = new Transaction(doc, "Clear Change Monitor Highlights"))
                {
                    t.Start();
                    Highlighter.ClearAllOverrides(doc);
                    t.Commit();
                }

                TaskDialog.Show("Change Monitor", "Cleared element overrides applied by Change Monitor.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Change Monitor - Error", ex.ToString());
            }
        }
    }
}
