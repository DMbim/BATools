using Autodesk.Revit.DB;
using System.Windows.Media;

namespace BA.Core.Graphics
{
    public static class RevitColorUtils
    {
        public static Autodesk.Revit.DB.Color ToRevitColor(System.Windows.Media.Color c)
            => new Autodesk.Revit.DB.Color(c.R, c.G, c.B);

        public static System.Windows.Media.Color ToWpfColor(Autodesk.Revit.DB.Color c)
            => System.Windows.Media.Color.FromRgb(c.Red, c.Green, c.Blue);

        public static bool TryGetProjectionLineColor(OverrideGraphicSettings ogs, out Autodesk.Revit.DB.Color color)
        {
            color = null;
            try
            {
                var c = ogs.ProjectionLineColor;
                if (c == null) return false;
                color = c;
                return true;
            }
            catch { return false; }
        }

        public static bool TryGetCutLineColor(OverrideGraphicSettings ogs, out Autodesk.Revit.DB.Color color)
        {
            color = null;
            try
            {
                var c = ogs.CutLineColor;
                if (c == null) return false;
                color = c;
                return true;
            }
            catch { return false; }
        }

        public static void SetProjectionLineColor(OverrideGraphicSettings ogs, Autodesk.Revit.DB.Color color)
        {
            if (ogs == null || color == null) return;
            ogs.SetProjectionLineColor(color);
        }

        public static void SetCutLineColor(OverrideGraphicSettings ogs, Autodesk.Revit.DB.Color color)
        {
            if (ogs == null || color == null) return;
            ogs.SetCutLineColor(color);
        }
    }
}
