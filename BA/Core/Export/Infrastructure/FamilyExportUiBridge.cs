using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Export.Services;

namespace BA.Core.Export.Infrastructure
{
    /// <summary>
    /// Static ExternalEvent bridge for the family export window, separate
    /// from ExportUiBridge since this is a genuinely different workflow
    /// (families, not sheets, no scheduling), not because it needed to be
    /// split for isolation reasons. Same lazy-initialization lifecycle:
    /// EnsureInitialized() must be called from a valid Revit API context
    /// before first use. Same queue-based Execute as ExportUiBridge, a
    /// single pending slot was confirmed to silently drop requests when
    /// two Submit() calls land before Revit's next idle tick, no reason to
    /// repeat that mistake here.
    /// </summary>
    public static class FamilyExportUiBridge
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

        public static void Submit(FamilyExportUiRequest request, Action<FamilyExportUiResponse> onComplete)
        {
            if (_externalEvent == null)
            {
                AppLogger.LogError("FamilyExportUiBridge.Submit: EnsureInitialized() was never called", null);
                onComplete?.Invoke(new FamilyExportUiResponse
                {
                    Action = request.Action,
                    Success = false,
                    ErrorMessage = "Family export UI bridge was not initialized."
                });
                return;
            }

            Dispatcher callingDispatcher = Application.Current?.Dispatcher;

            Action<FamilyExportUiResponse> marshaledCallback = response =>
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
            private readonly Queue<(FamilyExportUiRequest Request, Action<FamilyExportUiResponse> Callback)> _queue =
                new Queue<(FamilyExportUiRequest, Action<FamilyExportUiResponse>)>();

            public void Enqueue(FamilyExportUiRequest request, Action<FamilyExportUiResponse> callback)
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

            private static void ProcessOne(UIApplication app, FamilyExportUiRequest request, Action<FamilyExportUiResponse> callback)
            {
                if (request == null)
                {
                    return;
                }

                var response = new FamilyExportUiResponse { Action = request.Action };

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
                        case FamilyExportUiAction.GetFamilies:
                            response.Families = FamilyDiscoveryService.GetFamilies(doc);
                            response.Success = true;
                            break;

                        case FamilyExportUiAction.RunFamilyExport:
                            response.RunResult = FamilyExportRunner.Run(doc, request.SettingsForRun);
                            response.Success = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    AppLogger.LogError($"FamilyExportUiBridge failed for action {request.Action}", ex);
                }

                callback?.Invoke(response);
            }

            public string GetName() => "BA Tools - Family Export External Event Handler";
        }
    }
}
