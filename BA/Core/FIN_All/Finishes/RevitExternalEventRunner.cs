using Autodesk.Revit.UI;
using System;
using System.Threading;

namespace BA.UI.Core.Finishes
{
    /// <summary>
    /// Minimal external event runner: queue an Action to run in Revit API context.
    /// </summary>
    public sealed class RevitExternalEventRunner : IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _exEvent;
        private readonly Handler _handler;

        public RevitExternalEventRunner(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _handler = new Handler();
            _exEvent = ExternalEvent.Create(_handler);
        }

        public void Raise(string title, Action<UIApplication> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            _handler.Set(title, () => action(_uiApp));
            _exEvent.Raise();
        }

        public void Dispose()
        {
            // nothing explicit required
        }

        private sealed class Handler : IExternalEventHandler
        {
            private readonly object _lock = new();
            private string _title = "BA";
            private Action? _action;

            public void Set(string title, Action action)
            {
                lock (_lock)
                {
                    _title = string.IsNullOrWhiteSpace(title) ? "BA" : title;
                    _action = action ?? throw new ArgumentNullException(nameof(action));
                }
            }

            public void Execute(UIApplication app)
            {
                Action? a;
                string t;
                lock (_lock)
                {
                    a = _action;
                    t = _title;
                    _action = null;
                }

                if (a == null) return;

                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show(t, ex.ToString());
                }
            }

            public string GetName() => "BA ExternalEvent Runner";
        }
    }
}