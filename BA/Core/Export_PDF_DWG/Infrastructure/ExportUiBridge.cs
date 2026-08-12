using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Export.Models;
using BA.Core.Export.Services;
using BA.Settings.Export;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

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
    /// are thirteen distinct operations here and a dispatch switch avoids thirteen
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

            Action<ExportUiResponse> marshaledCallback = response =>
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

            // Queued, not a single pending slot. A single slot meant a
            // second Submit() call before Revit's next idle tick processed
            // the first silently overwrote it, the first request's
            // callback then never fired. Confirmed cause of "Add Parameter
            // Column" and paper size detection going unresponsive whenever
            // SheetPickerWindow fired two requests back to back in its
            // constructor.
            HandlerInstance.Enqueue(request, marshaledCallback);
            _externalEvent.Raise();
        }

        private class Handler : IExternalEventHandler
        {
            private readonly Queue<(ExportUiRequest Request, Action<ExportUiResponse> Callback)> _queue =
                new Queue<(ExportUiRequest, Action<ExportUiResponse>)>();

            public void Enqueue(ExportUiRequest request, Action<ExportUiResponse> callback)
            {
                _queue.Enqueue((request, callback));
            }

            public void Execute(UIApplication app)
            {
                // Drain everything queued so far, not just one item.
                // Multiple Submit() calls can queue up before Revit invokes
                // Execute, Raise() coalesces repeated raises into a single
                // pending Execute call, so anything still in the queue at
                // that point must be processed here or it is lost.
                while (_queue.Count > 0)
                {
                    var (request, callback) = _queue.Dequeue();
                    ProcessOne(app, request, callback);
                }
            }

            private static void ProcessOne(UIApplication app, ExportUiRequest request, Action<ExportUiResponse> callback)
            {
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

                        case ExportUiAction.GetAllSheets:
                            response.Sheets = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_Sheets)
                                .WhereElementIsNotElementType()
                                .OfType<ViewSheet>()
                                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                                .Select(s => new SheetSummary { SheetNumber = s.SheetNumber ?? string.Empty, SheetName = s.Name ?? string.Empty })
                                .ToList();
                            response.Success = true;
                            break;

                        case ExportUiAction.GetAllViews:
                            // Excludes sheets (those go through the sheet
                            // picker instead), templates, and anything
                            // CanBePrinted itself would reject, matching
                            // exactly what PdfExportService/DwgExportService
                            // will validate at export time, no point
                            // listing something that would just fail.
                            response.Views = new FilteredElementCollector(doc)
                                .OfClass(typeof(View))
                                .Cast<View>()
                                .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet && v.CanBePrinted)
                                .OrderBy(v => v.ViewType.ToString(), StringComparer.OrdinalIgnoreCase)
                                .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                                .Select(v => new ViewSummary { UniqueId = v.UniqueId, Name = v.Name ?? string.Empty, ViewType = v.ViewType.ToString() })
                                .ToList();
                            response.Success = true;
                            break;

                        case ExportUiAction.GetSheetParameterNames:
                            response.StringList = GetParameterNamesForSheet(doc, request.SampleSheetNumber);
                            response.Success = true;
                            break;

                        case ExportUiAction.GetViewParameterNames:
                            response.StringList = GetParameterNamesForView(doc, request.SampleViewUniqueId);
                            response.Success = true;
                            break;

                        case ExportUiAction.GetPredefinedDwgSetups:
                            response.PredefinedDwgSetupNames = BaseExportOptions.GetPredefinedSetupNames(doc).ToList();
                            response.Success = true;
                            break;

                        case ExportUiAction.GetPredefinedDwgSetupDetails:
                            try
                            {
                                var loaded = DWGExportOptions.GetPredefinedOptions(doc, request.SetupNameToInspect);

                                response.PredefinedSetupDetails = new PredefinedDwgSetupDetails
                                {
                                    FileVersion = loaded.FileVersion,
                                    TargetUnit = loaded.TargetUnit,
                                    MergedViews = loaded.MergedViews,
                                    SharedCoords = loaded.SharedCoords,
                                    ExportingAreas = loaded.ExportingAreas,
                                    HideScopeBox = loaded.HideScopeBox,
                                    HideReferencePlane = loaded.HideReferencePlane,
                                    LineScaling = loaded.LineScaling,
                                    Colors = loaded.Colors,
                                    PropOverrides = loaded.PropOverrides
                                };
                                response.Success = true;
                            }
                            catch (Exception ex)
                            {
                                response.Success = false;
                                response.ErrorMessage = $"Could not load setup '{request.SetupNameToInspect}': {ex.Message}";
                            }
                            break;

                        case ExportUiAction.PreviewNaming:
                            HandlePreview(doc, request.JobForPreviewOrRun, app.ActiveUIDocument?.ActiveView, response);
                            break;

                        case ExportUiAction.RunJobNow:
                            response.JobResults = ExportJobRunner.RunJob(doc, request.JobForPreviewOrRun, DateTime.Now, app.ActiveUIDocument?.ActiveView);
                            response.Success = response.JobResults.All(r => !r.HasJobLevelError);
                            response.ErrorMessage = string.Join(" | ", response.JobResults.Where(r => r.HasJobLevelError).Select(r => $"{r.Format}: {r.JobLevelError}"));
                            break;

                        case ExportUiAction.DiscoverParameterColumns:
                            response.ParameterColumnCandidates = ParameterColumnDiscoveryService.DiscoverColumns(doc, request.SheetNumbersForColumns);
                            response.Success = true;
                            break;

                        case ExportUiAction.ResolveParameterColumnValues:
                            response.ParameterColumnValues = ParameterColumnValueService.ResolveValues(doc, request.SheetNumbersForColumns, request.ColumnsToResolve);
                            response.Success = true;
                            break;

                        case ExportUiAction.GetPaperSizeInfo:
                            response.PaperSizeInfoBySheet = PaperSizeDetectionService.DetectForSheets(doc, request.SheetNumbersForColumns);
                            response.Success = true;
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

            private static void HandlePreview(Document doc, ExportJobSettings job, View activeView, ExportUiResponse response)
            {
                if (job == null)
                {
                    response.Success = false;
                    response.ErrorMessage = "No job data supplied for preview.";
                    return;
                }

                var enabledFormats = new List<ExportFormat>();
                if (job.ExportPdf) enabledFormats.Add(ExportFormat.Pdf);
                if (job.ExportDwg) enabledFormats.Add(ExportFormat.Dwg);

                if (enabledFormats.Count == 0)
                {
                    response.Success = false;
                    response.ErrorMessage = "No export format is enabled on this job. Check PDF and/or DWG.";
                    return;
                }

                if (job.UseActiveViewOrSheet)
                {
                    HandleActiveViewOrSheetPreview(job, activeView, enabledFormats, response);
                    return;
                }

                if (job.SourceMode == ExportSourceMode.Views)
                {
                    HandleViewPreview(doc, job, enabledFormats, response);
                }
                else
                {
                    HandleSheetPreview(doc, job, enabledFormats, response);
                }
            }

            /// <summary>
            /// Previews against whatever is actually active right now,
            /// matching exactly what ExportJobRunner will do at run time
            /// when UseActiveViewOrSheet is set, rather than previewing
            /// against a configured selection that will be ignored anyway.
            /// </summary>
            private static void HandleActiveViewOrSheetPreview(ExportJobSettings job, View activeView, List<ExportFormat> enabledFormats, ExportUiResponse response)
            {
                if (job.SourceMode == ExportSourceMode.Sheets)
                {
                    if (!(activeView is ViewSheet activeSheet))
                    {
                        response.Success = false;
                        response.ErrorMessage = activeView == null
                            ? "No active view/sheet is available to preview against."
                            : "The active view is not a sheet. Switch to a sheet, or turn off 'Current View/Sheet Only' for this job.";
                        return;
                    }

                    var revisionParamName = NamingTemplateEngine.LoadCurrentRevisionParamName();
                    var now = DateTime.Now;

                    foreach (var format in enabledFormats)
                    {
                        var result = new NamingPreviewResult { Format = format };

                        try
                        {
                            var extension = format == ExportFormat.Pdf ? ".pdf" : ".dwg";
                            result.FileName = NamingTemplateEngine.ResolveFileName(job.NamingTemplate, activeSheet, job, now, revisionParamName) + extension;
                            result.Folder = string.IsNullOrWhiteSpace(job.OutputFolderTemplate)
                                ? "(no output folder template set)"
                                : NamingTemplateEngine.ResolveFolder(job.OutputFolderTemplate, activeSheet, job, now, revisionParamName);
                            result.Success = true;
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.ErrorMessage = $"Template resolution failed against active sheet {activeSheet.SheetNumber}: {ex.Message}";
                        }

                        response.PreviewResults.Add(result);
                    }
                }
                else
                {
                    if (activeView == null || activeView.ViewType == ViewType.DrawingSheet || activeView.IsTemplate)
                    {
                        response.Success = false;
                        response.ErrorMessage = activeView == null
                            ? "No active view is available to preview against."
                            : "The active view is a sheet or a view template, neither can be previewed in Views mode.";
                        return;
                    }

                    var now = DateTime.Now;

                    foreach (var format in enabledFormats)
                    {
                        var result = new NamingPreviewResult { Format = format };

                        try
                        {
                            var extension = format == ExportFormat.Pdf ? ".pdf" : ".dwg";
                            result.FileName = NamingTemplateEngine.ResolveFileNameForView(job.NamingTemplate, activeView, job, now) + extension;
                            result.Folder = string.IsNullOrWhiteSpace(job.OutputFolderTemplate)
                                ? "(no output folder template set)"
                                : NamingTemplateEngine.ResolveFolderForView(job.OutputFolderTemplate, activeView, job, now);
                            result.Success = true;
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.ErrorMessage = $"Template resolution failed against active view {activeView.Name}: {ex.Message}";
                        }

                        response.PreviewResults.Add(result);
                    }
                }

                response.Success = response.PreviewResults.All(r => r.Success);
            }

            private static void HandleSheetPreview(Document doc, ExportJobSettings job, List<ExportFormat> enabledFormats, ExportUiResponse response)
            {
                var sampleSheet = FindSampleSheet(doc, job.SelectedSheetNumbers);

                if (sampleSheet == null)
                {
                    response.Success = false;
                    response.ErrorMessage = "No sheets exist in this document to preview against.";
                    return;
                }

                var revisionParamName = NamingTemplateEngine.LoadCurrentRevisionParamName();
                var now = DateTime.Now;

                foreach (var format in enabledFormats)
                {
                    var result = new NamingPreviewResult { Format = format };

                    try
                    {
                        var extension = format == ExportFormat.Pdf ? ".pdf" : ".dwg";

                        result.FileName = NamingTemplateEngine.ResolveFileName(job.NamingTemplate, sampleSheet, job, now, revisionParamName) + extension;

                        result.Folder = string.IsNullOrWhiteSpace(job.OutputFolderTemplate)
                            ? "(no output folder template set)"
                            : NamingTemplateEngine.ResolveFolder(job.OutputFolderTemplate, sampleSheet, job, now, revisionParamName);

                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Template resolution failed against sample sheet {sampleSheet.SheetNumber}: {ex.Message}";
                    }

                    response.PreviewResults.Add(result);
                }

                response.Success = response.PreviewResults.All(r => r.Success);
            }

            private static void HandleViewPreview(Document doc, ExportJobSettings job, List<ExportFormat> enabledFormats, ExportUiResponse response)
            {
                var sampleView = FindSampleView(doc, job.SelectedViewUniqueIds);

                if (sampleView == null)
                {
                    response.Success = false;
                    response.ErrorMessage = "No views exist in this document to preview against.";
                    return;
                }

                var now = DateTime.Now;

                foreach (var format in enabledFormats)
                {
                    var result = new NamingPreviewResult { Format = format };

                    try
                    {
                        var extension = format == ExportFormat.Pdf ? ".pdf" : ".dwg";

                        result.FileName = NamingTemplateEngine.ResolveFileNameForView(job.NamingTemplate, sampleView, job, now) + extension;

                        result.Folder = string.IsNullOrWhiteSpace(job.OutputFolderTemplate)
                            ? "(no output folder template set)"
                            : NamingTemplateEngine.ResolveFolderForView(job.OutputFolderTemplate, sampleView, job, now);

                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Template resolution failed against sample view {sampleView.Name}: {ex.Message}";
                    }

                    response.PreviewResults.Add(result);
                }

                response.Success = response.PreviewResults.All(r => r.Success);
            }

            private static View FindSampleView(Document doc, IList<string> selectedViewUniqueIds)
            {
                if (selectedViewUniqueIds != null && selectedViewUniqueIds.Count > 0)
                {
                    foreach (var uniqueId in selectedViewUniqueIds)
                    {
                        if (doc.GetElement(uniqueId) is View view && !view.IsTemplate)
                        {
                            return view;
                        }
                    }
                }

                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet && v.CanBePrinted);
            }

            private static List<string> GetParameterNamesForSheet(Document doc, string sheetNumber)
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

                var sheet = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Sheets)
                    .WhereElementIsNotElementType()
                    .OfType<ViewSheet>()
                    .FirstOrDefault(s => string.Equals(s.SheetNumber, sheetNumber, StringComparison.OrdinalIgnoreCase));

                if (sheet != null)
                {
                    foreach (Parameter p in sheet.Parameters)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Definition?.Name))
                        {
                            names.Add(p.Definition.Name);
                        }
                    }
                }

                var projectInfo = doc.ProjectInformation;

                if (projectInfo != null)
                {
                    foreach (Parameter p in projectInfo.Parameters)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Definition?.Name))
                        {
                            names.Add(p.Definition.Name);
                        }
                    }
                }

                return names.ToList();
            }

            private static List<string> GetParameterNamesForView(Document doc, string viewUniqueId)
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(viewUniqueId) && doc.GetElement(viewUniqueId) is View view)
                {
                    foreach (Parameter p in view.Parameters)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Definition?.Name))
                        {
                            names.Add(p.Definition.Name);
                        }
                    }
                }

                var viewProjectInfo = doc.ProjectInformation;

                if (viewProjectInfo != null)
                {
                    foreach (Parameter p in viewProjectInfo.Parameters)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Definition?.Name))
                        {
                            names.Add(p.Definition.Name);
                        }
                    }
                }

                return names.ToList();
            }

            private static ViewSheet FindSampleSheet(Document doc, IList<string> selectedSheetNumbers)
            {
                if (selectedSheetNumbers != null && selectedSheetNumbers.Count > 0)
                {
                    var match = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Sheets)
                        .WhereElementIsNotElementType()
                        .OfType<ViewSheet>()
                        .FirstOrDefault(s => selectedSheetNumbers.Contains(s.SheetNumber, StringComparer.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        return match;
                    }
                }

                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Sheets)
                    .WhereElementIsNotElementType()
                    .OfType<ViewSheet>()
                    .FirstOrDefault();
            }

            public string GetName() => "BA Tools - Export Settings External Event Handler";
        }
    }
}