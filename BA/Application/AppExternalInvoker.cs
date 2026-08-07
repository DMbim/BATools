// File: BA.UI/ExternalEvents/AppExternalInvoker.cs
using Autodesk.Revit.UI;
using System;
using System.Windows.Threading;

namespace BA.UI.ExternalEvents
{
    /// <summary>
    /// Application-scoped, lazily-initialized ExternalEvent + handler pair.
    /// Created on first access (must be first accessed from the Revit UI thread,
    /// e.g. inside a ribbon button click handler) and lives for the duration of
    /// the Revit session. Never disposed explicitly; Revit tears down the
    /// ExternalEvent registration on process exit.
    ///
    /// This exists to avoid per-window ExternalEvent/handler pairs whose
    /// lifetime is tied to a WPF window that may close (and become GC-eligible)
    /// while a Raise() is still pending on the native side. See BimHubWindow's
    /// original crash: a self-closing launcher window owned its own
    /// ExternalEvent, and closing it before the queued job executed left
    /// nothing rooting the handler, so the GC could collect it out from under
    /// a pending native callback.
    /// </summary>
    public static class AppExternalInvoker
    {
        private static RevitExternalInvoker? _instance;
        private static readonly object _lock = new();

        public static RevitExternalInvoker Instance
        {
            get
            {
                if (_instance != null) return _instance;

                lock (_lock)
                {
                    _instance ??= Create();
                }

                return _instance;
            }
        }

        private static RevitExternalInvoker Create()
        {
            var handler = new RevitActionQueueHandler(Dispatcher.CurrentDispatcher);
            var externalEvent = ExternalEvent.Create(handler);
            return new RevitExternalInvoker(handler, externalEvent);
        }
    }
}