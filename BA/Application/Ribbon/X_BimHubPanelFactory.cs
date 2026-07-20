// File: BA_Tools/BAApplication/Ribbon/BimHubPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Overhead;
using BA.BIM.Commands.Anno;
using BA.Commands;
using BA.Commands.Anno;
using BA.Commands.Standards;
using BA.KeyplanGrid;
using BA.QA.FamilyVersioning.Commands;
using BA.Ribbon;
using BA.UI.BimHub.Commands;
using BATools.ParamCopy.Commands;
using BATools.SelectionManager.Commands;
using Nice3point.Revit.Extensions;
namespace BA.BAApplication.Ribbon
{
    public static class BimHubPanelFactory
    {
        public static void Build(RibbonPanel panel)
        {

          
            panel.AddPushButton<OpenBimHubCommand>(
                "ContentBrowser", "Content\nBrowser",
                "Browse the BA content library and place family types directly into the model.",
                IconResources.BIM16, IconResources.BIM32);
            panel.AddPushButton<OpenSelectionManagerCommand>(
                "OpenSelectionManager",
                "Selection\nManager",
                "Open the Selection Manager — a tool for saving, loading, and managing selection sets.",
                IconResources.SelectionManager16, IconResources.SelectionManager32);

            panel.AddPushButton<OpenRecentsCommand>(
                "OpenRecents",
                "Recents",
                "Open the Recents — a tool for quickly accessing recent selection sets.",
                IconResources.SelectionManager16, IconResources.SelectionManager32);

            panel.AddPushButton<Cmd_SubcategoryAuditor>(
                "SubcategorySken", "Subcategory\nSken",
                "Skens families and collects subcategories.",
                IconResources.SubCatSK16, IconResources.SubCatSK32);

            panel.AddPushButton<Cmd_FamilyVersioningSetup>(
                "VersionSetup", "Version\nSetUp",
                "Set up family versioning.",
                IconResources.FamilyParVer_16, IconResources.FamilyParVer_32);

            panel.AddPushButton<Cmd_FamilyVersioningDashboard>(
                "VersionDashboard", "Version\nDashboard",
                "Open the family versioning dashboard.",
                IconResources.FamilyParVer_16, IconResources.FamilyParVer_32);

            panel.AddPushButton<CmdPublishTypeData>(
                "PublishTypeData", "Publish\nType Data",
                "Publish type data for families.",
                IconResources.FamilyVer_16, IconResources.FamilyVer_32);
            panel.AddPushButton<CmdOpenLedgerSettings>(
                "OpenLedgerSettings", "Ledger\nSettings",
                "Open the ledger settings.",
                IconResources.FamilyVer_16, IconResources.FamilyVer_32);
        }
    }
}