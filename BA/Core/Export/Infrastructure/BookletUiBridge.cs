using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Export.Services;

namespace BA.Core.Export.Infrastructure
{
    /// <summary>
    /// Static ExternalEvent bridge for the booklet generator window. Same
    /// lazy-initialization lifecycle and queue-based Execute as
    /// ExportUiBridge and FamilyExportUiBridge, a single pending slot was
    /// confirmed to silently drop requests when two Submit() calls land
    /// before Revit's next idle tick.
    /// </summary>
    public static class BookletUiBridge
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

        public static void Submit(BookletUiRequest request, Action<BookletUiResponse> onComplete)
        {
            if (_externalEvent == null)
            {
                AppLogger.LogError("BookletUiBridge.Submit: EnsureInitialized() was never called", null);
                onComplete?.Invoke(new BookletUiResponse
                {
                    Action = request.Action,
                    Success = false,
                    ErrorMessage = "Booklet UI bridge was not initialized."
                });
                return;
            }

            Dispatcher callingDispatcher = Application.Current?.Dispatcher;

            Action<BookletUiResponse> marshaledCallback = response =>
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

            HandlerInstance.Enqueue(request, marshaledCallback);
            _externalEvent.Raise();
        }

        private class Handler : IExternalEventHandler
        {
            private readonly Queue<(BookletUiRequest Request, Action<BookletUiResponse> Callback)> _queue =
                new Queue<(BookletUiRequest, Action<BookletUiResponse>)>();

            public void Enqueue(BookletUiRequest request, Action<BookletUiResponse> callback)
            {
                _queue.Enqueue((request, callback));
            }

            public void Execute(UIApplication app)
            {
                while (_queue.Count > 0)
                {
                    var (request, callback) = _queue.Dequeue();
                    ProcessOne(app, request, callback);
                }
            }

            private static void ProcessOne(UIApplication app, BookletUiRequest request, Action<BookletUiResponse> callback)
            {
                if (request == null)
                {
                    return;
                }

                var response = new BookletUiResponse { Action = request.Action };

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
                        case BookletUiAction.GetTypesByCategory:
                            response.Types = BookletTypeDiscoveryService.GetTypesByCategory(doc, request.Category);
                            response.Success = true;
                            break;

                        case BookletUiAction.GetTypesByParameter:
                            response.Types = BookletTypeDiscoveryService.GetTypesByParameterValue(doc, request.ParameterName);
                            response.Success = true;
                            break;

                        case BookletUiAction.DiscoverInfoParameters:
                            response.ParameterCandidates = BookletParameterDiscoveryService.DiscoverColumns(doc, request.TypeUniqueIdsForParameterDiscovery);
                            response.Success = true;
                            break;

                        case BookletUiAction.GetTitleBlocks:
                            var titleBlocks = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .WhereElementIsElementType()
                                .ToList();
                            response.TitleBlockNames = titleBlocks.Select(t => t.Name ?? string.Empty).ToList();
                            response.TitleBlockUniqueIds = titleBlocks.Select(t => t.UniqueId).ToList();
                            response.Success = true;
                            break;

                        case BookletUiAction.GetLegendViews:
                            var legendViews = new FilteredElementCollector(doc)
                                .OfClass(typeof(View))
                                .Cast<View>()
                                .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate)
                                .ToList();
                            response.LegendViewNames = legendViews.Select(v => v.Name ?? string.Empty).ToList();
                            response.LegendViewUniqueIds = legendViews.Select(v => v.UniqueId).ToList();
                            response.Success = true;
                            break;

                        case BookletUiAction.RunBooklets:
                            response.RunOutcomes = BookletRunner.Run(doc, request.SettingsForRun);
                            response.Success = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    AppLogger.LogError($"BookletUiBridge failed for action {request.Action}", ex);
                }

                callback?.Invoke(response);
            }

            public string GetName() => "BA Tools - Booklet Generator External Event Handler";
        }
    }
}
