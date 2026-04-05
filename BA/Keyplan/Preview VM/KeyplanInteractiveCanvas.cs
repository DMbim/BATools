using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Line = System.Windows.Shapes.Line;
using Ellipse = System.Windows.Shapes.Ellipse;
using Polygon = System.Windows.Shapes.Polygon;

namespace BA.UI.KeyplanGrid
{
    public sealed class KeyplanInteractiveCanvas : Canvas
    {
        private const double AxisHitThreshold = 8.0;

        private KeyplanGridPreviewData _data;
        private AxisPreviewInfo _dragAxis;

        public event EventHandler<PreviewCellClickEventArgs> CellPolygonClicked;
        public event Action<AxisOrientation, int, double> AxisDragged;

        public KeyplanInteractiveCanvas()
        {
            Background = Brushes.White;

            MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
            MouseLeftButtonUp += Canvas_MouseLeftButtonUp;
            MouseMove += Canvas_MouseMove;
        }

        public void RenderPreview(KeyplanGridPreviewData data)
        {
            _data = data;
            Children.Clear();

            if (data == null)
                return;

            DrawPrimaryFill(data);
            DrawFilledPolygons(data);
            DrawGridLines(data);
            DrawOutline(data);
            DrawAxisHandles(data);
        }

        private void DrawPrimaryFill(KeyplanGridPreviewData data)
        {

        }

        private void DrawFilledPolygons(KeyplanGridPreviewData data)
        {
            if (data.FilledPolygons == null || data.FilledPolygons.Count == 0)
                return;

            foreach (PreviewCellPolygon poly in data.FilledPolygons)
            {
                if (poly?.Points == null || poly.Points.Count < 3)
                    continue;

                Polygon shape = new Polygon
                {
                    StrokeThickness = poly.IsSelected ? 2.4 : 1.0
                };

                if (poly.IsExcluded)
                {
                    shape.Stroke = new SolidColorBrush(Color.FromRgb(180, 80, 80));
                    shape.Fill = new SolidColorBrush(Color.FromArgb(40, 220, 80, 80));
                }
                else if (poly.IsSelected)
                {
                    shape.Stroke = new SolidColorBrush(Color.FromRgb(255, 140, 0));
                    shape.Fill = new SolidColorBrush(Color.FromArgb(70, 255, 190, 90));
                }
                else
                {
                    shape.Stroke = new SolidColorBrush(Color.FromArgb(35, 120, 90, 180));
                    shape.Fill = Brushes.Transparent;
                }

                foreach (Point pt in poly.Points)
                    shape.Points.Add(pt);

                Children.Add(shape);
            }
        }

        private void DrawGridLines(KeyplanGridPreviewData data)
        {
            if (data.GridLines == null || data.GridLines.Count == 0)
                return;

            foreach ((Point A, Point B) line in data.GridLines)
            {
                Line l = new Line
                {
                    X1 = line.A.X,
                    Y1 = line.A.Y,
                    X2 = line.B.X,
                    Y2 = line.B.Y,
                    Stroke = new SolidColorBrush(Color.FromRgb(30, 50, 220)),
                    StrokeThickness = 1.4,
                    StrokeDashArray = new DoubleCollection { 8, 4, 1.5, 4 }
                };

                Children.Add(l);
            }
        }

        private void DrawOutline(KeyplanGridPreviewData data)
        {
            if (data.Outline == null || data.Outline.Count < 2)
                return;

            Polygon outline = new Polygon
            {
                Stroke = new SolidColorBrush(Color.FromRgb(245, 235, 40)),
                Fill = Brushes.Transparent,
                StrokeThickness = 3.0
            };

            foreach (Point pt in data.Outline)
                outline.Points.Add(pt);

            Children.Add(outline);
        }

        private void DrawAxisHandles(KeyplanGridPreviewData data)
        {
            if (data.Transform == null)
                return;

            double top = data.Transform.Padding;
            double bottom = data.Transform.CanvasHeight - data.Transform.Padding;
            double left = data.Transform.Padding;
            double right = data.Transform.CanvasWidth - data.Transform.Padding;

            foreach (AxisPreviewInfo axis in data.VerticalAxes ?? Enumerable.Empty<AxisPreviewInfo>())
            {
                Line l = new Line
                {
                    X1 = axis.CanvasPosition,
                    X2 = axis.CanvasPosition,
                    Y1 = top,
                    Y2 = bottom,
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 100, 255)),
                    StrokeThickness = 2.6,
                    Opacity = 0.35
                };
                Children.Add(l);

