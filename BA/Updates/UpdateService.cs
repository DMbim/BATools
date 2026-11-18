// File: BA/Updates/UpdateService.cs
using Autodesk.Revit.UI;
using BA.BAApplication;
using System;
using System.Diagnostics;

namespace BA.Updates
{
    /// <summary>
    /// At the first Idling event after startup, starts the hidden installer waiter. The waiter
    /// does all update logic after Revit exits. This class does no network work and shows no UI.
    /// </summary>
    internal static class UpdateService
    {
        private static bool _registered;
        private static bool _launched;

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

        private static void OnFirstIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if (_launched) return;
            _launched = true;

            if (sender is not UIApplication uiapp)
                return;

            uiapp.Idling -= OnFirstIdling;

            try
            {
                // Read Revit API values here, on the UI thread, before anything async runs.
                var revitVersion = uiapp.Application.VersionNumber;
                var revitPid = Process.GetCurrentProcess().Id;

                // Fire and forget. Failures are logged inside and must never affect Revit.
                _ = InstallerLauncher.LaunchWaitAndCheckAsync(revitVersion, revitPid);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("UpdateService.OnFirstIdling", ex);
            }
        }
    }
}