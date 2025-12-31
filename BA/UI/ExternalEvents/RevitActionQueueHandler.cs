using Autodesk.Revit.UI;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BA.UI.ExternalEvents
{
    public sealed class RevitActionQueueHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<IRevitRequest> _queue = new();

        internal void Enqueue(IRevitRequest req) => _queue.Enqueue(req);

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out var req))
            {
                try { req.Execute(app); }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
        }

        public string GetName() => "BA Revit Action Queue Handler";

        internal interface IRevitRequest
        {
            void Execute(UIApplication app);
        }

        internal sealed class RevitRequest<T> : IRevitRequest
        {
            private readonly Func<UIApplication, T> _work;
            private readonly Action<T> _onOk;
            private readonly Action<Exception> _onErr;

            public RevitRequest(Func<UIApplication, T> work, Action<T> onOk, Action<Exception> onErr)
            {
                _work = work;
                _onOk = onOk;
                _onErr = onErr;
            }

            public void Execute(UIApplication app)
            {
                try { _onOk(_work(app)); }
                catch (Exception ex) { _onErr(ex); }
            }
        }
    }
}
