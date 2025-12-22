using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using BA.Core;

namespace BA.UI
{
    public static class LiveReviewHost
    {
        private static LiveReviewWindow _window;

        public static void ShowOrActivate(UIApplication uiApp, ChangeReport report = null)
        {
            if (_window == null)
            {
                _window = new LiveReviewWindow(uiApp, report);
                _window.Closed += (s, e) => _window = null;

                try
                {
                    var hwnd = uiApp?.MainWindowHandle ?? IntPtr.Zero;
                    if (hwnd != IntPtr.Zero)
                    {
                        var wih = new WindowInteropHelper(_window) { Owner = hwnd };
                        _window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        _window.ShowInTaskbar = false;
                    }
                }
                catch
                {
                    // no owner; still show
                }

                _window.Show();
            }
            else
            {
                if (_window.WindowState == WindowState.Minimized)
                    _window.WindowState = WindowState.Normal;

                _window.Activate();
                _window.Focus();
            }
        }
    }
}
