namespace BA.Core.Export.Models
{
    public enum PaperOrientation
    {
        Landscape,
        Portrait,
        Unspecified
    }

    /// <summary>
    /// Result of detecting a sheet's paper size from its placed title
    /// block. Display only in this pass, does not drive export behavior or
    /// grouping, per confirmed scope. HasNoTitleBlock and IsAmbiguous are
    /// mutually exclusive with a resolved size, exactly one of the three
    /// states applies to a given sheet.
    /// </summary>
    public class PaperSizeInfo
    {
        /// <summary>
        /// Null or empty when the detected dimensions did not match any
        /// entry in the size table within tolerance, WidthMm/HeightMm are
        /// still populated in that case so the raw measurement is visible.
        /// </summary>
        public string ResolvedSizeName { get; set; }

        public PaperOrientation Orientation { get; set; } = PaperOrientation.Unspecified;

        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        /// <summary>
        /// True when the sheet has more than one title block instance and
        /// they report different dimensions. A value is deliberately not
        /// silently picked in this case, WidthMm/HeightMm carry the first
        /// candidate found only for reference.
        /// </summary>
        public bool IsAmbiguous { get; set; }

        /// <summary>
        /// True when no title block instance on the sheet has usable
        /// Sheet Width/Sheet Height values, either no title block is
        /// placed or the placed family does not populate those parameters.
        /// </summary>
        public bool HasNoTitleBlock { get; set; }

        public string DisplayText
        {
            get
            {
                if (HasNoTitleBlock)
                {
                    return "No title block";
                }

                if (IsAmbiguous)
                {
                    return "Ambiguous, multiple title blocks disagree";
                }

                if (string.IsNullOrEmpty(ResolvedSizeName))
                {
                    return $"Unresolved ({WidthMm:0} x {HeightMm:0} mm)";
                }

                var orientationText = Orientation == PaperOrientation.Unspecified
                    ? string.Empty
                    : $" {Orientation}";

                return $"{ResolvedSizeName}{orientationText}";
            }
        }
    }
}
