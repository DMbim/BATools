namespace BA.Core.Export.Models
{
    /// <summary>
    /// One entry in the paper size matching table. Not hard coded
    /// exclusively around ISO sizes at the architecture level, the table is
    /// a plain list PaperSizeDetectionService matches against, a custom
    /// list can be supplied in place of the built in ISO defaults.
    /// </summary>
    public class PaperSizeDefinition
    {
        public string Name { get; set; } = string.Empty;
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        /// <summary>
        /// Zero or unset means the caller's default tolerance applies.
        /// </summary>
        public double MatchingToleranceMm { get; set; }
    }
}
