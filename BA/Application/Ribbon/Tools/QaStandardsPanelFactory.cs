// FILE: BA_Tools/BAApplication/Ribbon/QaStandardsPanelFactory.cs
using Autodesk.Revit.UI;
using BA.CadPurge.Commands;
using BA.Classification;
using BA.Commands;
using BA.Commands.CurveToElement;
using BA.Commands.Management;
using BA.Commands.Standards;
using BA.IssueReporter.Commands;
using BA.Ribbon;
using BA.Subcategories.Commands;
using BA.UI.Commands.Anno;
using BA.UI.Commands.Management;
using BA.VisibilityDiagnostics.Commands;

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




            var pdIssues=panel.AddPulldownButton<ManageIssuesCommand>(
                "Issues", "Issues",
                "Manage project issues.",
                IconResources.Issues_16, IconResources.Issues_32);

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


            #endregion






            PulldownButton qa = panel.AddPulldownButton<Cmd_WindowOrientation>(
                  "QA", "BA Quality\nAssurance",
                    "Colletion of Tools to help maintain the office standards",
                IconResources.ta16, IconResources.ta32);

            qa.AddPushButton<Cmd_OpenWarningsDashboard>(
                "OpenWarningsDashboard", "Open\nDashboard",
                "Open the BA Quality Assurance dashboard.",
                IconResources.Warn16, IconResources.Warn32);

            qa.AddPushButton<VisibilityDiagnosticCommand>(
                "VisibilityDiagnostics", "Visibility\nDiagnostics",
                "Diagnose and fix visibility issues in the active view.",
                IconResources.Visb16, IconResources.Visb32);

            qa.AddPushButton<CadPurgeCommand>(
                "CadPurge", "CAD\nPurge",
                "Remove unused CAD imports and linked CAD files.",
                IconResources.CADPurge16, IconResources.CADPurge32);

            qa.AddSeparator();

            qa.AddPushButton<Cmd_ChangeMonitorStart>(
                "ChangeMonitorStart", "TrackChanges \n Start",
                "Start monitoring the active document for changes.",
                IconResources.ChangeMonitorS16, IconResources.ChangeMonitorS32);

            qa.AddPushButton<Cmd_ChangeMonitorStop>(
                "ChangeMonitorStop", "T.Ch. \n Stop & Export",
                "Stop monitoring and export the change report.",
                IconResources.ChangeMonitorStop16, IconResources.ChangeMonitorStop32);

            qa.AddPushButton<Cmd_ChangeMonitorLive>(
                "ChangeMonitorLive", "T.Ch. \n Live\nReview",
                "Open the live change review window.",
                IconResources.ChangeMonitorLive16, IconResources.ChangeMonitorLive32);

            qa.AddPushButton<Cmd_ChangeMonitorClearHighlights>(
                "ChangeMonitorClear", "T.Ch. \n Clear\nHighlights",
                "Remove all graphic overrides applied by Change Monitor.",
                IconResources.ChangeMonitorClear16, IconResources.ChangeMonitorClear32);

            qa.AddSeparator();

            qa.AddPushButton<Cmd_ClassifyElements>(
                "ClassifyElements", "Classify\nElements",
                "Classify element types against a rule set loaded from an Excel file.",
                IconResources.Classify16, IconResources.Classify32);

            qa.AddPushButton<SubcategoryManagerCommand>(
                  "SubcategoryAuditor", "Subcategory\nAuditor",
                  "Audit family subcategories against BA naming conventions.",
                  IconResources.SubCat16, IconResources.SubCat32);

            panel.AddPushButton<Cmd_Settings>(
                "Settings", "Settings",
                "Configure BA Quality Assurance settings.",
                IconResources.BATools_16, IconResources.BATools_32);
        }
    }
}