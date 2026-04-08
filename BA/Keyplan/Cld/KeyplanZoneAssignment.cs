using Autodesk.Revit.DB;

namespace BA.UI.KeyplanGrid
{
    /// <summary>
    /// Represents the resolved label assignment for a single generated filled region.
    /// </summary>
    public sealed class KeyplanZoneAssignment
    {
        /// <summary>UniqueId of the Revit FilledRegion element.</summary>
        public string RegionUniqueId { get; set; } = string.Empty;

        /// <summary>StableKey from GeneratedElementRecord — used to correlate with preview.</summary>
        public string StableKey { get; set; } = string.Empty;

        /// <summary>Parameter name to write to, e.g. "BA.Tls_Zn_1".</summary>
        public string ParameterName { get; set; } = string.Empty;

        /// <summary>The label string to write, e.g. "1", "A", "a".</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>2D centroid of the source polygon in model space.</summary>
        public XYZ Centroid { get; set; }

        /// <summary>Scalar projection of the centroid onto the direction vector.</summary>
        public double ProjectionValue { get; set; }

        /// <summary>0-based index in sorted traversal order.</summary>
        public int SequenceIndex { get; set; }
    }
}
