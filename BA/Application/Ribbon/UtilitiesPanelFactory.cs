// FILE: BA_Tools/Application/Ribbon/UtilitiesPanelFactory.cs
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
using System.Windows;

namespace BA.BAApplication.Ribbon
{
    internal static class UtilitiesPanelFactory
    {
        public static void Build(RibbonPanel panel, UIApplication uiApp, Autodesk.Revit.ApplicationServices.Application revit)
        {
            if (uiApp is null) throw new ArgumentNullException(nameof(uiApp));
            if (revit is null) throw new ArgumentNullException(nameof(revit));
            Build(panel);
        }
        internal static void Build(RibbonPanel panel)
        {
            #region Copy
            var pdCopy = panel.AddPulldownButton<Cmd_ScheduleSync>(
                "Copy", "\nCopy",
                "Copying tools",
                IconResources.Copy16, IconResources.Copy32);

            pdCopy.AddPushButton<Cmd_ScheduleSync>(
                "ScheduleCopyValue", "Schedule\nCopy Value",
                "Synchronize schedule data across multiple schedules based on matching parameters.",
                IconResources.ScheduleSync16, IconResources.ScheduleSync32);

            pdCopy.AddPushButton<ParamCopyCommand>(
                "CopyTypeParams", "Copy Type\nParameters",
                "Copy type parameters from a source element to one or more target elements.",
                IconResources.CopyP16, IconResources.CopyP32);
            #endregion
            #region Classification
            var pdClassification = panel.AddPulldownButton<Cmd_ClassifyElements>(
                "Classification", "Classification",
                "Tools for classification.",
                IconResources.Classify16, IconResources.Classify32);

            pdClassification.AddPushButton<Cmd_ClassifyElements>(
                "ClassifyTypes", "Classify\nTypes",
                "Classify element types against a rule set loaded from an Excel file.",
                IconResources.Classify16, IconResources.Classify32);

            pdClassification.AddPushButton<RoomClassificationImportCommand>(
                "RoomClassificationImport", "Room\nClassification",
                "Import room program data (type, department, function, code, group) from an Excel matrix.",
                IconResources.ClsRoom16, IconResources.ClsRoom32);
            #endregion

            #region Excel / Sheet Date+Revision (stacked)
            var (pdpdExp, pdSheetDateRev) = panel.AddStackedPulldownButtons(
                "Export/ImportToExcel", "Export/Import\nSchedule",
                "Export the selected schedule to an Excel file.",
                IconResources.ExpIExc16, IconResources.ExpIExc32,

                "SheetDateRevisionPulldown", "Sheet\nDate + Rev",
                "Update the Issue Date and/or Revision on selected sheets.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);

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
            #endregion

            var (settings, updates) = panel.AddStackedButtons<Cmd_Settings, Cmd_CheckForUpdates>(
                "Settings", "Project\nSettings",
                "CheckForUpdates", "Check for\nUpdates",
                IconResources.ArAnno16, IconResources.ArAnno16,
                "General Plugin Settings",
                "Check GitHub for a newer BA Tools release and update now if one is available.");

            // Icons borrowed from SheetRevision as a placeholder, this feature
            // doesn't have its own icon asset yet, swap these out once it does.
            var pdExport = panel.AddPulldownButton<OpenExportSettingsCommand>(
                "Export", "PDF/DWG\nExport",
                "Export sheets to PDF or DWG with custom naming and scheduling.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);

            pdExport.AddPushButton<OpenExportSettingsCommand>(
                "OpenExportSettings", "Export\nSettings",
                "Configure PDF and DWG export jobs: naming, sheet sets, schedule.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);

            pdExport.AddPushButton<RunExportJobCommand>(
                "RunExportJobs", "Run\nExports Now",
                "Run every configured export job for this document immediately.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);

            // Hold off wiring these two until the revision-parameter question above
            // is settled, they may not be needed at all.
            // pdExport.AddPushButton<BumpRevisionCommand>(...)
            // pdExport.AddPushButton<BindRevisionParameterCommand>(...)

            // Cmd_GetVolume MOVED to FamiliesPanelFactory (Family + Content tab), merged
            // with "Family from Geometry" -- pending that file to complete correctly.
        }
    }
}