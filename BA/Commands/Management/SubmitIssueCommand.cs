using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.IssueReporter.Services;
using BA.IssueReporter.Views;
using System;
using System.IO;

namespace BA.IssueReporter.Commands;

[Transaction(TransactionMode.Manual)]
public class SubmitIssueCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            UIApplication uiApp = commandData.Application;
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
        if (doc == null)
            return "No active project";

        if (!string.IsNullOrWhiteSpace(doc.PathName))
            return Path.GetFileName(doc.PathName);

        if (!string.IsNullOrWhiteSpace(doc.Title))
            return doc.Title;

        return "Unsaved project";
    }
}
