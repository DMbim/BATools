using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace BA.UI.TextHub.ExternalEvents
{
    public sealed class TextHubExternalInvoker : IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _exEvent;
        private readonly TextHubExternalHandler _handler;

        public TextHubExternalInvoker(BA.UI.ExternalEvents.RevitExternalInvoker invoker, UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _handler = new TextHubExternalHandler();
            _exEvent = ExternalEvent.Create(_handler);
        }

        public void ApplyTextStyleEdits(List<TextStyleRow> rows, Action<string> onDone)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (onDone == null) throw new ArgumentNullException(nameof(onDone));

            _handler.SetRequest(new ApplyEditsRequest(rows, onDone));
            _exEvent.Raise();
        }

        public void Dispose()
        {
            // ExternalEvent doesn't require explicit dispose. Kept for symmetry/future.
        }
    }
}