using Autodesk.Revit.UI;
using BA.Commands;
using BA.Commands.Management;
using BA.IssueReporter.Commands;
using BA.Ribbon;
using BA.UI.Commands.Anno;
using BA.UI.Commands.Management;
using BA_Tools.ScheduleExporter.Commands;
using BATools.Zoom.Commands;
namespace BA.BAApplication.Ribbon
{
    internal static class ProjectPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {

            #region Change Monitor
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
            #endregion

         
            panel.AddPushButton<Cmd_RevitParameters>(
                "ParameterManager", "Manage\nParameters",
                "View and manage Parameters in the active document.",
                IconResources.RevPar16, IconResources.RevPar32);

            #region Issues
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
            #endregion
            #region Zoom
            var Zoom = panel.AddPulldownButton<Cmd_ZoomToRoom>(
                "Zoom", "Zoom\n->",
                "Zoom to rooms in the model.",
                IconResources.Zoom16, IconResources.Zoom32);

            Zoom.AddPushButton<Cmd_ZoomToRoom>(
                "ZoomToRoomLocal", "ToRoom\nLocal",
                "Zoom to rooms in the active model.",
                IconResources.ZoomLo16, IconResources.ZoomLo32);

            Zoom.AddPushButton<Cmd_ZoomToRoom_Link>(
                "ZoomToRoomLink", "ToRoom\nLinked",
                "Zoom to rooms in a linked model.",
                IconResources.ZoomL16, IconResources.ZoomL32);

            Zoom.AddPushButton<Cmd_ZoomToSelectedElement>(
                "ZoomToSelectedElement", "To Selected\nElement",
                "Zoom to the selected element in the model.",
                IconResources.ZoomE16, IconResources.ZoomE32);
            #endregion


        }
    }
}