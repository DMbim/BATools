using System;

namespace BA.UI.KeyplanGrid
{
    public sealed class KeyplanSourceReference
    {
        public long SourceViewId { get; set; }
        public string SourceViewUniqueId { get; set; } = string.Empty;
        public string SourceViewName { get; set; } = string.Empty;

        public long SourceAreaId { get; set; }
        public string SourceAreaUniqueId { get; set; } = string.Empty;

        public string SourceAreaName { get; set; } = string.Empty;
        public string SourceAreaNumber { get; set; } = string.Empty;

        public string BoundarySignature { get; set; } = string.Empty;

        public void Normalize()
        {
            SourceViewUniqueId ??= string.Empty;
            SourceViewName ??= string.Empty;
            SourceAreaUniqueId ??= string.Empty;
            SourceAreaName ??= string.Empty;
            SourceAreaNumber ??= string.Empty;
            BoundarySignature ??= string.Empty;
        }
    }

    public sealed class KeyplanSourceResolutionResult
    {
        public Autodesk.Revit.DB.CurveLoop OuterLoop { get; set; }
        public Autodesk.Revit.DB.ElementId SourceAreaId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId;
        public string SourceAreaUniqueId { get; set; } = string.Empty;
        public string SourceAreaName { get; set; } = string.Empty;
        public string SourceAreaNumber { get; set; } = string.Empty;
        public string BoundarySignature { get; set; } = string.Empty;
        public string ResolutionMode { get; set; } = string.Empty;
    }
}