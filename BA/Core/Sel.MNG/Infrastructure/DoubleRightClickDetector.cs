using System;
using System.Windows;
using Point = System.Windows.Point;

namespace BATools.SelectionManager.Infrastructure
{
    /// <summary>
    /// Detects two right-clicks within a time window and spatial proximity.
    /// Non-right-button events are ignored — they do not reset the sequence.
    /// </summary>
    public class DoubleRightClickDetector
    {
        private readonly int _windowMs;
        private readonly double _maxDistancePx;

        private DateTime _firstClickTime = DateTime.MinValue;
        private Point _firstClickPos;
        private bool _waitingForSecond;

        /// <summary>Fires with the screen position of the second click.</summary>
        public event Action<Point>? DoubleRightClickDetected;

        public DoubleRightClickDetector(int windowMs = 400, double maxDistancePx = 12.0)
        {
            _windowMs = windowMs;
            _maxDistancePx = maxDistancePx;
        }

        /// <summary>Must be called on the WPF dispatcher thread.</summary>
        public void OnRightButtonDown(Point screenPos)
        {
            var now = DateTime.UtcNow;

            if (!_waitingForSecond)
            {
                _firstClickTime = now;
                _firstClickPos = screenPos;
                _waitingForSecond = true;
                return;
            }

            double elapsed = (now - _firstClickTime).TotalMilliseconds;
            double distance = Distance(screenPos, _firstClickPos);

            // Expired — treat as new first click
            if (elapsed > _windowMs)
            {
                _firstClickTime = now;
                _firstClickPos = screenPos;
                return;
            }

            // Too far away — treat as new first click
            if (distance > _maxDistancePx)
            {
                _firstClickTime = now;
                _firstClickPos = screenPos;
                return;
            }

            // Valid double right-click
            Reset();
            DoubleRightClickDetected?.Invoke(screenPos);
        }

        public void Reset()
        {
            _waitingForSecond = false;
            _firstClickTime = DateTime.MinValue;
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}