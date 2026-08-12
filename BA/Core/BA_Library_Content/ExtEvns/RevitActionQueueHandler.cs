// File: BA.UI/ExternalEvents/RevitActionQueueHandler.cs
using Autodesk.Revit.UI;
using System;
using System.Collections.Concurrent;
using System.Windows.Threading;

namespace BA.UI.ExternalEvents
{
    /// <summary>
    /// ExternalEvent handler that executes queued API calls in Revit context,
    /// then marshals callbacks back to the WPF UI thread via Dispatcher.
    /// </summary>
    public sealed class RevitActionQueueHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<IJob> _queue = new();

        public Dispatcher Dispatcher { get; }

        public RevitActionQueueHandler(Dispatcher dispatcher)
        {
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Enqueue(
            Func<UIApplication, object?> apiFunc,
            Action<object?>? onCompleted,
            Action<Exception>? onError)
        {
            if (apiFunc == null) throw new ArgumentNullException(nameof(apiFunc));
            _queue.Enqueue(new Job(apiFunc, onCompleted, onError));
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out var job))
            {
                try
                {
                    var result = job.Run(app);

                    if (job.OnCompleted != null)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { job.OnCompleted(result); }
                            catch (Exception uiEx)
                            {
                                System.Diagnostics.Debug.WriteLine(uiEx);
                            }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    if (job.OnError != null)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { job.OnError(ex); }
                            catch (Exception uiEx)
                            {
                                System.Diagnostics.Debug.WriteLine(uiEx);
                            }
                        }));
                    }
                }
            }
        }

        public string GetName() => "BA | Revit Action Queue";

        // --------------------------
        // internal job abstraction
        // --------------------------
        private interface IJob
        {
            object? Run(UIApplication app);
            Action<object?>? OnCompleted { get; }
            Action<Exception>? OnError { get; }
        }

        private sealed class Job : IJob
        {
            private readonly Func<UIApplication, object?> _apiFunc;

            public Action<object?>? OnCompleted { get; }
            public Action<Exception>? OnError { get; }

            public Job(
                Func<UIApplication, object?> apiFunc,
                Action<object?>? onCompleted,
                Action<Exception>? onError)
            {
                _apiFunc = apiFunc;
                OnCompleted = onCompleted;
                OnError = onError;
            }

            public object? Run(UIApplication app) => _apiFunc(app);
        }
    }
}
