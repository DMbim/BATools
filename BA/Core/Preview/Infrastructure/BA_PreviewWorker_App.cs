// Path: BA.PreviewWorker/BA_PreviewWorker_App.cs
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.Core.Content.Preview;
using System;
using System.IO;

namespace BA.PreviewWorker
{
    public sealed class BA_PreviewWorker_App : IExternalApplication
    {
        private const int MaxItemsPerRun = 500;
        private const int MaxAttemptsBeforeQuarantine = 3;

        // Environment specific, fill these in for your actual deployment
        // before building this project.
        private static readonly string QueueDatabasePath = @"S:\CAD\Autodesk Revit\_admin\BA_tools\ContentBrowserfamilyversioning.db";
        private static readonly string[] LibraryRootFolders = { @"S:\CAD\Autodesk Revit\BA_Families\BA_Families_v26" };
        private static readonly string LogPath = @"C:\Users\mpocinek\AppData\Roaming\BA\ContentBrowser\PreviewWorker\worker.log";

        public Result OnStartup(UIControlledApplication application)
        {
            application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ControlledApplication.ApplicationInitialized -= OnApplicationInitialized;
            return Result.Succeeded;
        }

        private void OnApplicationInitialized(object? sender, ApplicationInitializedEventArgs e)
        {
            // Application instance is passed as sender, not via e
            if (sender is Application app)
            {
                try
                {
                    Log("Worker run started.");
                    RunQueue(app);
                    Log("Worker run completed.");
                }
                catch (Exception ex)
                {
                    Log("FATAL " + ex);
                }
                finally
                {
                    ExitRevit(app);
                }
            }
            else
            {
                Log("FATAL: sender is not Application.");
            }
        }

        private static void RunQueue(Application app)
        {
            var repo = new PreviewQueueRepository(QueueDatabasePath);
            repo.EnsureSchema();
            repo.ReconcileStuckProcessingRows(MaxAttemptsBeforeQuarantine);

            var pathsNeedingPreview = PreviewLibraryScanner.FindPathsNeedingPreview(LibraryRootFolders);
            repo.UpsertPending(pathsNeedingPreview);

            var batch = repo.GetNextBatch(MaxItemsPerRun);
            Log($"Processing {batch.Count} item(s) this run.");

            foreach (var item in batch)
            {
                repo.MarkProcessing(item.Id);

                try
                {
                    PreviewExportResult result = HeadlessFamilyPreviewExportService.ExportPreview(app, item.FamilyPath, overwriteExisting: true);

                    if (result.Success)
                    {
                        repo.MarkSuccess(item.Id);
                        Log($"OK   {item.FamilyPath}");
                    }
                    else
                    {
                        repo.MarkFailed(item.Id, result.Message, MaxAttemptsBeforeQuarantine);
                        Log($"FAIL {item.FamilyPath} -> {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    repo.MarkFailed(item.Id, ex.Message, MaxAttemptsBeforeQuarantine);
                    Log($"FAIL {item.FamilyPath} -> {ex.Message}");
                }
            }
        }

        private static void ExitRevit(Application app)
        {
            try
            {
                var uiapp = new UIApplication(app);
                RevitCommandId exitCommand = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
                uiapp.PostCommand(exitCommand);
            }
            catch (Exception ex)
            {
                // If this fails for any reason, the outer supervisor
                // process still bounds total runtime and will terminate
                // this Revit process rather than let it hang the
                // scheduled task indefinitely.
                Log("Could not post exit command: " + ex.Message);
            }
        }

        private static void Log(string message)
        {
            try
            {
                string? dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(LogPath, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}