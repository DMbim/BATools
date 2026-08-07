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
    /// the standard ExternalEvent pattern. Four separate ExternalEvents: one read-only
    /// (diagnostics refresh), and three write-capable (setting the manual central identifier,
    /// setting the manual project set override, and setting the per-central Ledger
    /// enabled/disabled flag), each needing its own Transaction. Kept separate rather than
    /// combining, so a write request can never accidentally piggyback on a stale read-only
    /// Raise() or vice versa.
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

        private static ExternalEvent _setProjectSetEvent;
        private static readonly SetProjectSetHandler SetProjectSetHandlerInstance = new SetProjectSetHandler();

        private static ExternalEvent _setLedgerEnabledEvent;
        private static readonly SetLedgerEnabledHandler SetLedgerEnabledHandlerInstance = new SetLedgerEnabledHandler();

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

            if (_setProjectSetEvent == null)
            {
                _setProjectSetEvent = ExternalEvent.Create(SetProjectSetHandlerInstance);
            }

            if (_setLedgerEnabledEvent == null)
            {
                _setLedgerEnabledEvent = ExternalEvent.Create(SetLedgerEnabledHandlerInstance);
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

        /// <summary>
        /// Sets the manual Project Set override on the active document's ProjectInformation
        /// element inside a real Transaction. Passing null or empty clears the override,
        /// reverting the document to auto-detection from its central file path. onComplete(true)
        /// on success, onComplete(false) if it failed (see the log for why).
        /// </summary>
        public static void RequestSetProjectSet(string projectSetName, Action<bool> onComplete)
        {
            if (_setProjectSetEvent == null)
            {
                AppLogger.LogError("LedgerUiBridge.RequestSetProjectSet: EnsureInitialized() was never called", null);
                onComplete?.Invoke(false);
                return;
            }

            Dispatcher callingDispatcher = Application.Current?.Dispatcher;
            SetProjectSetHandlerInstance.PendingProjectSetName = projectSetName;
            SetProjectSetHandlerInstance.PendingCallback = success =>
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

            _setProjectSetEvent.Raise();
        }

        /// <summary>
        /// Sets the per-central Ledger sync enabled/disabled flag on the active document's
        /// ProjectInformation element inside a real Transaction. onComplete(true) on success,
        /// onComplete(false) if it failed (see the log for why).
        /// </summary>
        public static void RequestSetLedgerEnabled(bool enabled, Action<bool> onComplete)
        {
            if (_setLedgerEnabledEvent == null)
            {
                AppLogger.LogError("LedgerUiBridge.RequestSetLedgerEnabled: EnsureInitialized() was never called", null);
                onComplete?.Invoke(false);
                return;
            }

            Dispatcher callingDispatcher = Application.Current?.Dispatcher;
            SetLedgerEnabledHandlerInstance.PendingEnabled = enabled;
            SetLedgerEnabledHandlerInstance.PendingCallback = success =>
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

            _setLedgerEnabledEvent.Raise();
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

        private class SetProjectSetHandler : IExternalEventHandler
        {
            public string PendingProjectSetName;
            public Action<bool> PendingCallback;

            public void Execute(UIApplication app)
            {
                string projectSetName = PendingProjectSetName;
                Action<bool> callback = PendingCallback;
                PendingProjectSetName = null;
                PendingCallback = null;

                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    AppLogger.LogError("LedgerUiBridge.SetProjectSetHandler.Execute: no active document", null);
                    callback?.Invoke(false);
                    return;
                }

                try
                {
                    using (var tx = new Transaction(doc, "Set Ledger Project Set"))
                    {
                        tx.Start();
                        ProjectSetService.SetProjectSetName(doc, projectSetName);
                        tx.Commit();
                    }

                    AppLogger.LogInfo($"LedgerUiBridge.SetProjectSetHandler: project set override set to '{projectSetName}' on '{doc.Title}'.");
                    callback?.Invoke(true);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("LedgerUiBridge.SetProjectSetHandler.Execute failed", ex);
                    callback?.Invoke(false);
                }
            }

            public string GetName() => "BA Ledger Settings Set Project Set";
        }

        private class SetLedgerEnabledHandler : IExternalEventHandler
        {
            public bool PendingEnabled;
            public Action<bool> PendingCallback;

            public void Execute(UIApplication app)
            {
                bool enabled = PendingEnabled;
                Action<bool> callback = PendingCallback;
                PendingCallback = null;

                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    AppLogger.LogError("LedgerUiBridge.SetLedgerEnabledHandler.Execute: no active document", null);
                    callback?.Invoke(false);
                    return;
                }

                try
                {
                    using (var tx = new Transaction(doc, "Set Ledger Sync Enabled"))
                    {
                        tx.Start();
                        LedgerEnabledService.SetEnabled(doc, enabled);
                        tx.Commit();
                    }

                    AppLogger.LogInfo($"LedgerUiBridge.SetLedgerEnabledHandler: Ledger sync enabled set to '{enabled}' on '{doc.Title}'.");
                    callback?.Invoke(true);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("LedgerUiBridge.SetLedgerEnabledHandler.Execute failed", ex);
                    callback?.Invoke(false);
                }
            }

            public string GetName() => "BA Ledger Settings Set Sync Enabled";
        }
    }
}