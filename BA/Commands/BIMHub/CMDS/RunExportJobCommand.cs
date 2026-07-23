using System;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Export.Services;
using BA.Settings.Export;

namespace BA.Commands.Export
{
    /// <summary>
    /// Manually runs every currently configured export job for the active
    /// document, regardless of ScheduleEnabled. Useful for testing job
    /// configuration before the settings UI ships. Once that UI exists this
    /// should be replaced with a per-job "Run now" button that calls
    /// ExportJobRunner.RunJob directly for a single selected job instead of
    /// iterating everything.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunExportJobCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            var doc = uiApp.ActiveUIDocument?.Document;

            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            try
            {
                var settingsRoot = ExportSettingsStore.Load(doc);

                if (settingsRoot.Jobs.Count == 0)
                {
                    TaskDialog.Show("BA Tools - Run Export Jobs", "No export jobs are configured for this project yet.");
                    return Result.Succeeded;
                }

                var summary = new StringBuilder();
                var now = DateTime.Now;

                foreach (var job in settingsRoot.Jobs)
                {
                    var result = ExportJobRunner.RunJob(doc, job, now);

                    summary.AppendLine(result.HasJobLevelError
                        ? $"{job.JobName}: FAILED - {result.JobLevelError}"
                        : $"{job.JobName}: {result.SuccessCount} succeeded, {result.FailureCount} failed");
                }

                TaskDialog.Show("BA Tools - Run Export Jobs", summary.ToString());

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RunExportJobCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
