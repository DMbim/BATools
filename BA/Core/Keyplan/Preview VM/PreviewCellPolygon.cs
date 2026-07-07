using System.Collections.Generic;
using System.Windows;
using Point = System.Windows.Point;

namespace BA.UI.KeyplanGrid
{
    /// <summary>
    /// Represents a single filled polygon in the WPF preview canvas.
    /// </summary>
    public sealed class PreviewCellPolygon
    {
        /// <summary>Stable key matching GeneratedElementRecord.StableKey or cell key.</summary>
        public string CellKey { get; set; } = string.Empty;

        public int XIndex { get; set; }
        public int YIndex { get; set; }

        public bool IsExcluded { get; set; }
        public bool IsSelected { get; set; }

        /// <summary>Canvas-space points for rendering.</summary>
        public List<Point> Points { get; set; } = new List<Point>();

        /// <summary>
        /// Zone pick role assigned during an active KeyplanZoneLabelSession.
        /// None when no session is active or this region is not involved.
        /// </summary>
        public ZonePickRole ZonePickRole { get; set; } = ZonePickRole.None;

        /// <summary>
        /// Zone label assigned after a committed session, e.g. "1", "A".
        /// Empty string when no label has been assigned.
        /// </summary>
        public string ZoneLabel { get; set; } = string.Empty;
    }
}
