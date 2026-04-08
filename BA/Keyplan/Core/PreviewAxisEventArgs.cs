using System;

namespace BA.UI.KeyplanGrid
{
    public sealed class PreviewAxisEventArgs : EventArgs
    {
        public string SplitId { get; }
        public AxisOrientation Orientation { get; }
        public double Normalized { get; }

        public PreviewAxisEventArgs(string splitId, AxisOrientation orientation, double normalized)
        {
            SplitId = splitId ?? string.Empty;
            Orientation = orientation;
            Normalized = normalized;
        }
    }

    public sealed class PreviewAxisClickEventArgs : EventArgs
    {
        public string SplitId { get; }
        public AxisOrientation Orientation { get; }

        public PreviewAxisClickEventArgs(string splitId, AxisOrientation orientation)
        {
            SplitId = splitId ?? string.Empty;
            Orientation = orientation;
        }
    }
}