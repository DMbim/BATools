using System;

namespace BATools.SelectionManager.Infrastructure
{
    /// <summary>
    /// Detects two rapid presses of a configurable key within a time window.
    /// Non-target keys are ignored entirely — only time-based expiry resets
    /// the sequence. This is required because host applications (Revit) generate
    /// synthetic keyboard events between user keypresses that would otherwise
    /// cancel the detection sequence.
    /// </summary>
    public class DoubleKeyDetector
    {
        private readonly Func<uint, bool> _isTargetKey;
        private readonly int _windowMs;

        private bool _physicallyDown;
        private bool _waitingForSecond;
        private DateTime _firstPressTime;

        public event Action? DoubleKeyDetected;

        public DoubleKeyDetector(Func<uint, bool> isTargetKey, int windowMs = 350)
        {
            _isTargetKey = isTargetKey;
            _windowMs = windowMs;
        }

        public void OnKeyDown(uint vkCode)
        {
            if (!_isTargetKey(vkCode))
            {
                // Non-target keys are IGNORED — not reset.
                // Revit generates synthetic key events between keypresses.
                // Time-based expiry handles stale sequences correctly.
                return;
            }

            // Reject OS auto-repeat — key is still physically held
            if (_physicallyDown) return;
            _physicallyDown = true;

            // Check whether a pending first-press has expired
            if (_waitingForSecond &&
                (DateTime.UtcNow - _firstPressTime).TotalMilliseconds > _windowMs)
            {
                // Too much time passed since first press — start fresh
                _waitingForSecond = false;
            }

            if (!_waitingForSecond)
            {
                // Record first press
                _firstPressTime = DateTime.UtcNow;
                _waitingForSecond = true;
                return;
            }

            // Second press — check elapsed time
            double elapsed = (DateTime.UtcNow - _firstPressTime).TotalMilliseconds;
            if (elapsed <= _windowMs)
            {
                Reset();
                DoubleKeyDetected?.Invoke();
            }
            else
            {
                // Too slow — this press becomes the new first press
                _firstPressTime = DateTime.UtcNow;
                _waitingForSecond = true;
            }
        }

        public void OnKeyUp(uint vkCode)
        {
            if (_isTargetKey(vkCode))
                _physicallyDown = false;
        }

        public void Reset()
        {
            _physicallyDown = false;
            _waitingForSecond = false;
            _firstPressTime = DateTime.MinValue;
        }
    }
}