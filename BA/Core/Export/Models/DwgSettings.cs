using Autodesk.Revit.DB;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// LayerMapping on DWGExportOptions is just a string selecting one of a
    /// small set of known industry standards, not a project-specific saved
    /// table, confirmed against the live API. RevitDefault maps to an
    /// empty string, meaning Revit picks its own localized default.
    /// </summary>
    public enum DwgLayerMappingStandard
    {
        RevitDefault,
        AIA,
        CP83,
        BS1192,
        ISO13567
    }

    /// <summary>
    /// Custom DWG export settings, applied directly to a fresh
    /// DWGExportOptions instance built with its plain constructor, no
    /// dependency on a predefined setup existing in the project. Every
    /// field here maps to a real, currently valid DWGExportOptions or
    /// ACADExportOptions/BaseExportOptions property, confirmed against the
    /// live Revit API documentation before this was written.
    /// </summary>
    public class DwgSettings
    {
        public ACADVersion FileVersion { get; set; } = ACADVersion.R2018;
        public DwgLayerMappingStandard LayerMapping { get; set; } = DwgLayerMappingStandard.RevitDefault;
        public ExportUnit TargetUnit { get; set; } = ExportUnit.Default;

        /// <summary>
        /// Merging into one file (true) versus separate, externally
        /// referenced files (false), the exact setting behind Revit's
        /// native "Export views on sheets and links as external
        /// references" checkbox, confirmed directly from Autodesk's own
        /// AEC DevBlog. The native checkbox checked corresponds to
        /// MergedViews = false, not true, an earlier version of this
        /// class had that backwards in its tooltip.
        /// </summary>
        public bool MergedViews { get; set; }
        public bool SharedCoords { get; set; }
        public bool ExportingAreas { get; set; }
        public bool HideScopeBox { get; set; }
        public bool HideReferencePlane { get; set; }

        /// <summary>
        /// IndexColors (Revit's own default) snaps every color to the
        /// nearest of 255 fixed AutoCAD palette colors, "may not provide
        /// an exact match for RGB and Pantone colors" per Autodesk's own
        /// documentation. TrueColorRGB preserves exact RGB instead.
        /// Confirmed cause of exported colors looking visibly different
        /// from the Revit view when overrides use non-palette colors.
        /// </summary>
        public ExportColorMode Colors { get; set; } = ExportColorMode.IndexColors;

        /// <summary>
        /// "Set linetype scale" in Revit's native DWG export UI. Default
        /// matches BaseExportOptions' own documented default (ViewScale).
        /// Coordinate base is deliberately not a separate setting here,
        /// the API only exposes it as the SharedCoords bool above, not a
        /// richer multi-option choice, confirmed against the full
        /// DWGExportOptions/ACADExportOptions/BaseExportOptions property
        /// list.
        /// </summary>
        public LineScaling LineScaling { get; set; } = LineScaling.ViewScale;

        /// <summary>
        /// "How to export overridden object styles" in Revit's terms.
        /// Confirmed default on DWGExportOptions itself is
        /// PropOverrideMode.ByEntity, explicit per-object color and
        /// lineweight overrides rather than inheriting from the layer.
        /// Defaulted to ByLayer here instead, deliberately deviating from
        /// Revit's own raw default, since ByLayer is the standard,
        /// expected convention for a clean CAD deliverable and matches
        /// what most consultants actually want. Confirmed cause of
        /// exported hatches and other objects showing an explicit RGB
        /// color and lineweight instead of ByLayer in AutoCAD.
        /// </summary>
        public PropOverrideMode PropOverrides { get; set; } = PropOverrideMode.ByLayer;

        /// <summary>
        /// Name of a DWG export setup already built and saved in Revit's
        /// own "Modify DWG/DXF Export Setup" dialog (Export Setups DWG/DXF
        /// > Layers tab). When set, this is loaded via
        /// DWGExportOptions.GetPredefinedOptions() as the base options
        /// object, and only that call carries the actual custom category
        /// to layer name mapping (color, layer name, and so on per
        /// category). Confirmed from a real Autodesk forum thread:
        /// building that mapping programmatically via the
        /// ExportLayerTable/SetExportLayerTable API is documented as not
        /// working reliably (categories inherited the wrong layer names),
        /// while loading a UI-built predefined setup by name is the
        /// confirmed working path. LayerMapping below is deliberately
        /// skipped whenever this is set, so it can't clobber the custom
        /// table loaded from the named setup.
        /// </summary>
        public string PredefinedSetupName { get; set; } = string.Empty;

        /// <summary>
        /// Converts LayerMapping to the string DWGExportOptions.LayerMapping
        /// actually expects. Empty string means Revit's own localized
        /// default, not "no mapping at all".
        /// </summary>
        public string ResolveLayerMappingString()
        {
            switch (LayerMapping)
            {
                case DwgLayerMappingStandard.AIA:
                    return "AIA";
                case DwgLayerMappingStandard.CP83:
                    return "CP83";
                case DwgLayerMappingStandard.BS1192:
                    return "BS1192";
                case DwgLayerMappingStandard.ISO13567:
                    return "ISO13567";
                default:
                    return string.Empty;
            }
        }
    }
}