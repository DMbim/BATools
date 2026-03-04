// File: BA.UI/ExternalEvents/RevitExternalInvoker.cs
using Autodesk.Revit.UI;
using System;

namespace BA.UI.ExternalEvents
{
    /// <summary>
    /// Small wrapper around ExternalEvent + handler queue.
    /// </summary>
    public sealed class RevitExternalInvoker
    {
        private readonly RevitActionQueueHandler _handler;
        private readonly ExternalEvent _externalEvent;

        public RevitExternalInvoker(RevitActionQueueHandler handler, ExternalEvent externalEvent)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));
        }

        // ---------------------------
        // Primary API: Run(...)
        // ---------------------------

        public void Run(Action<UIApplication> apiAction, Action? onCompleted = null, Action<Exception>? onError = null)
        {
            if (apiAction == null) throw new ArgumentNullException(nameof(apiAction));

            _handler.Enqueue(
                uiApp => { apiAction(uiApp); return null; },
                _ => onCompleted?.Invoke(),
                onError);

            _externalEvent.Raise();
        }

        public void Run<T>(Func<UIApplication, T> apiFunc, Action<T>? onCompleted = null, Action<Exception>? onError = null)
        {
            if (apiFunc == null) throw new ArgumentNullException(nameof(apiFunc));

            _handler.Enqueue(
                uiApp => apiFunc(uiApp),
                result => onCompleted?.Invoke((T)result!),
                onError);

            _externalEvent.Raise();
        }

        // ---------------------------
        // Compatibility aliases: Invoke(...)
        // ---------------------------

        public void Invoke(Action<UIApplication> apiAction, Action? onCompleted = null, Action<Exception>? onError = null)
            => Run(apiAction, onCompleted, onError);

        public void Invoke<T>(Func<UIApplication, T> apiFunc, Action<T>? onCompleted = null, Action<Exception>? onError = null)
            => Run(apiFunc, onCompleted, onError);
    }
}
