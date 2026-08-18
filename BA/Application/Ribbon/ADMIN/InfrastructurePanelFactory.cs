// FILE: BA_Tools/BAApplication/Ribbon/InfrastructurePanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Settings;
using BA.Commands;
using BA.Commands.Diagnostics;
using BA.Commands.Export;
using BA.Commands.Management;
using BA.Ribbon;
using BA.UI.BimHub.Commands;
using BA.UI.Commands.Management;
using BATools.SelectionManager.Commands;

namespace BA.BAApplication.Ribbon
{
    internal static class InfrastructurePanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            // "Content Browser" (OpenBimHubCommand) intentionally removed from this panel.
            // Per your call, the content browser stays as a single entry point on the
            // BA_Tools tab, FamiliesPanelFactory, "Load Families" / Cmd_OpenContentBrowserCommand.
            // This assumes OpenBimHubCommand and Cmd_OpenContentBrowserCommand were the same
            // feature wired twice, if they're genuinely different windows, tell me and I'll
            // reinstate this one under a distinct label instead of deleting it.

            panel.AddPushButton<OpenSelectionManagerCommand>(
                "OpenSelectionManager",
                "Selection\nManager",
                "Open the Selection Manager, a tool for saving, loading, and managing selection sets.",
                IconResources.SelectionManager16, IconResources.SelectionManager32);

            panel.AddPushButton<OpenRecentsCommand>(
                "OpenRecents",
                "Recents",
                "Open the Recents, a tool for quickly accessing recent selection sets.",
                IconResources.SelectionManager16, IconResources.SelectionManager32);

            var (settings, updates) = panel.AddStackedButtons<Cmd_Settings, Cmd_CheckForUpdates>(
                "Settings", "Project\nSettings",
                "CheckForUpdates", "Check for\nUpdates",
                IconResources.ArAnno16, IconResources.ArAnno16,
                "General Plugin Settings",
                "Check GitHub for a newer BA Tools release and update now if one is available.");

            panel.AddPushButton<OpenFamilyExportCommand>(
                 "OpenFamilyExport", "Family\nExports",
                 "Open the Family Export dialog to configure and run family exports.",
                 IconResources.SheetRevision16, IconResources.SheetRevision32);

            panel.AddPushButton<OpenBookletCommand>(
                "OpenBooklet", "Booklet\nExport",
                "Open the Booklet Export dialog to configure and run booklet exports.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);

            panel.AddPushButton<OpenBimHubCommand>(
                "OpenBimHub", "OpenBimHub\nBimHub",
                "Open the Content Browser to browse and load content from the BIM Hub.",
                IconResources.ContentBrowser16, IconResources.ContentBrowser32);    

            panel.AddPushButton<Cmd_ViewTemplateTransfer>(
                "ViewTemplateTransfer", "View Template\nTransfer",
                "Transfer view templates from one view to another.",
                IconResources.ViewTemplate16, IconResources.ViewTemplate32);

            panel.AddPushButton<Cmd_OpenWarningsDashboard>(
                  "OpenWarningsDashboard", "Warnings\nDashboard",
                  "Open the Smart Warnings Dashboard to review, group, zoom to, and auto-resolve model warnings.",
                  IconResources.Warn16, IconResources.Warn32);

            panel.AddPushButton<Cmd_DiagnoseFailureDefinitions>(
                  "DiagnoseFailureDefinitions", "Diagnose\nFailures",
                  "Open the Diagnose Failure Definitions tool to analyze and resolve Revit failure definitions.",
                  IconResources.Warn16, IconResources.Warn32);

        }
    }
}