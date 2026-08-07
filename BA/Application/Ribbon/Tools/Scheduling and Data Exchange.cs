// FILE: BA_Tools/BAApplication/Ribbon/SchedulingPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Settings;
using BA.BIM.Commands.Anno;
using BA.Classification;
using BA.Commands;
using BA.Commands.Export;
using BA.Commands.Management;
using BA.Commands.Standards;
using BA.Commands.Views.ScopeBoxes;
using BA.IssueReporter.Commands;
using BA.Markup.Commands;
using BA.Ribbon;
using BA.RoomClassification;
using BA.Subcategories.Commands;
using BA.UI.Commands.Management;
using BA.UI.Helpers;
using BA.UI.Parameters;
using BA.UI.Settings;
using BA.UI.Views;
using BA_Tools.ScheduleExporter.Commands;
using BATools.ParamCopy.Commands;

namespace BA.BAApplication.Ribbon
{
    internal static class SchedulingPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            #region Copy (moved from UtilitiesPanelFactory, now retired)
            // Schedule Copy Value moved out to RoomsPanelFactory, stacked with Room
            // Classification Import and Finishes. Copy Type Parameters stays here untouched.



            #endregion

            #region Export/Import Schedule + Sheet Date/Revision + PDF/DWG Export (stacked, moved from UtilitiesPanelFactory)
            var (pdpdExp, pdSheetDateRev, pdExport) = panel.AddStackedPulldownButtons(
                "Exp/Imp ToExcel", "Exp/Imp\nSchedule",
                "Export the selected schedule to an Excel file.",
                IconResources.ExpIExc16, IconResources.ExpIExc32,

                "SheetDateRevisionPulldown", "Sheet\nDate + Rev",
                "Update the Issue Date and/or Revision on selected sheets.",
                IconResources.SheetRevision16, IconResources.SheetRevision32,

                "Export", "PDF/DWG\nExport",
                "Export sheets to PDF or DWG with custom naming and scheduling.",
                IconResources.PDF16, IconResources.PDF32);

            pdpdExp.AddPushButton<ExportScheduleCommand>(
                "ExportToExcel", "Export\nSchedule",
                "Export the selected schedule to an Excel file.",
                IconResources.ExpExc16, IconResources.ExpExc32);

            pdpdExp.AddPushButton<ImportScheduleCommand>(
                "ImportToExcel", "Import\nSchedule",
                "Import the selected schedule from an Excel file.",
                IconResources.ImpExc16, IconResources.ImpExc32);

            pdSheetDateRev.AddPushButton<Cmd_SheetDateAndRevision>(
                "SheetDateRevision", "Update\nSheets",
                "Update the Issue Date and/or Revision on selected sheets.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);

            pdSheetDateRev.AddPushButton<Cmd_SheetDateAndRevision_Settings>(
                "SheetDateRevisionSettings", "Settings",
                "Configure the date parameter, revision parameter, and date format used by Sheet Date + Rev.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);

            pdExport.AddPushButton<OpenExportSettingsCommand>(
                "OpenExportSettings", "Export\nSettings",
                "Configure PDF and DWG export jobs: naming, sheet sets, schedule.",
                IconResources.PDF16, IconResources.PDF32);

            pdExport.AddPushButton<RunExportJobCommand>(
                "RunExportJobs", "Run\nExports Now",
                "Run every configured export job for this document immediately.",
                IconResources.PDF16, IconResources.PDF32);
            #endregion

        }
    }
}