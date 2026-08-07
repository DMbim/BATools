// File: BA/Core/CurveToElement/Infrastructure/WallGenerationRequestHandler.cs
// Action: REPLACE (full file)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.CurveToElement.Models;
using BA.Core.CurveToElement.Services;

namespace BA.Core.CurveToElement.Infrastructure
{
    /// <summary>
    /// IExternalEventHandler wrapping WallGenerationService.Execute. Same queued-request
    /// pattern as WallFaceOffsetPreviewHandler, for the same reason: ExternalEvent.Raise()
    /// calls made while Execute() is pending are collapsed by Revit, so a single-slot field
    /// would silently drop a request if the user could somehow trigger Generate twice in quick
    /// succession (guarded against in the ViewModel via CanExecuteGenerate/IsGenerating, but
    /// the handler itself should not depend on the caller's discipline to stay correct).
    /// </summary>
    public class WallGenerationRequestHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<PendingGeneration> _pendingRequests = new ConcurrentQueue<PendingGeneration>();
        private readonly WallGenerationService _generationService = new WallGenerationService();
        private readonly ExternalEvent _externalEvent;

        public WallGenerationRequestHandler()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        public void RequestGeneration(
            IReadOnlyList<GroupGenerationRequest> requests,
            bool deleteSourceLinesAfterCreation,
            Action<GenerationResult> onComplete)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            _pendingRequests.Enqueue(new PendingGeneration(requests, deleteSourceLinesAfterCreation, onComplete));
            _externalEvent.Raise();
        }

        public void Execute(UIApplication app)
        {
            if (app?.ActiveUIDocument?.Document == null)
            {
                AppLogger.LogInfo("[CurveToElement] WallGenerationRequestHandler.Execute - no active document, requests dropped.");
                DrainWithFailureCallback("No active Revit document.");
                return;
            }

            Document doc = app.ActiveUIDocument.Document;

            while (_pendingRequests.TryDequeue(out PendingGeneration pending))
            {
                GenerationResult result;
                try
                {
                    result = _generationService.Execute(doc, pending.Requests, pending.DeleteSourceLinesAfterCreation);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[CurveToElement] WallGenerationRequestHandler.Execute - unhandled exception", ex);
                    result = new GenerationResult(false, $"Unexpected error: {ex.Message}", 0, new List<string>(), 0);
                }

                pending.OnComplete?.Invoke(result);
            }
        }

        private void DrainWithFailureCallback(string reason)
        {
            while (_pendingRequests.TryDequeue(out PendingGeneration pending))
            {
                pending.OnComplete?.Invoke(new GenerationResult(false, reason, 0, new List<string>(), 0));
            }
        }

        public string GetName() => "BA Curve to Element - Wall Generation";

        private class PendingGeneration
        {
            public IReadOnlyList<GroupGenerationRequest> Requests { get; }
            public bool DeleteSourceLinesAfterCreation { get; }
            public Action<GenerationResult> OnComplete { get; }

            public PendingGeneration(
                IReadOnlyList<GroupGenerationRequest> requests,
                bool deleteSourceLinesAfterCreation,
                Action<GenerationResult> onComplete)
            {
                Requests = requests;
                DeleteSourceLinesAfterCreation = deleteSourceLinesAfterCreation;
                OnComplete = onComplete;
            }
        }
    }
}