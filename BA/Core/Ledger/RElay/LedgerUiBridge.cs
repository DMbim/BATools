using System;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Bridges the non-modal LedgerSettingsWindow (WPF thread) to the Revit API thread using
    /// the standard ExternalEvent pattern. Two separate ExternalEvents: one read-only
    /// (diagnostics refresh), one write-capable (setting the manual central identifier, which
    /// needs its own Transaction). Kept separate rather than one handler doing both, so a
    /// write request can never accidentally piggyback on a stale read-only Raise() or vice
    /// versa.
    ///
    /// EnsureInitialized() MUST be called from a valid Revit API context (IExternalCommand.Execute)
    /// the first time, since ExternalEvent.Create() requires the Revit API thread.
    /// </summary>
    public static class LedgerUiBridge
    {
        private static ExternalEvent _refreshEvent;
        private static readonly RefreshHandler RefreshHandlerInstance = new RefreshHandler();

        private static ExternalEvent _setIdentifierEvent;
        private static readonly SetIdentifierHandler SetIdentifierHandlerInstance = new SetIdentifierHandler();

        public static void EnsureInitialized()
        {
            if (_refreshEvent == null)
            {
                _refreshEvent = ExternalEvent.Create(RefreshHandlerInstance);
            }

            if (_setIdentifierEvent == null)
            {
                _setIdentifierEvent = ExternalEvent.Create(SetIdentifierHandlerInstance);
            }
        }

        /// <summary>
        /// Requests a diagnostics refresh against the active document. onComplete is invoked
        /// on the calling (WPF) thread's Dispatcher once the Revit-thread computation finishes.
        /// </summary>
        public static void RequestRefresh(Action<LedgerDiagnosticsResult> onComplete)
        {
            if (_refreshEvent == null)
            {
                AppLogger.LogError("LedgerUiBridge.RequestRefresh: EnsureInitialized() was never called", null);
                return;
            }

            Dispatcher callingDispatcher = Application.Current?.Dispatcher;
            RefreshHandlerInstance.PendingCallback = result =>
            {
                if (callingDispatcher != null)
                {
                    callingDispatcher.BeginInvoke(new Action(() => onComplete(result)));
                }
                else
                {
                    onComplete(result);
                }
            };

            _refreshEvent.Raise();
        }

        /// <summary>
        /// Sets the manual central identifier on the active document's ProjectInformation
        /// element inside a real Transaction. onComplete(true) on success, onComplete(false)
        /// if it failed (see the log for why).
        /// </summary>
        public static void RequestSetCentralIdentifier(string identifier, Action<bool> onComplete)
        {
            if (_setIdentifierEvent == null)
            {
                AppLogger.LogError("LedgerUiBridge.RequestSetCentralIdentifier: EnsureInitialized() was never called", null);
                onComplete?.Invoke(false);
                return;
            }

            Dispatcher callingDispatcher = Application.Current?.Dispatcher;
            SetIdentifierHandlerInstance.PendingIdentifier = identifier;
            SetIdentifierHandlerInstance.PendingCallback = success =>
            {
                if (callingDispatcher != null)
                {
                    callingDispatcher.BeginInvoke(new Action(() => onComplete?.Invoke(success)));
                }
                else
                {
                    onComplete?.Invoke(success);
                }
            };

            _setIdentifierEvent.Raise();
        }

        private class RefreshHandler : IExternalEventHandler
        {
            public Action<LedgerDiagnosticsResult> PendingCallback;

            public void Execute(UIApplication app)
            {
                Action<LedgerDiagnosticsResult> callback = PendingCallback;
                PendingCallback = null;

                try
                {
                    Document doc = app.ActiveUIDocument?.Document;
                    LedgerDiagnosticsResult result = LedgerDiagnosticsService.Compute(doc);
                    callback?.Invoke(result);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("LedgerUiBridge.RefreshHandler.Execute failed", ex);
                    callback?.Invoke(new LedgerDiagnosticsResult());
                }
            }

            public string GetName() => "BA Ledger Settings Diagnostics Refresh";
        }

        private class SetIdentifierHandler : IExternalEventHandler
        {
            public string PendingIdentifier;
            public Action<bool> PendingCallback;

            public void Execute(UIApplication app)
            {
                string identifier = PendingIdentifier;
                Action<bool> callback = PendingCallback;
                PendingIdentifier = null;
                PendingCallback = null;

                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    AppLogger.LogError("LedgerUiBridge.SetIdentifierHandler.Execute: no active document", null);
                    callback?.Invoke(false);
                    return;
                }

                try
                {
                    using (var tx = new Transaction(doc, "Set Ledger Central Identifier"))
                    {
                        tx.Start();
                        CentralIdentifierService.SetIdentifier(doc, identifier);
                        tx.Commit();
                    }

                    AppLogger.LogInfo($"LedgerUiBridge.SetIdentifierHandler: identifier set to '{identifier}' on '{doc.Title}'.");
                    callback?.Invoke(true);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("LedgerUiBridge.SetIdentifierHandler.Execute failed", ex);
                    callback?.Invoke(false);
                }
            }

            public string GetName() => "BA Ledger Settings Set Central Identifier";
        }
    }
}
