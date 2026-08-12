using Autodesk.Revit.DB;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Custom image export settings, shared by both JPEG and PNG since
    /// ImageExportOptions only differs between them by file type
    /// (HLRandWFViewsFileType / ShadowViewsFileType). Every field here
    /// maps to a real, currently valid ImageExportOptions property,
    /// confirmed against the live Revit API documentation before this was
    /// written.
    /// </summary>
    public class ImageSettings
    {
        public ImageResolution Resolution { get; set; } = ImageResolution.DPI_300;
        public ZoomFitType ZoomType { get; set; } = ZoomFitType.FitToPage;

        /// <summary>
        /// Only meaningful when ZoomType is FitToPage, ImageExportOptions
        /// ignores this otherwise, confirmed from the API docs.
        /// </summary>
        public int PixelSize { get; set; } = 2000;

        /// <summary>
        /// Only meaningful when ZoomType is FitToPage, same as PixelSize.
        /// </summary>
        public FitDirectionType FitDirection { get; set; } = FitDirectionType.Horizontal;

        /// <summary>
        /// Only meaningful when ZoomType is Zoom, ignored when FitToPage.
        /// </summary>
        public int ZoomPercentage { get; set; } = 100;
    }
}
