namespace BA.UI.KeyplanGrid
{
    public sealed class KeyplanGridOptions
    {
        public string SourceViewName { get; set; } = string.Empty;
        public string TargetDraftingViewName { get; set; } = string.Empty;
        public string TargetViewTemplateName { get; set; } = string.Empty;
        public string FilledRegionTypeName { get; set; } = string.Empty;

        public bool ClearTargetViewFirst { get; set; } = true;
        public bool CopySourceViewSpecificElements { get; set; } = false;
        public bool DrawGridLines { get; set; } = true;
        public bool CreateFilledRegions { get; set; } = true;
        public bool DrawOutline { get; set; } = true;

        /// <summary>
        /// When true, the exact outer loop creates the main filled region.
        /// Cell-based fills are then treated as secondary/advanced behavior.
        /// </summary>
        public bool UseOutlineAsPrimaryFill { get; set; } = true;

        public KeyplanCellFillMode FillMode { get; set; } = KeyplanCellFillMode.FullCellIfOccupied;

        /// <summary>
        /// Used only when FillMode == FullCellIfOccupied.
        /// Range 0.0 - 1.0.
        /// </summary>
        public double MinimumOccupancyRatio { get; set; } = 0.05;
    }
}