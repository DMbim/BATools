// File: BA/Core/CurveToElement/Infrastructure/WallFaceOffsetPreviewHandler.cs
// Action: CREATE NEW

using System;
using System.Collections.Concurrent;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.CurveToElement.Models;
using BA.Core.CurveToElement.Services;

namespace BA.Core.CurveToElement.Infrastructure
{
    /// <summary>
    /// IExternalEventHandler for live wall face offset preview refresh, fired on every
    /// relevant combo-box change in the settings panel. Requests are queued rather than
    /// stored in a single field, because ExternalEvent.Raise() calls made while Execute()
    /// is pending get collapsed by Revit into one Execute() invocation - without a queue,
    /// rapid changes across different groups would silently drop all but the last request.
    /// </summary>
    public class WallFaceOffsetPreviewHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<WallPreviewRequest> _pendingRequests = new ConcurrentQueue<WallPreviewRequest>();
        private readonly WallFaceOffsetPreviewCalculator _calculator = new WallFaceOffsetPreviewCalculator();
        private readonly ExternalEvent _externalEvent;

        /// <summary>
        /// Raised once per queued request, on the Revit UI thread (Execute always runs on
        /// the main thread, same thread WPF's dispatcher pumps for a properly-owned modeless
        /// window), so subscribers can update bound properties directly without Dispatcher.Invoke.
        /// </summary>
        public event Action<WallPreviewResult> ResultReady;

        public WallFaceOffsetPreviewHandler()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        public void RequestPreview(Guid groupId, ElementId wallTypeId)
        {
            if (wallTypeId == null || wallTypeId == ElementId.InvalidElementId)
                return;

            _pendingRequests.Enqueue(new WallPreviewRequest(groupId, wallTypeId));
            _externalEvent.Raise();
        }

        public void Execute(UIApplication app)
        {
            if (app?.ActiveUIDocument?.Document == null)
            {
                AppLogger.LogInfo("[CurveToElement] WallFaceOffsetPreviewHandler.Execute - no active document, requests dropped.");
                DrainQueueWithoutProcessing();
                return;
            }

            Document doc = app.ActiveUIDocument.Document;

            while (_pendingRequests.TryDequeue(out WallPreviewRequest request))
            {
                try
                {
                    ProcessRequest(doc, request);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[CurveToElement] Preview request failed for group {request.GroupId}", ex);
                }
            }
        }
        public string GetName() => "BA Curve to Element - Wall Face Offset Preview"; // <- NEW
        private void ProcessRequest(Document doc, WallPreviewRequest request)
        {
            Element element = doc.GetElement(request.WallTypeId);
            if (!(element is WallType wallType))
            {
                AppLogger.LogInfo($"[CurveToElement] Preview request for group {request.GroupId} - element {request.WallTypeId.Value} is not a WallType.");
                return;
            }

            WallFaceOffsetPreview preview = _calculator.Calculate(doc, wallType);

            WallPreviewResult result = new WallPreviewResult(
                request.GroupId,
                preview,
                preview.IsSupported ? _calculator.FormatOffset(doc, preview.TotalWidth) : "-",
                preview.IsSupported ? _calculator.FormatOffset(doc, preview.CoreCenterlineOffset) : "-",
                preview.IsSupported ? _calculator.FormatOffset(doc, preview.CoreSide1FaceOffset) : "-",
                preview.IsSupported ? _calculator.FormatOffset(doc, preview.CoreSide2FaceOffset) : "-",
                preview.IsSupported ? _calculator.FormatOffset(doc, preview.FinishSide1FaceOffset) : "-",
                preview.IsSupported ? _calculator.FormatOffset(doc, preview.FinishSide2FaceOffset) : "-");

            ResultReady?.Invoke(result);
        }

        private void DrainQueueWithoutProcessing()
        {
            while (_pendingRequests.TryDequeue(out _)) { }
        }
    }
}