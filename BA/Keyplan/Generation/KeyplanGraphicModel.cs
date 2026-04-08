using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace BA.UI.KeyplanGrid
{
    public sealed class KeyplanGraphicModel
    {
        public List<KeyplanPolygonGraphicItem> FilledRegions { get; } = new List<KeyplanPolygonGraphicItem>();
        public List<KeyplanLineGraphicItem> GridLines { get; } = new List<KeyplanLineGraphicItem>();
        public List<KeyplanLineGraphicItem> OutlineLines { get; } = new List<KeyplanLineGraphicItem>();
    }

    public sealed class KeyplanPolygonGraphicItem
    {
        public string StableKey { get; set; } = string.Empty;
        public List<XYZ> Polygon { get; set; } = new List<XYZ>();
    }

    public sealed class KeyplanLineGraphicItem
    {
        public string StableKey { get; set; } = string.Empty;
        public XYZ A { get; set; } = XYZ.Zero;
        public XYZ B { get; set; } = XYZ.Zero;
    }

    public sealed class GeneratedElementRecord
    {
        /// <summary>
        /// Geometry- or topology-based key assigned at build time.
        /// Prefixed with role: "fill:", "grid:", "outline:".
        /// </summary>
        public string StableKey { get; set; } = string.Empty;

        /// <summary>
        /// "FilledRegion", "GridLine", or "OutlineLine".
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// Revit ElementId at creation time.  Not stable across sessions — use UniqueId for persistence.
        /// </summary>
        public ElementId ElementId { get; set; } = ElementId.InvalidElementId;

        /// <summary>
        /// Revit element UniqueId.  Stable across save/reopen.  Populated immediately after element creation.
        /// </summary>
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>
        /// 2D centroid of the source polygon in pre-scale model space.
        /// Used by KeyplanZoneLabelService for direction-vector projection.
        /// </summary>
        public XYZ Centroid { get; set; }

        /// <summary>
        /// Label assigned by KeyplanZoneLabelService, e.g. "1", "A".
        /// Empty until a zone label session is committed.
        /// </summary>
        public string ZoneLabel { get; set; } = string.Empty;

        public string GraphicRole { get; set; }

        /// <summary>
        /// Parameter name assigned by KeyplanZoneLabelService, e.g. "BA.Tls_Zn_3".
        /// Empty until a zone label session is committed.
        /// </summary>
        public string ZoneParameterName { get; set; } = string.Empty;
    }
}