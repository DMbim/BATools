using System;
using System.Windows.Threading;

namespace BATools.SelectionManager.Infrastructure
{
    /// <summary>
    /// Detects a configurable key held continuously for a set duration.
    /// Cancelled if key is released early or any other key is pressed.
    /// Ignores OS auto-repeat — only fires once per genuine key-down gesture.
    /// Must be fed OnKeyDown and OnKeyUp on the WPF dispatcher thread.
    /// </summary>
    public class KeyHoldDetector
    {
        private readonly Func<uint, bool> _isTargetKey;
        private readonly int _holdMs;

        private DispatcherTimer? _timer;
        private bool _physicallyDown;

        public event Action? KeyHeld;

        public KeyHoldDetector(Func<uint, bool> isTargetKey, int holdMs = 500)
        {
            _isTargetKey = isTargetKey;
            _holdMs = holdMs;
        }

        public void OnKeyDown(uint vkCode)
        {
            if (_isTargetKey(vkCode))
            {
                if (!_physicallyDown)
                {
                    _physicallyDown = true;
                    StartTimer();
                }
                // Auto-repeat: already counting, don't restart
            }
            else
            {
                // Any other key cancels
                Cancel();
            }
        }

        public void OnKeyUp(uint vkCode)
        {
            if (_isTargetKey(vkCode))
            {
                _physicallyDown = false;
                Cancel();
            }
        }

        public void Cancel()
        {
            _timer?.Stop();
            _timer = null;
        }

        private void StartTimer()
        {
            Cancel();
            _timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(_holdMs)
            };
            _timer.Tick += (_, _) =>
            {
                Cancel();
                KeyHeld?.Invoke();
            };
            _timer.Start();
        }
    }
}