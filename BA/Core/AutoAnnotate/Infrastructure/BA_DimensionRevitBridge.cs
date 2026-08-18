using System;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace BA.BIM.Core.Dimensioning.Infrastructure
{
    /// <summary>
    /// Marshals a delegate from the WPF UI thread onto the Revit API thread via
    /// ExternalEvent, returning the result through a TaskCompletionSource. Create
    /// ONCE (BA_DimensionModule.Initialize, called from BaApplication.OnStartup)
    /// and reuse for the add-in session - ExternalEvent.Create must run on the
    /// Revit API thread, which OnStartup satisfies.
    ///
    /// One request in flight at a time by design: RunAsync throws
    /// InvalidOperationException if called again before the previous call's Task
    /// has completed, rather than silently dropping the earlier pending delegate.
    /// </summary>
    public sealed class BA_DimensionRevitBridge : IExternalEventHandler, IDisposable
    {
        private readonly ExternalEvent _externalEvent;
        private readonly object _lock = new object();
        private Func<UIApplication, object> _pendingAction;
        private TaskCompletionSource<object> _pendingTcs;

        public BA_DimensionRevitBridge()
        {
            _externalEvent = ExternalEvent.Create(this);
        }

        public Task<TResult> RunAsync<TResult>(Func<UIApplication, TResult> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                if (_pendingTcs != null && !_pendingTcs.Task.IsCompleted)
                    throw new InvalidOperationException(
                        "BA_DimensionRevitBridge: a request is already in flight. " +
                        "Await the previous RunAsync call before starting another.");

                _pendingAction = uiApp => (object)action(uiApp);
                _pendingTcs = tcs;
            }

            _externalEvent.Raise();

            return tcs.Task.ContinueWith(t => (TResult)t.Result, TaskScheduler.Default);
        }

        public void Execute(UIApplication app)
        {
            Func<UIApplication, object> action;
            TaskCompletionSource<object> tcs;

            lock (_lock)
            {
                action = _pendingAction;
                tcs = _pendingTcs;
                _pendingAction = null;
            }

            if (action == null || tcs == null) return;

            try { tcs.SetResult(action(app)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }

        public string GetName() => "BA_Tools Auto-Dimension External Event Bridge";

        public void Dispose() => _externalEvent?.Dispose();
    }
}