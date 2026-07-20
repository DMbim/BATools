// File: BA.UI/WindowPositionFixer.cs
using Autodesk.Revit.UI;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BA.UI
{
    internal static class WindowPositionFixer
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        public static void CenterToRevitAndClamp(Window w, UIApplication uiApp)
        {
            if (w == null) return;

            // Ensure WPF window handle exists
            var interop = new WindowInteropHelper(w);
            if (interop.Handle == IntPtr.Zero)
            {
                // force handle creation
                var _ = new WindowInteropHelper(w).EnsureHandle();
            }

            // Revit main window handle (best), otherwise fallback to foreground window
            IntPtr revitHwnd = IntPtr.Zero;
            try
            {
                // UIApplication.MainWindowHandle exists in newer APIs; if not, fallback
                revitHwnd = uiApp?.MainWindowHandle ?? IntPtr.Zero;
            }
            catch { }

            if (revitHwnd == IntPtr.Zero)
                revitHwnd = GetForegroundWindow();

            // Determine monitor working area nearest to Revit window
            IntPtr monitor = MonitorFromWindow(revitHwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref mi))
                return;

            var work = mi.rcWork;
            double workLeft = work.Left;
            double workTop = work.Top;
            double workRight = work.Right;
            double workBottom = work.Bottom;

            // Center within working area
            double targetLeft = workLeft + (workRight - workLeft - w.Width) / 2.0;
            double targetTop = workTop + (workBottom - workTop - w.Height) / 2.0;

            // Clamp fully on screen (keep title bar visible)
            double minLeft = workLeft;
            double minTop = workTop;
            double maxLeft = workRight - w.Width;
            double maxTop = workBottom - w.Height;

            if (maxLeft < minLeft) maxLeft = minLeft;
            if (maxTop < minTop) maxTop = minTop;

            w.Left = Math.Max(minLeft, Math.Min(targetLeft, maxLeft));
            w.Top = Math.Max(minTop, Math.Min(targetTop, maxTop));
        }
    }
}