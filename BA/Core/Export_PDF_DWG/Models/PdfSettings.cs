using Autodesk.Revit.DB;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Custom PDF export settings, applied directly to a fresh
    /// PDFExportOptions instance. Every field here maps to a real,
    /// currently valid PDFExportOptions property, confirmed against the
    /// live Revit API documentation before this was written.
    ///
    /// Paper size, orientation, and placement are deliberately left out
    /// here, that overlaps with the paper size detection already built
    /// into the sheet picker and deserves its own pass to wire the two
    /// together properly rather than a second, disconnected paper size
    /// concept living in this settings model.
    /// </summary>
    public class PdfSettings
    {
        public ColorDepthType ColorDepth { get; set; } = ColorDepthType.Color;
        public PDFExportQualityType ExportQuality { get; set; } = PDFExportQualityType.DPI600;
        public ZoomType ZoomType { get; set; } = ZoomType.FitToPage;

        /// <summary>
        /// Only meaningful when ZoomType is Zoom, PDFExportOptions ignores
        /// this when ZoomType is FitToPage, confirmed from the API docs.
        /// </summary>
        public int ZoomPercentage { get; set; } = 100;

        public bool AlwaysUseRaster { get; set; }
        public bool HideCropBoundaries { get; set; } = true;
        public bool HideScopeBoxes { get; set; } = true;
        public bool HideReferencePlane { get; set; }
        public bool ViewLinksInBlue { get; set; }
    }
}
