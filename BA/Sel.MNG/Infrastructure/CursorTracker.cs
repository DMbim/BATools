using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace BATools.SelectionManager.Infrastructure
{
    /// <summary>
    /// Polls cursor position at ~60fps and fires PositionChanged.
    /// Tracks continuously until stopped or paused.
    /// No directional release logic — caller controls all state transitions.
    /// </summary>
    public class CursorTracker
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private readonly DispatcherTimer _timer;
        private bool _paused;

        public bool IsTracking => _timer.IsEnabled;
        public bool IsPaused => _paused;

        /// <summary>Fires on every tick while not paused. Point is in screen pixels.</summary>
        public event Action<Point>? PositionChanged;

        public CursorTracker()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += OnTick;
        }

        /// <summary>Start polling. Clears paused state.</summary>
        public void Start()
        {
            _paused = false;
            _timer.Start();
        }

        /// <summary>Stop polling and clear paused state. Call when toolbar hides.</summary>
        public void Stop()
        {
            _timer.Stop();
            _paused = false;
        }

        /// <summary>
        /// Pause reporting without stopping the timer.
        /// The toolbar stays at its current screen position; cursor can interact with buttons.
        /// </summary>
        public void Pause()
        {
            _paused = true;
        }

        /// <summary>Resume reporting from current cursor position.</summary>
        public void Resume()
        {
            _paused = false;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_paused) return;
            if (!GetCursorPos(out POINT pt)) return;
            PositionChanged?.Invoke(new Point(pt.X, pt.Y));
        }
    }
}