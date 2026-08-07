// FILE: BA_Tools/BAApplication/Ribbon/FamilyVersioningPanelFactory.cs
using Autodesk.Revit.UI;
using BA.Commands;
using BA.Commands.Standards;
using BA.QA.FamilyVersioning.Commands;
using BA.Ribbon;

namespace BA.BAApplication.Ribbon
{
    internal static class FamilyVersioningPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
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
            #region Subcategory Audit, project-wide (moved and renamed from
            // BimHubPanelFactory, now retired; was "SubcategorySken" / "Subcategory Sken".
            // Renamed so it reads clearly as the project-wide scan, as opposed to
            // FamiliesPanelFactory's "Subcategory Manager" which works inside one open
            // family document.)
            panel.AddPushButton<Cmd_SubcategoryAuditor>(
                "SubcategoryAuditProject", "Subcategory\nAudit (Project)",
                "Scan all loaded families in the project and audit their subcategories against BA naming conventions.",
                IconResources.Sub16, IconResources.Sub32);
            #endregion
        }
    }
}