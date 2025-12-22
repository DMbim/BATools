using System;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Sheets;
using BA.Settings;
using BA.UI.Helpers;
using BA.UI.Sheets;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Commands.Management
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_SheetDateAndRevision : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;

            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            try
            {
                // Load BA settings (auto-migrates from old bimBA paths via AppSettingsBase)
                var settings = DateToolSettings.LoadWithMigration();

                // SHIFT => setup
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    var setup = new DateSetupWindow(commandData, settings);
                    RevitWindowHelper.SetOwner(setup, uiapp);

                    if (setup.ShowDialog() == true)
                    {
                        settings.Save();
                        return Result.Succeeded;
                    }

                    return Result.Cancelled;
                }

                // Normal run requires configured settings
                if (string.IsNullOrWhiteSpace(settings.SelectedFormat) ||
                    string.IsNullOrWhiteSpace(settings.SelectedDateParam) ||
                    string.IsNullOrWhiteSpace(settings.SelectedRevParam))
                {
                    TaskDialog.Show("BA",
                        "No saved settings found.\nHold Shift while launching the command to configure it first.");
                    return Result.Cancelled;
                }

                // UI: choose sheets + what to update
                var dlg = new DateSheetsWindow(commandData, settings);
                RevitWindowHelper.SetOwner(dlg, uiapp);

                if (dlg.ShowDialog() != true)
                    return Result.Cancelled;

                var selectedRows = dlg.GetSelectedRows();
                if (selectedRows.Count == 0)
                {
                    TaskDialog.Show("BA", "No sheets selected for update.");
                    return Result.Cancelled;
                }

                // Apply updates
                SheetUpdateReport report;

                using (var t = new Transaction(doc, "BA – Update Sheet Date/Revision"))
                {
                    t.Start();

                    SheetUpdateService.Apply(doc, selectedRows, settings, out report);

                    t.Commit();
                }

                TaskDialog.Show("BA – Sheet Update", report.ToDialogText());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA – Error", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
