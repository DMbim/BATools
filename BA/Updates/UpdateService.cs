// File: BA/Updates/UpdateService.cs
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BA.Updates
{
    /// <summary>
    /// Split responsibility:
    ///  - Idling (once, at startup): throttled GitHub check, result cached in memory. No prompt.
    ///  - DocumentClosing (last open document only): shows the prompt from the cached result.
    ///    No network I/O here, so nothing can stall Revit's shutdown.
    ///  - ForceCheckAsync / HandleForceCheckResult: manual "Check for Updates" ribbon command.
    ///    Split into fetch (safe to call from Task.Run off the UI thread) and display (must run
    ///    on the UI thread) to avoid a UI-thread deadlock. See Cmd_CheckForUpdates.cs.
    /// </summary>
    internal static class UpdateService
    {
        private static bool _registered;
        private static bool _startupCheckRan;
        private static UpdateCheckResult? _cachedResult;

        public static void Register(UIControlledApplication app)
        {
            if (_registered) return;
            _registered = true;

            app.Idling += OnFirstIdling;
            app.ControlledApplication.DocumentClosing += OnDocumentClosing;
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (!_registered) return;
            _registered = false;

            app.Idling -= OnFirstIdling;
            app.ControlledApplication.DocumentClosing -= OnDocumentClosing;
        }

        private static async void OnFirstIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if (_startupCheckRan) return;
            _startupCheckRan = true;

            if (sender is not UIApplication uiapp)
                return;

            uiapp.Idling -= OnFirstIdling;

            try
            {
                var r = await UpdateCoordinator.CheckAsync(uiapp, force: false, CancellationToken.None)
                    .ConfigureAwait(true);

                if (r != null)
                    _cachedResult = r;
            }
            catch
            {
                // never break Revit startup
            }
        }

        // Fires when a document is about to close. Only treated as "Revit is exiting" when it
        // is the LAST open document (Documents.Size == 1 at this point, since e.Document hasn't
        // finished closing yet). This is an approximation: a user closing their last document
        // without also exiting Revit (landing on the home screen instead) will also trigger this.
        // There is no perfectly precise "about to exit Revit" hook available here; this is the
        // most reliable one that reliably supports showing a TaskDialog.
        private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
        {
            try
            {
                if (e.Document == null)
                    return;

                var fullApp = e.Document.Application; // Autodesk.Revit.ApplicationServices.Application
                if (fullApp.Documents.Size > 1)
                    return; // not the last open document

                TryPromptFromCache();
            }
            catch
            {
            }
        }

        internal static void TryPromptFromCache()
        {
            if (_cachedResult == null || !_cachedResult.HasUpdate)
                return;

            var state = UpdateStateStore.Load();
            if (!string.IsNullOrEmpty(state.DismissedVersion) &&
                state.DismissedVersion == _cachedResult.Tag)
            {
                return;
            }

            UpdateCoordinator.PromptAndHandle(_cachedResult);
        }

        /// <summary>
        /// Manual check triggered from the ribbon. Bypasses the throttle. Pure network I/O,
        /// no Revit API calls, no TaskDialog — safe to call from inside Task.Run off the UI
        /// thread. Callers must call HandleForceCheckResult afterward, back on the UI thread,
        /// to actually show anything. Do not call TaskDialog from here.
        /// </summary>
        public static async Task<UpdateCheckResult?> ForceCheckAsync(UIApplication uiapp)
        {
            var r = await UpdateCoordinator.CheckAsync(uiapp, force: true, CancellationToken.None)
                .ConfigureAwait(false);

            if (r != null)
                _cachedResult = r;

            return r;
        }

        /// <summary>
        /// Shows the result of a forced check. Must be called on the Revit UI thread
        /// (calls TaskDialog / Revit API). Pass the value returned by ForceCheckAsync.
        /// </summary>
        public static void HandleForceCheckResult(UpdateCheckResult? r)
        {
            var result = r ?? _cachedResult;

            if (result != null && result.HasUpdate)
            {
                UpdateCoordinator.PromptAndHandle(result);
            }
            else
            {
                var installed = result?.Installed
                    ?? VersionUtil.GetInstalledVersion(typeof(UpdateService).Assembly);

                TaskDialog.Show("BA Tools Update",
                    $"You're up to date.\n\nInstalled version: {installed}");
            }
        }
    }
}