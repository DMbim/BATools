using Autodesk.Revit.UI;
using System;
using System.Windows.Threading;

namespace BA.UI.ExternalEvents
{
    public sealed class RevitExternalInvoker
    {
        private readonly RevitActionQueueHandler _handler;
        private readonly ExternalEvent _extEvent;
        private readonly Dispatcher _dispatcher;

        public RevitExternalInvoker(RevitActionQueueHandler handler, ExternalEvent extEvent, Dispatcher dispatcher)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _extEvent = extEvent ?? throw new ArgumentNullException(nameof(extEvent));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Invoke<T>(Func<UIApplication, T> work, Action<T> onSuccess, Action<Exception> onError)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (onSuccess == null) throw new ArgumentNullException(nameof(onSuccess));
            if (onError == null) throw new ArgumentNullException(nameof(onError));

            void Ok(T result) => _dispatcher.BeginInvoke(new Action(() => onSuccess(result)));
            void Err(Exception ex) => _dispatcher.BeginInvoke(new Action(() => onError(ex)));

            var req = new RevitActionQueueHandler.RevitRequest<T>(work, Ok, Err);
            _handler.Enqueue(req);

            _extEvent.Raise();
        }
    }
}
