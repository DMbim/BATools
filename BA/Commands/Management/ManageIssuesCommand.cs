using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.IssueReporter.Services;
using BA.IssueReporter.Views;
using System;
using System.Linq;

namespace BA.IssueReporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ManageIssuesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                string user = uiApp.Application.Username;

                var settingsService = new SettingsService();
                var settings = settingsService.Load();

                bool managersConfigured = settings.ManagerUsers.Any(x => !string.IsNullOrWhiteSpace(x));
                bool isManager = !managersConfigured || settings.ManagerUsers.Any(x =>
                    string.Equals(x.Trim(), user, StringComparison.OrdinalIgnoreCase));

                if (!isManager)
                {   
                    TaskDialog.Show("BA Issue Reporter", "You are not listed as an issue manager.");
                    return Result.Cancelled;
                }

                var window = new ManageIssuesWindow(settings, user);
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("BA Issue Reporter", ex.Message);
                return Result.Failed;
            }
        }
    }
}