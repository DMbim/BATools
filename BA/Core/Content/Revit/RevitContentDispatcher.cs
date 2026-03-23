using Autodesk.Revit.UI;
using BA.Core.Content.Models;
using System;
using System.Collections.Generic;

namespace BA.Core.Content.Revit
{
    public sealed class RevitContentDispatcher : IDisposable
    {
        private readonly ContentLoadExternalHandler _loadHandler;
        private readonly ExternalEvent _loadExternalEvent;

        private readonly ContentPreviewExportExternalHandler _previewHandler;
        private readonly ExternalEvent _previewExternalEvent;

        public RevitContentDispatcher()
        {
            _loadHandler = new ContentLoadExternalHandler();
            _loadExternalEvent = ExternalEvent.Create(_loadHandler);

            _previewHandler = new ContentPreviewExportExternalHandler();
            _previewExternalEvent = ExternalEvent.Create(_previewHandler);
        }

        public void RequestLoad(ContentLoadRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            _loadHandler.SetRequest(request);
            _loadExternalEvent.Raise();
        }

        public string ConsumeLastLoadError()
        {
            return _loadHandler.ConsumeLastError();
        }

        public void RequestPreviewExport(ContentPreviewExportRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            _previewHandler.SetRequest(request);
            _previewExternalEvent.Raise();
        }

        public string ConsumeLastPreviewExportError()
        {
            return _previewHandler.ConsumeLastError();
        }

        public IReadOnlyList<ContentPreviewExportItemResult> ConsumeLastPreviewExportResults()
        {
            return _previewHandler.ConsumeLastResults();
        }

        public void Dispose()
        {
            _loadExternalEvent.Dispose();
            _previewExternalEvent.Dispose();
        }
    }
}