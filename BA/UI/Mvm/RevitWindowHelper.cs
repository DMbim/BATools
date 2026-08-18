// File: BA.UI/Helpers/RevitWindowHelper.cs
using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Interop;

namespace BA.UI.Helpers
{
    public static class RevitWindowHelper
    {
        /// <summary>
        /// Makes a WPF window owned by the Revit main window (Win32 owner handle).
        /// This is the correct and stable way for Revit add-ins.
        /// </summary>
        public static void SetOwnerToRevit(Window window, UIApplication uiApp)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));

            var hwnd = uiApp.MainWindowHandle;
            if (hwnd == IntPtr.Zero)
                return; // rare, but don't crash

            // IMPORTANT: do NOT set window.Owner here.
            new WindowInteropHelper(window).Owner = hwnd;

            // optional UX tweaks
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        /// <summary>
        /// Convenience: set owner and show modal.
        /// </summary>
        public static bool? ShowDialogOwnedByRevit(Window window, UIApplication uiApp)
        {
            SetOwnerToRevit(window, uiApp);
            return window.ShowDialog();
        }
    }
}