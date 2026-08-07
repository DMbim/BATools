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
    ///  - DocumentClosing (last open document only): if the cached result has an update and it
    ///    was not previously skipped, launches the installer window directly with no dialog.
    ///    No network I/O here, so nothing can stall Revit's shutdown.
    ///  - ForceCheckAsync / HandleForceCheckResult: manual "Check for Updates" ribbon command.
    ///    Split into fetch (safe to call from Task.Run off the UI thread) and display (must run
    ///    on the UI thread) to avoid a UI-thread deadlock. See Cmd_CheckForUpdates.cs. This path
    ///    only ever notifies, it never launches the installer itself.
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
        // without also exiting Revit (landing on the home screen instead) will also trigger this,
        // and will see the installer window pop up over the home screen. The installer's own
        // WaitPid logic still blocks on the real Revit process exit before touching any files,
        // so this is cosmetically odd at worst, not unsafe.
        private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
        {
            try
            {
                if (e.Document == null)
                    return;

                var fullApp = e.Document.Application; // Autodesk.Revit.ApplicationServices.Application
                if (fullApp.Documents.Size > 1)
                    return; // not the last open document

                TryAutoLaunchFromCache();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Called only from OnDocumentClosing. If the cached result has an update and that
        /// exact version was not previously skipped via NotifyOnly's "Skip this version" link,
        /// launches the installer window directly. No dialog is shown here.
        /// </summary>
        internal static void TryAutoLaunchFromCache()
        {
            if (_cachedResult == null || !_cachedResult.HasUpdate)
                return;

            var state = UpdateStateStore.Load();
            if (!string.IsNullOrEmpty(state.DismissedVersion) &&
                state.DismissedVersion == _cachedResult.Tag)
            {
                return;
            }

            UpdateCoordinator.AutoLaunchOnClose(_cachedResult);
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
        /// This path only ever notifies via UpdateCoordinator.NotifyOnly, it never launches
        /// the installer directly regardless of what the person chooses in that dialog.
        /// </summary>
        public static void HandleForceCheckResult(UpdateCheckResult? r)
        {
            var result = r ?? _cachedResult;

            if (result != null && result.HasUpdate)
            {
                UpdateCoordinator.NotifyOnly(result);
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