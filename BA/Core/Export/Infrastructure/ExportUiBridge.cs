using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Export.Models;
using BA.Core.Export.Services;
using BA.Settings.Export;

namespace BA.Core.Export.Infrastructure
{
    /// <summary>
    /// Static ExternalEvent bridge between the export settings WPF window and
    /// the Revit API thread, mirroring the lazy-initialization lifecycle
    /// established by BA.Core.Ledger.LedgerUiBridge: EnsureInitialized() must
    /// be called from a valid Revit API context (an IExternalCommand.Execute)
    /// before first use, ExternalEvent.Create() is never called eagerly at
    /// OnStartup.
    ///
    /// Unlike LedgerUiBridge, which creates one ExternalEvent per operation,
    /// this uses a single multiplexed handler keyed by ExportUiAction, there
    /// are six distinct operations here and a dispatch switch avoids six
    /// near-identical handler classes. If per-operation isolation ever
    /// matters (one operation needing to stay in flight while another fires),
    /// split this into separate handlers the same way LedgerUiBridge does,
    /// that is the proven pattern in this codebase for that case.
    ///
    /// Dispatcher marshaling mirrors LedgerUiBridge exactly: unconditional
    /// BeginInvoke via Application.Current.Dispatcher, not a CheckAccess
    /// guard, for consistency with the established convention.
    /// </summary>
    public static class ExportUiBridge
    {
        private static ExternalEvent _externalEvent;
        private static readonly Handler HandlerInstance = new Handler();

        public static void EnsureInitialized()
        {
            if (_externalEvent == null)
            {
                _externalEvent = ExternalEvent.Create(HandlerInstance);
            }
        }

        public static void Submit(ExportUiRequest request, Action<ExportUiResponse> onComplete)
        {
            if (_externalEvent == null)
            {
                AppLogger.LogError("ExportUiBridge.Submit: EnsureInitialized() was never called", null);
                onComplete?.Invoke(new ExportUiResponse
                {
                    Action = request.Action,
                    Success = false,
                    ErrorMessage = "Export UI bridge was not initialized."
                });
                return;
            }

            Dispatcher callingDispatcher = Application.Current?.Dispatcher;
            HandlerInstance.PendingRequest = request;
            HandlerInstance.PendingCallback = response =>
            {
                if (callingDispatcher != null)
                {
                    callingDispatcher.BeginInvoke(new Action(() => onComplete?.Invoke(response)));
                }
                else
                {
                    onComplete?.Invoke(response);
                }
            };

            _externalEvent.Raise();
        }

        private class Handler : IExternalEventHandler
        {
            public ExportUiRequest PendingRequest;
            public Action<ExportUiResponse> PendingCallback;

            public void Execute(UIApplication app)
            {
                var request = PendingRequest;
                var callback = PendingCallback;
                PendingRequest = null;
                PendingCallback = null;

                if (request == null)
                {
                    return;
                }

                var response = new ExportUiResponse { Action = request.Action };

                try
                {
                    var doc = app.ActiveUIDocument?.Document;

                    if (doc == null)
                    {
                        response.Success = false;
                        response.ErrorMessage = "No active document.";
                        callback?.Invoke(response);
                        return;
                    }

                    switch (request.Action)
                    {
                        case ExportUiAction.LoadSettings:
                            response.LoadedSettings = ExportSettingsStore.Load(doc);
                            response.Success = true;
                            break;

                        case ExportUiAction.SaveSettings:
                            ExportSettingsStore.Save(doc, request.SettingsToSave);
                            response.Success = true;
                            break;

                        case ExportUiAction.GetSheetSetNames:
                            response.StringList = new FilteredElementCollector(doc)
                                .OfClass(typeof(ViewSheetSet))
                                .Cast<ViewSheetSet>()
                                .Select(vss => vss.Name)
                                .OrderBy(n => n)
                                .ToList();
                            response.Success = true;
                            break;

                        case ExportUiAction.GetDwgExportSetupNames:
                            response.StringList = BaseExportOptions.GetPredefinedSetupNames(doc).ToList();
                            response.Success = true;
                            break;

                        case ExportUiAction.PreviewNaming:
                            HandlePreview(doc, request.JobForPreviewOrRun, response);
                            break;

                        case ExportUiAction.RunJobNow:
                            response.JobResult = ExportJobRunner.RunJob(doc, request.JobForPreviewOrRun, DateTime.Now);
                            response.Success = !response.JobResult.HasJobLevelError;
                            response.ErrorMessage = response.JobResult.JobLevelError;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    AppLogger.LogError($"ExportUiBridge failed for action {request.Action}", ex);
                }

                callback?.Invoke(response);
            }

            private static void HandlePreview(Document doc, ExportJobSettings job, ExportUiResponse response)
            {
                if (job == null)
                {
                    response.Success = false;
                    response.ErrorMessage = "No job data supplied for preview.";
                    return;
                }

                var sampleSheet = FindSampleSheet(doc, job.SheetSetName);

                if (sampleSheet == null)
                {
                    response.Success = false;
                    response.ErrorMessage = "No sheets exist in this document to preview against.";
                    return;
                }

                try
                {
                    var revisionParamName = NamingTemplateEngine.LoadCurrentRevisionParamName();
                    var now = DateTime.Now;

                    response.PreviewFileName = NamingTemplateEngine.ResolveFileName(job.NamingTemplate, sampleSheet, job, now, revisionParamName)
                        + (job.Format == ExportFormat.Pdf ? ".pdf" : ".dwg");

                    response.PreviewFolder = string.IsNullOrWhiteSpace(job.OutputFolderTemplate)
                        ? "(no output folder template set)"
                        : NamingTemplateEngine.ResolveFolder(job.OutputFolderTemplate, sampleSheet, job, now, revisionParamName);

                    response.Success = true;
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = $"Template resolution failed against sample sheet {sampleSheet.SheetNumber}: {ex.Message}";
                }
            }

            private static ViewSheet FindSampleSheet(Document doc, string preferredSheetSetName)
            {
                if (!string.IsNullOrWhiteSpace(preferredSheetSetName))
                {
                    var set = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheetSet))
                        .Cast<ViewSheetSet>()
                        .FirstOrDefault(vss => string.Equals(vss.Name, preferredSheetSetName, StringComparison.OrdinalIgnoreCase));

                    if (set != null)
                    {
                        foreach (View v in set.Views)
                        {
                            if (v is ViewSheet vs)
                            {
                                return vs;
                            }
                        }
                    }
                }

                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault();
            }

            public string GetName() => "BA Tools - Export Settings External Event Handler";
        }
    }
}
