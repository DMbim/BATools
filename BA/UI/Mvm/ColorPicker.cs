using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace BA.UI.Helpers
{
    public static class ColorPicker
    {
        public static bool TryPickColor(System.Windows.Media.Color initial, out System.Windows.Media.Color picked)
        {
            using (var dialog = new ColorDialog())
            {
                dialog.Color = System.Drawing.Color.FromArgb(initial.A, initial.R, initial.G, initial.B);
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                dialog.SolidColorOnly = true;

                var owner = new RevitMainWindowHandle();
                var result = dialog.ShowDialog(owner);

                if (result == DialogResult.OK)
                {
                    var c = dialog.Color;
                    picked = System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);
                    return true;
                }

                picked = initial;
                return false;
            }
        }

        public static Autodesk.Revit.DB.Color ConvertToRevitColor(System.Windows.Media.Color color)
        {
            return new Autodesk.Revit.DB.Color(color.R, color.G, color.B);
        }

        private sealed class RevitMainWindowHandle : IWin32Window
        {
            public System.IntPtr Handle => Process.GetCurrentProcess().MainWindowHandle;
        }
    }
}