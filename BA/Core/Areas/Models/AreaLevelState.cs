using Autodesk.Revit.DB;

namespace BA.Core.AreaSchemes.Models
{
    public enum ViewStatus
    {
        Missing,
        ExistsEmpty,
        ExistsWithAreas
    }

    public sealed class AreaLevelState
    {
        public AreaSchemeDefinition Definition { get; init; } = null!;
        public Level Level { get; init; } = null!;
        public ViewStatus ViewStatus { get; set; }
        public ElementId? ViewId { get; set; }
        public double AreaM2 { get; set; }
        public int AreaCount { get; set; }
        public bool IsComplete => ViewStatus == ViewStatus.ExistsWithAreas && AreaM2 > 0;
    }
}