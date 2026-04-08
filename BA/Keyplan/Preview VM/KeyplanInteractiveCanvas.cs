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
        // -------------------------------------------------------------------------
        // Constants
        // -------------------------------------------------------------------------

        private const double AxisHitThreshold = 8.0;
        private const double HandleRadius = 6.0;

        // Zone pick role colours.
        private static readonly Color _zoneFirstColour = Color.FromRgb(30, 160, 70);   // green
        private static readonly Color _zoneSecondColour = Color.FromRgb(30, 100, 230);  // blue
        private static readonly Color _zoneLastColour = Color.FromRgb(210, 50, 50);  // red
        private static readonly Color _zoneInRangeColour = Color.FromRgb(255, 140, 0);  // orange

        // -------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------

        private KeyplanGridPreviewData _data;
        private AxisPreviewInfo _dragAxis;
        private bool _dragStarted;

        /// <summary>
        /// When true, cell clicks are routed to the zone label session rather than
        /// normal selection.  Cursor changes to indicate pick mode.
        /// </summary>
        public bool ZoneLabelPickModeActive { get; set; }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event EventHandler<PreviewCellClickEventArgs> CellPolygonClicked;
        public event EventHandler<PreviewAxisClickEventArgs> AxisClicked;
        public event EventHandler<PreviewAxisEventArgs> AxisDragged;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public KeyplanInteractiveCanvas()
        {
            Background = Brushes.White;

            MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
            MouseLeftButtonUp += Canvas_MouseLeftButtonUp;
            MouseMove += Canvas_MouseMove;
        }

        // -------------------------------------------------------------------------
        // Public render entry point
        // -------------------------------------------------------------------------

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

            // Show a semi-transparent overlay banner when in zone pick mode.
            if (ZoneLabelPickModeActive)
                DrawZonePickModeBanner();
        }

        // -------------------------------------------------------------------------
        // Draw methods
        // -------------------------------------------------------------------------

        private void DrawPrimaryFill(KeyplanGridPreviewData data)
        {
            // Reserved for future primary-fill overlay.
        }

        private void DrawFilledPolygons(KeyplanGridPreviewData data)
        {
            if (data.FilledPolygons == null || data.FilledPolygons.Count == 0)
                return;

            foreach (PreviewCellPolygon poly in data.FilledPolygons)
            {
                if (poly?.Points == null || poly.Points.Count < 3)
                    continue;

                Polygon shape = BuildFilledPolygonShape(poly);
                Children.Add(shape);

                // If a zone label has been committed, draw it at the centroid.
                if (!string.IsNullOrWhiteSpace(poly.ZoneLabel))
                    Children.Add(BuildZoneLabelText(poly));
            }
        }

        private Polygon BuildFilledPolygonShape(PreviewCellPolygon poly)
        {
            Polygon shape = new Polygon();

            // Zone pick mode overrides normal selection colours.
            if (poly.ZonePickRole != ZonePickRole.None)
            {
                ApplyZonePickStyle(shape, poly.ZonePickRole, poly.IsSelected);
            }
            else if (poly.IsExcluded)
            {
                shape.Stroke = new SolidColorBrush(Color.FromRgb(180, 80, 80));
                shape.Fill = new SolidColorBrush(Color.FromArgb(40, 220, 80, 80));
                shape.StrokeThickness = 1.0;
            }
            else if (poly.IsSelected)
            {
                shape.Stroke = new SolidColorBrush(Color.FromRgb(255, 140, 0));
                shape.Fill = new SolidColorBrush(Color.FromArgb(70, 255, 190, 90));
                shape.StrokeThickness = 2.4;
            }
            else
            {
                shape.Stroke = new SolidColorBrush(Color.FromArgb(35, 120, 90, 180));
                shape.Fill = Brushes.Transparent;
                shape.StrokeThickness = 1.0;
            }

            foreach (Point pt in poly.Points)
                shape.Points.Add(pt);

            return shape;
        }

        private static void ApplyZonePickStyle(Polygon shape, ZonePickRole role, bool isSelected)
        {
            Color baseColor;

            switch (role)
            {
                case ZonePickRole.First: baseColor = _zoneFirstColour; break;
                case ZonePickRole.Second: baseColor = _zoneSecondColour; break;
                case ZonePickRole.Last: baseColor = _zoneLastColour; break;
                case ZonePickRole.InRange: baseColor = _zoneInRangeColour; break;
                default: baseColor = Color.FromRgb(120, 120, 120); break;
            }

            shape.Stroke = new SolidColorBrush(baseColor);
            shape.Fill = new SolidColorBrush(Color.FromArgb(60, baseColor.R, baseColor.G, baseColor.B));
            shape.StrokeThickness = isSelected ? 3.0 : 2.2;

            // Dashed stroke for InRange regions so they look "pending".
            if (role == ZonePickRole.InRange)
                shape.StrokeDashArray = new DoubleCollection { 6, 3 };
        }

        private static System.Windows.Controls.TextBlock BuildZoneLabelText(PreviewCellPolygon poly)
        {
            // Approximate centroid from canvas points.
            double cx = poly.Points.Average(p => p.X);
            double cy = poly.Points.Average(p => p.Y);

            System.Windows.Controls.TextBlock tb = new System.Windows.Controls.TextBlock
            {
                Text = poly.ZoneLabel,
                Foreground = Brushes.White,
                FontSize = 11.0,
                FontWeight = System.Windows.FontWeights.Bold,
                IsHitTestVisible = false
            };

            SetLeft(tb, cx - 6.0);
            SetTop(tb, cy - 8.0);

            return tb;
        }

        private void DrawZonePickModeBanner()
        {
            // Thin coloured border around the entire canvas to signal pick mode.
            System.Windows.Shapes.Rectangle border = new System.Windows.Shapes.Rectangle
            {
                Width = ActualWidth > 0 ? ActualWidth : 800,
                Height = ActualHeight > 0 ? ActualHeight : 600,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 140, 0)),
                StrokeThickness = 3.0,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            SetLeft(border, 0);
            SetTop(border, 0);
            Children.Add(border);
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
                if (!axis.IsEnabled || double.IsNaN(axis.CanvasPosition))
                    continue;

                Brush stroke = axis.IsSelected
                    ? new SolidColorBrush(Color.FromRgb(255, 140, 0))
                    : new SolidColorBrush(Color.FromRgb(0, 100, 255));

                Children.Add(new Line
                {
                    X1 = axis.CanvasPosition,
                    X2 = axis.CanvasPosition,
                    Y1 = top,
                    Y2 = bottom,
                    Stroke = stroke,
                    StrokeThickness = axis.IsSelected ? 3.5 : 2.6,
                    Opacity = axis.IsSelected ? 0.65 : 0.35
                });

                Ellipse handle = new Ellipse
                {
                    Width = HandleRadius * 2.0,
                    Height = HandleRadius * 2.0,
                    Fill = Brushes.White,
                    Stroke = stroke,
                    StrokeThickness = axis.IsSelected ? 2.4 : 2.0
                };
                SetLeft(handle, axis.CanvasPosition - HandleRadius);
                SetTop(handle, top - HandleRadius);
                Children.Add(handle);
            }

            foreach (AxisPreviewInfo axis in data.HorizontalAxes ?? Enumerable.Empty<AxisPreviewInfo>())
            {
                if (!axis.IsEnabled || double.IsNaN(axis.CanvasPosition))
                    continue;

                Brush stroke = axis.IsSelected
                    ? new SolidColorBrush(Color.FromRgb(255, 140, 0))
                    : new SolidColorBrush(Color.FromRgb(0, 100, 255));

                Line l = new Line
                {
                    X1 = left,
                    X2 = right,
                    Y1 = axis.CanvasPosition,
                    Y2 = axis.CanvasPosition,
                    Stroke = stroke,
                    StrokeThickness = axis.IsSelected ? 3.5 : 2.6,
                    Opacity = axis.IsSelected ? 0.65 : 0.35
                };
                Children.Add(l);

                Ellipse handle = new Ellipse
                {
                    Width = HandleRadius * 2.0,
                    Height = HandleRadius * 2.0,
                    Fill = Brushes.White,
                    Stroke = stroke,
                    StrokeThickness = axis.IsSelected ? 2.4 : 2.0
                };

                SetLeft(handle, left - HandleRadius);
                SetTop(handle, axis.CanvasPosition - HandleRadius);
                Children.Add(handle);
            }
        }

        // -------------------------------------------------------------------------
        // Mouse handlers
        // -------------------------------------------------------------------------

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_data == null) return;

            Point p = e.GetPosition(this);

            // In zone pick mode, axis drag is suppressed — only cell clicks matter.
            if (!ZoneLabelPickModeActive)
            {
                _dragAxis = FindNearestAxis(p);
                _dragStarted = false;

                if (_dragAxis != null)
                {
                    AxisClicked?.Invoke(this,
                        new PreviewAxisClickEventArgs(_dragAxis.SplitId, _dragAxis.Orientation));

                    CaptureMouse();
                    Cursor = _dragAxis.Orientation == AxisOrientation.Vertical
                        ? Cursors.SizeWE
                        : Cursors.SizeNS;
                    e.Handled = true;
                    return;
                }
            }

            PreviewCellPolygon hitCell = FindTopmostCellPolygon(p);
            if (hitCell != null && !string.IsNullOrWhiteSpace(hitCell.CellKey))
            {
                CellPolygonClicked?.Invoke(this,
                    new PreviewCellClickEventArgs(hitCell.CellKey));
                e.Handled = true;
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragAxis != null)
            {
                _dragAxis = null;
                _dragStarted = false;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragAxis == null ||
                _data?.Transform == null ||
                e.LeftButton != MouseButtonState.Pressed)
                return;

            Point p = e.GetPosition(this);
            double normalized = CanvasToNormalized(_dragAxis.Orientation, p, _data.Transform);

            _dragStarted = true;
            AxisDragged?.Invoke(this,
                new PreviewAxisEventArgs(_dragAxis.SplitId, _dragAxis.Orientation, normalized));
            e.Handled = true;
        }

        // -------------------------------------------------------------------------
        // Hit testing
        // -------------------------------------------------------------------------

        private PreviewCellPolygon FindTopmostCellPolygon(Point p)
        {
            if (_data?.FilledPolygons == null || _data.FilledPolygons.Count == 0)
                return null;

            for (int i = _data.FilledPolygons.Count - 1; i >= 0; i--)
            {
                PreviewCellPolygon poly = _data.FilledPolygons[i];
                if (poly?.Points == null || poly.Points.Count < 3) continue;
                if (IsPointInPolygon(p, poly.Points)) return poly;
            }

            return null;
        }

        private AxisPreviewInfo FindNearestAxis(Point p)
        {
            AxisPreviewInfo best = null;
            double bestDist = double.MaxValue;

            foreach (AxisPreviewInfo axis in _data?.VerticalAxes ?? Enumerable.Empty<AxisPreviewInfo>())
            {
                if (!axis.IsEnabled || double.IsNaN(axis.CanvasPosition)) continue;
                double d = Math.Abs(p.X - axis.CanvasPosition);
                if (d < AxisHitThreshold && d < bestDist) { best = axis; bestDist = d; }
            }

            foreach (AxisPreviewInfo axis in _data?.HorizontalAxes ?? Enumerable.Empty<AxisPreviewInfo>())
            {
                if (!axis.IsEnabled || double.IsNaN(axis.CanvasPosition)) continue;
                double d = Math.Abs(p.Y - axis.CanvasPosition);
                if (d < AxisHitThreshold && d < bestDist) { best = axis; bestDist = d; }
            }

            return best;
        }

        // -------------------------------------------------------------------------
        // Geometry helpers
        // -------------------------------------------------------------------------

        private static bool IsPointInPolygon(Point testPoint, IList<Point> polygon)
        {
            bool inside = false;
            int count = polygon?.Count ?? 0;
            if (count < 3) return false;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Point pi = polygon[i];
                Point pj = polygon[j];

                bool intersect =
                    ((pi.Y > testPoint.Y) != (pj.Y > testPoint.Y)) &&
                    (testPoint.X < (pj.X - pi.X) * (testPoint.Y - pi.Y) /
                                   ((pj.Y - pi.Y) + 1e-12) + pi.X);

                if (intersect) inside = !inside;
            }

            return inside;
        }

        private static double CanvasToNormalized(
            AxisOrientation orientation,
            Point p,
            PreviewTransformInfo t)
        {
            if (orientation == AxisOrientation.Vertical)
            {
                double denom = Math.Max(1e-12, t.ModelMaxX - t.ModelMinX);
                double modelX = t.ModelMinX + ((p.X - t.Padding) / t.Scale);
                return Clamp01((modelX - t.ModelMinX) / denom);
            }
            else
            {
                double denom = Math.Max(1e-12, t.ModelMaxY - t.ModelMinY);
                double modelY = t.ModelMinY + (((t.CanvasHeight - t.Padding) - p.Y) / t.Scale);
                return Clamp01((modelY - t.ModelMinY) / denom);
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
