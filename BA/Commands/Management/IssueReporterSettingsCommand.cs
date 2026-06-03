using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Serv = BA.IssueReporter.Services;
using VV = BA.IssueReporter.Views;
using System;

namespace BA.IssueReporter.Commands;

[Transaction(TransactionMode.Manual)]
public class IssueReporterSettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var settingsService = new Serv.SettingsService();
            var settings = settingsService.Load();

            var window = new VV.IssueReporterSettingsWindow(settings, settingsService.SettingsPath);
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