                Ellipse handle = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.White,
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 100, 255)),
                    StrokeThickness = 2
                };

                SetLeft(handle, axis.CanvasPosition - 5);
                SetTop(handle, top - 5);
                Children.Add(handle);
            }

            foreach (AxisPreviewInfo axis in data.HorizontalAxes ?? Enumerable.Empty<AxisPreviewInfo>())
            {
                Line l = new Line
                {
                    X1 = left,
                    X2 = right,
                    Y1 = axis.CanvasPosition,
                    Y2 = axis.CanvasPosition,
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 100, 255)),
                    StrokeThickness = 2.6,
                    Opacity = 0.35
                };
                Children.Add(l);

                Ellipse handle = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.White,
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 100, 255)),
                    StrokeThickness = 2
                };

                SetLeft(handle, left - 5);
                SetTop(handle, axis.CanvasPosition - 5);
                Children.Add(handle);
            }
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_data == null)
                return;

            Point p = e.GetPosition(this);

            _dragAxis = FindNearestAxis(p);
            if (_dragAxis != null)
            {
                CaptureMouse();
                Cursor = _dragAxis.Orientation == AxisOrientation.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
                e.Handled = true;
                return;
            }

            PreviewCellPolygon hitCell = FindTopmostCellPolygon(p);
            if (hitCell != null && !string.IsNullOrWhiteSpace(hitCell.CellKey))
            {
                CellPolygonClicked?.Invoke(this, new PreviewCellClickEventArgs(hitCell.CellKey));
                e.Handled = true;
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragAxis != null)
            {
                _dragAxis = null;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragAxis == null || _data?.Transform == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point p = e.GetPosition(this);
            double normalized = CanvasToNormalized(_dragAxis.Orientation, p, _data.Transform);

            AxisDragged?.Invoke(_dragAxis.Orientation, _dragAxis.InteriorIndex, normalized);
            e.Handled = true;
        }

        private PreviewCellPolygon FindTopmostCellPolygon(Point p)
        {
            if (_data?.FilledPolygons == null || _data.FilledPolygons.Count == 0)
                return null;

            for (int i = _data.FilledPolygons.Count - 1; i >= 0; i--)
            {
                PreviewCellPolygon poly = _data.FilledPolygons[i];
                if (poly?.Points == null || poly.Points.Count < 3)
                    continue;

                if (IsPointInPolygon(p, poly.Points))
                    return poly;
            }

            return null;
        }

        private AxisPreviewInfo FindNearestAxis(Point p)
        {
            AxisPreviewInfo best = null;
            double bestDist = double.MaxValue;

            foreach (AxisPreviewInfo axis in _data?.VerticalAxes ?? Enumerable.Empty<AxisPreviewInfo>())
            {
                double d = Math.Abs(p.X - axis.CanvasPosition);
                if (d < AxisHitThreshold && d < bestDist)
                {
                    best = axis;
                    bestDist = d;
                }
            }

            foreach (AxisPreviewInfo axis in _data?.HorizontalAxes ?? Enumerable.Empty<AxisPreviewInfo>())
            {
                double d = Math.Abs(p.Y - axis.CanvasPosition);
                if (d < AxisHitThreshold && d < bestDist)
                {
                    best = axis;
                    bestDist = d;
                }
            }

            return best;
        }

        private static bool IsPointInPolygon(Point testPoint, IList<Point> polygon)
        {
            bool inside = false;
            int count = polygon?.Count ?? 0;
            if (count < 3)
                return false;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Point pi = polygon[i];
                Point pj = polygon[j];

                bool intersect =
                    ((pi.Y > testPoint.Y) != (pj.Y > testPoint.Y)) &&
                    (testPoint.X < (pj.X - pi.X) * (testPoint.Y - pi.Y) / ((pj.Y - pi.Y) + 1e-12) + pi.X);

                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        private static double CanvasToNormalized(AxisOrientation orientation, Point p, PreviewTransformInfo t)
        {
            if (orientation == AxisOrientation.Vertical)
            {
                double modelX = t.ModelMinX + ((p.X - t.Padding) / t.Scale);
                double normalized = (modelX - t.ModelMinX) / (t.ModelMaxX - t.ModelMinX);
                return Clamp01(normalized);
            }
            else
            {
                double modelY = t.ModelMinY + (((t.CanvasHeight - t.Padding) - p.Y) / t.Scale);
                double normalized = (modelY - t.ModelMinY) / (t.ModelMaxY - t.ModelMinY);
                return Clamp01(normalized);
            }
        }

        private static double Clamp01(double v)
        {
            if (v < 0.0) return 0.0;
            if (v > 1.0) return 1.0;
            return v;
        }
    }
}