using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.IssueReporter.Services;
using BA.IssueReporter.Views;
using System;
using System.IO;
using System.Linq;

namespace BA.IssueReporter.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class SubmitIssueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            try
            {
                Document doc = uiApp.ActiveUIDocument?.Document;

                var settingsService = new SettingsService();
                var settings = settingsService.Load();

                string user = uiApp.Application.Username;
                string projectName = GetProjectName(doc);
                string projectPath = doc?.PathName ?? string.Empty;

                var window = new SubmitIssueWindow(settings, user, projectName, projectPath);
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

        private static string GetProjectName(Document doc)
        {
            if (doc == null) return "No active project";
            if (!string.IsNullOrWhiteSpace(doc.PathName)) return Path.GetFileName(doc.PathName);
            if (!string.IsNullOrWhiteSpace(doc.Title)) return doc.Title;
            return "Unsaved project";
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ManageIssuesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            try
            {
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

    [Transaction(TransactionMode.Manual)]
    public class IssueReporterSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            try
            {
                var settingsService = new SettingsService();
                var settings = settingsService.Load();

                var window = new IssueReporterSettingsWindow(settings, settingsService.SettingsPath);
                if (window.ShowDialog() == true)
                    settingsService.Save(window.Settings);

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
