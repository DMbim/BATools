// FILE: BA_Tools/Application/Ribbon/UtilitiesPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Settings;
using BA.Classification;
using BA.Commands;
using BA.Commands.Management;
using BA.Commands.Standards;
using BA.Commands.Views.ScopeBoxes;
using BA.IssueReporter.Commands;
using BA.Markup.Commands;
using BA.Ribbon;
using BA.RoomClassification;
using BA.Subcategories.Commands;
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
            #region Excel
            var pdpdExp = panel.AddPulldownButton<ExportScheduleCommand>(
                "Export/ImportToExcel", "Export/Import\nSchedule",
                "Export the selected schedule to an Excel file.",
                IconResources.ExpIExc16, IconResources.ExpIExc32);

            pdpdExp.AddPushButton<ExportScheduleCommand>(
                "ExportToExcel", "Export\nSchedule",
                "Export the selected schedule to an Excel file.",
                IconResources.ExpExc16, IconResources.ExpExc32);

            pdpdExp.AddPushButton<ImportScheduleCommand>(
                "ImportToExcel", "Import\nSchedule",
                "Import the selected schedule from an Excel file.",
                IconResources.ImpExc16, IconResources.ImpExc32);
            #endregion

            #region Sheet Date+Revision
            panel.AddPushButton<Cmd_SheetDateAndRevision>(
                "SheetDateRevision", "Sheet\nDate + Rev",
                "Update the Issue Date and/or Revision on selected sheets.",
                IconResources.SheetRevision16, IconResources.SheetRevision32);
            #endregion

            panel.AddPushButton<BA.UI.Commands.Management.Cmd_Settings>(
                "Settings", "Project\nSettings",
                "General Plugin Settings",
                IconResources.SettingsP16, IconResources.SettingsP32);

            panel.AddPushButton<Cmd_GetVolume>(
                "GetVolume", "Get\nVolume",
                "For categories where Volume is not a native built-in parameter, calculates and writes the element volume into a prepared parameter.",
                IconResources.GetVolume_16, IconResources.GetVolume_32);

            // TODO: IconResources.SettingsP16/32 reused as a placeholder — swap for a
            // dedicated update/refresh icon when one is added to IconResources.
            panel.AddPushButton<Cmd_CheckForUpdates>(
                "CheckForUpdates", "Check for\nUpdates",
                "Check GitHub for a newer BA Tools release and update now if one is available.",
                IconResources.SettingsP16, IconResources.SettingsP32);
        }
    }
}