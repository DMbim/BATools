namespace BA.UI.KeyplanGrid
{
    public sealed class AxisPreviewInfo
    {
        public string SplitId { get; set; }
        public AxisOrientation Orientation { get; set; }
        public int InteriorIndex { get; set; }
        public double Normalized { get; set; }
        public double CanvasPosition { get; set; }
        public bool IsSelected { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string DisplayName { get; set; }
    }
}