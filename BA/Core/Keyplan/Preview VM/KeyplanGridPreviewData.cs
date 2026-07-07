using System.Collections.Generic;
using System.Windows;
using Point = System.Windows.Point;

namespace BA.UI.KeyplanGrid
{
    public sealed class KeyplanGridPreviewData
    {
        public List<Point> PrimaryFillOutline { get; set; } = new List<Point>();
        public List<Point> Outline { get; set; } = new List<Point>();

        public List<PreviewCellPolygon> FilledPolygons { get; set; } = new List<PreviewCellPolygon>();

        public List<(Point A, Point B)> GridLines { get; set; } = new List<(Point A, Point B)>();

        public List<AxisPreviewInfo> VerticalAxes { get; set; } = new List<AxisPreviewInfo>();
        public List<AxisPreviewInfo> HorizontalAxes { get; set; } = new List<AxisPreviewInfo>();

        public PreviewTransformInfo Transform { get; set; } = new PreviewTransformInfo();
    }
}