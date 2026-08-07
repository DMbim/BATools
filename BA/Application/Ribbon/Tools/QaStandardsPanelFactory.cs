// FILE: BA_Tools/BAApplication/Ribbon/QaStandardsPanelFactory.cs
using Autodesk.Revit.UI;
using BA.Classification;
using BA.Commands;
using BA.Commands.Management;
using BA.Commands.Standards;
using BA.IssueReporter.Commands;
using BA.Ribbon;
using BA.UI.Commands.Anno;
using BA.UI.Commands.Management;

namespace BA.BAApplication.Ribbon
{
    internal static class QaStandardsPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            #region Issues / Classify Types / Change Monitor (stacked)
            // Issues and Change Monitor each keep their own pulldown of sub-items, populated
            // below on the returned PulldownButton instances. Classify Types stays a single
            // plain command in the middle slot. Room Classification Import used to sit next to
            // Classify Types here, it now lives in RoomsPanelFactory stacked with Finishes and
            // Schedule Copy Value.
            var (pdIssues, classifyTypesBtn, pdChangeMon) = panel.AddStackedPulldownPushPulldown<Cmd_ClassifyElements>(
                "Issues", "Issues",
                "Manage project issues.",
                IconResources.Issues_16,

                "ClassifyTypes", "Classify\nTypes",
                "Classify element types against a rule set loaded from an Excel file.",
                IconResources.Classify16,

                "ChangeMonitor", "Change\nMonitor",
                "Track element additions, deletions, moves and parameter edits.",
                IconResources.ChangeMonitor16
                );

            pdIssues.AddPushButton<ManageIssuesCommand>(
                "ManageIssues", "Manage\nIssues",
                "Manage existing issues.",
                IconResources.ManageIssues_16, IconResources.ManageIssues_32);

            pdIssues.AddPushButton<SubmitIssueCommand>(
                "SubmitIssue", "Submit\nIssue",
                "Submit a new issue.",
                IconResources.SubmitIssue_16, IconResources.SubmitIssue_32);

            pdIssues.AddPushButton<IssueReporterSettingsCommand>(
                "IssueReporterSettings", "Settings",
                "Configure issue reporter settings.",
                IconResources.Settings_16, IconResources.Settings_32);

            pdChangeMon.AddPushButton<Cmd_ChangeMonitorStart>(
                "ChangeMonitorStart", "Start",
                "Start monitoring the active document for changes.",
                IconResources.ChangeMonitorS16, IconResources.ChangeMonitorS32);

            pdChangeMon.AddPushButton<Cmd_ChangeMonitorStop>(
                "ChangeMonitorStop", "Stop & Export",
                "Stop monitoring and export the change report.",
                IconResources.ChangeMonitorStop16, IconResources.ChangeMonitorStop32);

            pdChangeMon.AddPushButton<Cmd_ChangeMonitorLive>(
                "ChangeMonitorLive", "Live\nReview",
                "Open the live change review window.",
                IconResources.ChangeMonitorLive16, IconResources.ChangeMonitorLive32);

            pdChangeMon.AddPushButton<Cmd_ChangeMonitorClearHighlights>(
                "ChangeMonitorClear", "Clear\nHighlights",
                "Remove all graphic overrides applied by Change Monitor.",
                IconResources.ChangeMonitorClear16, IconResources.ChangeMonitorClear32);
            #endregion
        }
    }
}