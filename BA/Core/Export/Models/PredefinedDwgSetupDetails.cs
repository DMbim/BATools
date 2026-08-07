using Autodesk.Revit.DB;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Snapshot of every setting a predefined DWG export setup actually
    /// carries, read back from a loaded DWGExportOptions via
    /// GetPredefinedOptions(). Used purely for display, so the UI can
    /// readjust its own controls to show what will really be used while a
    /// predefined setup is active, rather than leaving them showing stale
    /// or misleading values. Never round tripped back into an actual
    /// export, DwgExportService always reloads fresh from Revit at export
    /// time.
    /// </summary>
    public class PredefinedDwgSetupDetails
    {
        public ACADVersion FileVersion { get; set; }
        public ExportUnit TargetUnit { get; set; }
        public bool MergedViews { get; set; }
        public bool SharedCoords { get; set; }
        public bool ExportingAreas { get; set; }
        public bool HideScopeBox { get; set; }
        public bool HideReferencePlane { get; set; }
        public LineScaling LineScaling { get; set; }
        public ExportColorMode Colors { get; set; }
        public PropOverrideMode PropOverrides { get; set; }
    }
}