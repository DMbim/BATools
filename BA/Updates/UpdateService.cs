// File: BA/Updates/UpdateService.cs
using Autodesk.Revit.UI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BA.Updates
{
    /// <summary>
    /// Responsibilities:
    ///  - OnFirstIdling (once, at startup): launches the hidden wait-and-check installer
    ///    process (see InstallerLauncher.LaunchWaitAndCheck), which is the sole mechanism for
    ///    automatic close time updates. Also runs the throttled UpdateCoordinator.CheckAsync
    ///    to populate _cachedResult, used only as a fallback for the manual ribbon dialog's
    ///    "you're up to date" message.
    ///  - ForceCheckAsync / HandleForceCheckResult: manual "Check for Updates" ribbon command.
    ///    Split into fetch (safe to call from Task.Run off the UI thread) and display (must run
    ///    on the UI thread) to avoid a UI-thread deadlock. See Cmd_CheckForUpdates.cs. This path
    ///    only ever notifies via UpdateCoordinator.NotifyOnly, it never launches the installer.
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
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (!_registered) return;
            _registered = false;

            app.Idling -= OnFirstIdling;
        }

        private static async void OnFirstIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if (_startupCheckRan) return;
            _startupCheckRan = true;

            if (sender is not UIApplication uiapp)
                return;
            uiapp.Idling -= OnFirstIdling;

            // Launch the hidden waiter immediately, regardless of what the check below finds.
            // It self-heals BATools-Installer.exe if missing, then waits until Revit exits and
            // does its own live GitHub check at that exact moment, opening a window only if a
            // newer version actually exists then. Fire-and-forget: this must not block or delay
            // the rest of startup, and any failure inside it is already logged via AppLogger.
            _ = InstallerLauncher.LaunchWaitAndCheckAsync(uiapp);

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