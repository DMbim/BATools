using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Interop;

namespace BA.UI.Helpers
{
    public static class RevitWindowHelper
    {
        public static void SetOwnerToRevit(Window window, UIApplication uiapp)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (uiapp == null) throw new ArgumentNullException(nameof(uiapp));

            var helper = new WindowInteropHelper(window);
            helper.Owner = uiapp.MainWindowHandle;
        }
    }
}
