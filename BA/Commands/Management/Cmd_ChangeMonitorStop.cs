using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Nice3point.Revit.Toolkit.External;
using BA.Core;
using BA.UI;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ChangeMonitorStop : ExternalCommand
    {
        public override void Execute()
        {
            try
            {
                if (!ChangeMonitorService.IsRunning)
                {
                    TaskDialog.Show("Change Monitor", "Monitoring is not currently running.");
                    return;
                }

                ChangeReport report = ChangeMonitorService.Stop();
                if (report == null || report.Records == null || report.Records.Count == 0)
                {
                    TaskDialog.Show("Change Monitor", "No changes were recorded.");
                    return;
                }

                string summary = report.GetSummaryText();

                var td = new TaskDialog("Change Monitor Report")
                {
                    MainInstruction = "Monitoring stopped. A report has been created.",
                    MainContent = summary + "\n\nChoose an action below or click OK to close.",
                    CommonButtons = TaskDialogCommonButtons.Ok,
                    AllowCancellation = true
                };

                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Export CSV report…");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Highlight changed elements in recorded views");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Open Live Review Window");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Tag BA_Change = Yes (if available) + add view filter");

                var choice = td.Show();

                if (choice == TaskDialogResult.CommandLink1)
                {
                    ExportCsv(report);
                }
                else if (choice == TaskDialogResult.CommandLink2)
                {
                    using (var t = new Transaction(report.Document, "Highlight Changed Elements"))
                    {
                        t.Start();
                        Highlighter.ApplyPerViewOverrides(report);
                        t.Commit();
                    }
                    TaskDialog.Show("Change Monitor", "Highlight applied.");
                }
                else if (choice == TaskDialogResult.CommandLink3)
                {
                    LiveReviewHost.ShowOrActivate(UiApplication, report);
                }
                else if (choice == TaskDialogResult.CommandLink4)
                {
                    Highlighter.ApplyBAChangeTags(report);
                    TaskDialog.Show(
                        "Change Monitor",
                        "Attempted to set BA_Change = Yes and create per-view filter.\n" +
                        "(Requires the 'BA_Change' Yes/No instance parameter to already exist and be bound.)");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Change Monitor - Error", ex.ToString());
            }
        }

        private static void ExportCsv(ChangeReport report)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"RevitChanges_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            bool? res = dialog.ShowDialog();
            if (res != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,ChangeType,ElementId,Category,ViewName,ViewId,Username,Transactions,Parameter,OldValue,NewValue");

            foreach (var r in report.Records)
            {
                string elId = r.ElementId != null ? r.ElementId.ToString() : "";
                string vId = r.ViewId != null && r.ViewId != ElementId.InvalidElementId
                    ? r.ViewId.ToString()
                    : "";

                if (r.ParameterChanges == null || r.ParameterChanges.Count == 0)
                {
                    sb.AppendLine($"{r.When:O},{r.ChangeType},{Csv(elId)},{Csv(r.Category)},{Csv(r.ViewName)},{Csv(vId)},{Csv(r.Username)},{Csv(r.TransactionNames)},,,");
                }
                else
                {
                    foreach (var p in r.ParameterChanges)
                    {
                        sb.AppendLine(
                            $"{r.When:O},{r.ChangeType},{Csv(elId)},{Csv(r.Category)},{Csv(r.ViewName)},{Csv(vId)},{Csv(r.Username)},{Csv(r.TransactionNames)},{Csv(p.ParamName)},{Csv(p.OldValue)},{Csv(p.NewValue)}");
                    }
                }
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            TaskDialog.Show("Change Monitor", $"Exported: {dialog.FileName}");

            static string Csv(string s) =>
                s == null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
