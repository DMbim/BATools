using System.Collections.Generic;
using System.Windows;
using Point = System.Windows.Point;

namespace BA.UI.KeyplanGrid
{
    public sealed class PreviewCellPolygon
    {
        public string CellKey { get; set; } = string.Empty;
        public int XIndex { get; set; }
        public int YIndex { get; set; }

        public bool IsExcluded { get; set; }
        public bool IsSelected { get; set; }

        public List<Point> Points { get; set; } = new List<Point>();
    }
}