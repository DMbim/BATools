using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// How to group families/types into booklets: a real Revit category,
    /// or an arbitrary parameter value (e.g. a shared parameter used as a
    /// classification, not necessarily "Category" at all).
    /// </summary>
    /// <summary>
    /// Which mechanism generates each sheet's graphics. RealViews uses
    /// genuine ViewSection/ViewPlan elements (BookletViewGenerationService).
    /// LegendComponents duplicates an existing, manually created seed
    /// Legend view per type and retargets its components, since the API
    /// cannot create a new Legend view or Legend Component from nothing,
    /// confirmed directly by Autodesk (ticket CF-759, still unimplemented).
    /// </summary>
    public enum BookletGenerationMode
    {
        RealViews,
        LegendComponents
    }

    public enum BookletGroupingMode
    {
        RevitCategory,
        ParameterValue
    }

    /// <summary>
    /// Configuration for one type booklet generation run. Not a scheduled
    /// job, same reasoning as family export, this is a one-shot,
    /// user-initiated batch tool, not something issued on a recurring
    /// schedule.
    /// </summary>
    public class BookletSettings
    {
        public BookletGenerationMode Mode { get; set; } = BookletGenerationMode.RealViews;

        /// <summary>
        /// Only relevant when Mode is LegendComponents. UniqueId of the
        /// existing, manually created Legend view containing at least one
        /// placed Legend Component, this gets duplicated once per selected
        /// type, its components retargeted to that type. Must already
        /// exist, there is no API path to create a new one.
        /// </summary>
        public string SeedLegendViewUniqueId { get; set; } = string.Empty;

        public BookletGroupingMode GroupingMode { get; set; } = BookletGroupingMode.RevitCategory;

        /// <summary>
        /// Used when GroupingMode is RevitCategory.
        /// </summary>
        public BuiltInCategory Category { get; set; } = BuiltInCategory.OST_Windows;

        /// <summary>
        /// Used when GroupingMode is ParameterValue, the parameter whose
        /// value distinguishes which types get included, not necessarily
        /// which category they belong to.
        /// </summary>
        public string GroupingParameterName { get; set; } = string.Empty;

        /// <summary>
        /// Type UniqueIds selected for booklet generation, resolved
        /// against the live document at run time, same stability rule
        /// used everywhere else in this module.
        /// </summary>
        public List<string> SelectedTypeUniqueIds { get; set; } = new List<string>();

        /// <summary>
        /// Maps source parameters (picked from the selected types) to
        /// literal instance parameter names on the user's title block
        /// family. Replaces the earlier TextNote based info block, real
        /// title block fields instead of a drawn table.
        /// </summary>
        public List<BookletTitleBlockFieldMapping> TitleBlockFieldMappings { get; set; } = new List<BookletTitleBlockFieldMapping>();

        /// <summary>
        /// Title block parameter name to receive an auto numbered item
        /// mark (e.g. "OZN"), separate from the field mappings above since
        /// a running sheet sequence number isn't a parameter pulled from
        /// the type itself. Empty means don't set one.
        /// </summary>
        public string ItemMarkTitleBlockParameterName { get; set; } = string.Empty;

        public string ItemMarkPrefix { get; set; } = "Z ";

        /// <summary>
        /// Extra distance added around the instance's own bounding box
        /// when cropping the floor plan, section, and isometric views, in
        /// millimeters, so the graphic isn't cropped flush against the
        /// frame edge.
        /// </summary>
        public double CropMarginMm { get; set; } = 150;

        /// <summary>
        /// View scale applied to the generated floor plan, section, and
        /// isometric views, expressed the same way Revit does, as the
        /// denominator of a 1:N ratio (e.g. 20 means 1:20).
        /// </summary>
        public int ViewScale { get; set; } = 20;

        public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Fine;

        /// <summary>
        /// UniqueId of the title block type to use for generated sheets.
        /// Empty means use whatever the document's default title block is.
        /// </summary>
        public string TitleBlockUniqueId { get; set; } = string.Empty;

        public string OutputSheetNumberPrefix { get; set; } = "TB-";
    }
}
