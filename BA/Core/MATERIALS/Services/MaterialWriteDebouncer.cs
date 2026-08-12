// Path: BA\Materials\MaterialWriteDebouncer.cs
using System;
using System.Windows.Threading;
using BA.Materials.Models;

namespace BA.Materials
{
    /// <summary>
    /// Coalesces rapid MaterialChannelSet changes (slider drags) into a single write,
    /// firing OnFlush roughly Delay after the last Update call. No Revit API dependency,
    /// this class only knows about MaterialChannelSet and a DispatcherTimer, the actual
    /// Revit write happens in whatever callback is passed to the constructor, which the
    /// caller is responsible for routing through RevitExternalInvoker.Run.
    ///
    /// Must be constructed on the WPF UI thread (DispatcherTimer requirement). Call
    /// FlushImmediately on window close/Apply so a change mid-drag is never lost.
    /// </summary>
    public sealed class MaterialWriteDebouncer : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Action<MaterialChannelSet> _onFlush;
        private MaterialChannelSet _pending;
        private MaterialChannelSet _lastFlushed;
        private bool _disposed;

        public MaterialWriteDebouncer(Action<MaterialChannelSet> onFlush, TimeSpan? delay = null)
        {
            _onFlush = onFlush ?? throw new ArgumentNullException(nameof(onFlush));

            _timer = new DispatcherTimer
            {
                Interval = delay ?? TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += OnTimerTick;
        }

        /// <summary>
        /// Registers a new pending change and resets the debounce window. Clones the
        /// passed-in channel set so later in-place mutation by the caller cannot alter
        /// what was queued.
        /// </summary>
        public void Update(MaterialChannelSet channels)
        {
            if (_disposed) return;
            if (channels == null) throw new ArgumentNullException(nameof(channels));

            _pending = channels.Clone();

            _timer.Stop();
            _timer.Start();
        }

        /// <summary>
        /// Forces an immediate flush of any pending change, bypassing the debounce
        /// window. Call this on window close and on explicit Apply/Save.
        /// </summary>
        public void FlushImmediately()
        {
            if (_disposed) return;

            _timer.Stop();
            FlushInternal();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            FlushInternal();
        }

        private void FlushInternal()
        {
            if (_pending == null)
                return;

            if (_lastFlushed != null && _pending.ChannelsEqual(_lastFlushed))
            {
                // Nothing actually changed since the last write, e.g. a slider fired an
                // input event without a real value change. Skip the Revit round trip.
                _pending = null;
                return;
            }

            MaterialChannelSet toWrite = _pending;
            _pending = null;
            _lastFlushed = toWrite.Clone();

            _onFlush(toWrite);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }
    }
}