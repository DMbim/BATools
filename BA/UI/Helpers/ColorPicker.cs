// File: BA.UI/Helpers/ColorPicker.cs
using System.Windows.Media;
using Autodesk.Revit.DB;

namespace BA.UI.Helpers
{
    public static class ColorPicker
    {
        // simple placeholder (no dependencies). Replace with real picker later.
        public static bool TryPickWpfColor(System.Windows.Media.Color initial, out System.Windows.Media.Color picked)
        {
            var options = new[]
            {
                System.Windows.Media.Color.FromRgb(20,20,20),
                System.Windows.Media.Color.FromRgb(70,70,70),
                System.Windows.Media.Color.FromRgb(120,120,120),
                System.Windows.Media.Color.FromRgb(160,160,160),
                System.Windows.Media.Color.FromRgb(200,200,200),
                System.Windows.Media.Color.FromRgb(235,235,235),
            };

            int idx = 0;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].R == initial.R && options[i].G == initial.G && options[i].B == initial.B)
                {
                    idx = (i + 1) % options.Length;
                    break;
                }
            }

            picked = options[idx];
            return true;
        }

        public static bool TryPickColor(System.Windows.Media.Color initial, out System.Windows.Media.Color picked)
        {
            // Placeholder implementation for color picking logic
            picked = initial;
            return true;
        }

        public static Autodesk.Revit.DB.Color ConvertToRevitColor(System.Windows.Media.Color color)
        {
            return new Autodesk.Revit.DB.Color(color.R, color.G, color.B);
        }
    }
}
