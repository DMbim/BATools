using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;

namespace BA.UI.Helpers
{
    public static class RevitWindowHelper
    {
        public static void SetOwner(Window window, UIApplication uiapp)
        {
            if (window == null || uiapp == null) return;
            var h = uiapp.MainWindowHandle;
            if (h == IntPtr.Zero) return;
            new WindowInteropHelper(window).Owner = h;
        }
    }
}
