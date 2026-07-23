using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.BAApplication;
using BA.Core.Export.Models;
using BA.Core.Export.Services;
using BA.Settings.Export;

namespace BA.Core.Export.Infrastructure
{
    /// <summary>
    /// Subscribes to UIApplication.Idling and fires enabled export jobs whose
    /// scheduled time falls within their CatchUpWindow. If Revit was not open
    /// during that window on a given day, the job is skipped entirely for
    /// that day, it never fires late, it waits for its next scheduled day.
    /// Register once at add-in startup:
    ///   var scheduler = new ExportScheduler();
    ///   uiControlledApplication.Idling += scheduler.OnIdling;
    /// and unsubscribe the same delegate in OnShutdown.
    /// </summary>
    public class ExportScheduler
    {
        private bool _runInProgress;

        public void OnIdling(object sender, IdlingEventArgs e)
        {
            if (_runInProgress || SynchronizeGuard.IsSynchronizing)
            {
                return;
            }

            if (!(sender is UIApplication uiApp))
            {
                return;
            }

            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                return;
            }

            var doc = uiDoc.Document;

            if (doc.IsLinked)
            {
                return;
            }

            ExportSettingsRoot settingsRoot;

            try
            {
                settingsRoot = ExportSettingsStore.Load(doc);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ExportScheduler failed to load export settings", ex);
                return;
            }

            if (settingsRoot.Jobs.Count == 0)
            {
                return;
            }

            var now = DateTime.Now;
            var anyFired = false;

            foreach (var job in settingsRoot.Jobs)
            {
                if (!ShouldFireNow(job, now))
                {
                    continue;
                }

                _runInProgress = true;
                anyFired = true;

                try
                {
                    var result = ExportJobRunner.RunJob(doc, job, now);
                    job.LastAutoRunDate = now.Date;

                    AppLogger.LogInfo(result.HasJobLevelError
                        ? $"Scheduled export job '{job.JobName}' failed: {result.JobLevelError}"
                        : $"Scheduled export job '{job.JobName}' ran: {result.SuccessCount} succeeded, {result.FailureCount} failed.");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Scheduled export job '{job.JobName}' threw unexpectedly", ex);
                }
                finally
                {
                    _runInProgress = false;
                }
            }

            if (anyFired)
            {
                try
                {
                    ExportSettingsStore.Save(doc, settingsRoot);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ExportScheduler failed to persist LastAutoRunDate", ex);
                }
            }
        }

        private static bool ShouldFireNow(ExportJobSettings job, DateTime now)
        {
            if (!job.Enabled || !job.ScheduleEnabled)
            {
                return false;
            }

            if (job.ScheduledDays.Count > 0 && !job.ScheduledDays.Contains(now.DayOfWeek))
            {
                return false;
            }

            if (job.LastAutoRunDate.HasValue && job.LastAutoRunDate.Value.Date == now.Date)
            {
                return false;
            }

            var scheduledMoment = now.Date + job.ScheduledTimeOfDay;
            var windowEnd = scheduledMoment + job.CatchUpWindow;

            // Confirmed decision: if Revit was not open during
            // [scheduledMoment, windowEnd], skip entirely for today, never
            // fire late outside this window. Fires again on the next
            // scheduled day.
            return now >= scheduledMoment && now <= windowEnd;
        }
    }
}
