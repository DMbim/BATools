using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using BA.Settings;
using BA.UI.Helpers;
using BA.UI.Sheets;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
namespace BA.Commands.Management
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_SheetDateAndRevision_Settings : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                var settings = DateToolSettings.LoadWithMigration();

                var setup = new DateSetupWindow(commandData, settings);
                RevitWindowHelper.SetOwnerToRevit(setup, uiapp);
                if (setup.ShowDialog() != true)
                    return Result.Cancelled;

                settings.Save();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA \u2013 Error", ex.ToString());
                return Result.Failed;
            }
        }
    }
}