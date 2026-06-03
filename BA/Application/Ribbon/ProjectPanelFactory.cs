using Autodesk.Revit.UI;
using BA.Commands;
using BA.Commands.Management;
using BA.Commands.TextHub;
using BA.IssueReporter.Commands;
using BA.Ribbon;
using BA_Tools.ScheduleExporter.Commands;

namespace BA.BAApplication.Ribbon
{
    internal static class ProjectPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            panel.AddPushButton<Cmd_ProjectToolsHub>(
                "ProjectToolsHub", "Project\nTools Hub",
                "Open the Project Tools Hub — a consolidated window for project-level management tasks.",
                IconResources.Menu16, IconResources.Menu32);

            var pdMon = panel.AddPulldownButton<Cmd_ChangeMonitorStart>(
                "ChangeMonitor", "Change\nMonitor",
                "Track element additions, deletions, moves and parameter edits.",
                IconResources.ChangeMonitor16, IconResources.ChangeMonitor32);

            pdMon.AddPushButton<Cmd_ChangeMonitorStart>(
                "ChangeMonitorStart", "Start",
                "Start monitoring the active document for changes.",
                IconResources.ChangeMonitorS16, IconResources.ChangeMonitorS32);

            pdMon.AddPushButton<Cmd_ChangeMonitorStop>(
                "ChangeMonitorStop", "Stop & Export",
                "Stop monitoring and export the change report.",
                IconResources.ChangeMonitorStop16, IconResources.ChangeMonitorStop32);

            pdMon.AddPushButton<Cmd_ChangeMonitorLive>(
                "ChangeMonitorLive", "Live\nReview",
                "Open the live change review window.",
                IconResources.ChangeMonitorLive16, IconResources.ChangeMonitorLive32);

            pdMon.AddPushButton<Cmd_ChangeMonitorClearHighlights>(
                "ChangeMonitorClear", "Clear\nHighlights",
                "Remove all graphic overrides applied by Change Monitor.",
                IconResources.ChangeMonitorClear16, IconResources.ChangeMonitorClear32);

            panel.AddPushButton<Cmd_SheetDateAndRevision>(
                "SheetDateRevision", "Sheet\nDate + Rev",
                "Update the Issue Date and/or Revision on selected sheets.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);



            var pdpdExp = panel.AddPulldownButton<ExportScheduleCommand>(
                "Export/ImportToExcel", "Export/Import\nSchedule",
                "Export the selected schedule to an Excel file.",
                IconResources.ExpIExc16, IconResources.ExpIExc32);

            pdpdExp.AddPushButton<ExportScheduleCommand>(
                "ExportToExcel", "Expor\nSchedule",
                "Export the selected schedule to an Excel file.",
                IconResources.ExpExc16, IconResources.ExpExc32);

            pdpdExp.AddPushButton<ImportScheduleCommand>(
                "ImportToExcel", "Import\nSchedule",
                "Import the selected schedule from an Excel file.",
                IconResources.ImpExc16, IconResources.ImpExc32);

            var pdpIssue = panel.AddPulldownButton<ManageIssuesCommand>(
                "Issues", "Issues",
                "Manage project issues.",
                IconResources.Issues_16, IconResources.Issues_32);

            pdpIssue.AddPushButton<ManageIssuesCommand>(
                "ManageIssues", "Manage\nIssues",
                "Manage existing issues.",
                IconResources.ManageIssues_16, IconResources.ManageIssues_32);

            pdpIssue.AddPushButton<SubmitIssueCommand>(
                "SubmitIssue", "Submit\nIssue",
                "Submit a new issue.",
                IconResources.SubmitIssue_16, IconResources.SubmitIssue_32);

            pdpIssue.AddPushButton<IssueReporterSettingsCommand>(
                "IssueReporterSettings", "Settings",
                "Configure issue reporter settings.",
                IconResources.Settings_16, IconResources.Settings_32);

        }
    }
}