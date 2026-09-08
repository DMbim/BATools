using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.BIM.Core.Dimensioning.Models
{
    public enum BA_WallConstructionType
    {
        Undetermined,
        Concrete,
        Plasterboard
    }

    public enum BA_DimensionStyle
    {
        Axis,
        Edges
    }

    public sealed class BA_DimensionOpeningRef
    {
        public ElementId OpeningId { get; set; }
        public ElementId HostWallId { get; set; }
        public BA_WallConstructionType WallConstructionType { get; set; } = BA_WallConstructionType.Undetermined;
        public BA_DimensionStyle DimensionStyle { get; set; } = BA_DimensionStyle.Axis;
    }

    public sealed class BA_DimensionCandidate
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public List<ElementId> WallIds { get; set; } = new List<ElementId>();
        public ElementId WallId => WallIds != null && WallIds.Count > 0 ? WallIds[0] : ElementId.InvalidElementId;
        public string WallName { get; set; }
        public List<BA_DimensionOpeningRef> OrderedOpenings { get; set; } = new List<BA_DimensionOpeningRef>();
        public bool IsSelected { get; set; } = true;
        public bool IsFlipped { get; set; }
        public double OffsetFeet { get; set; }

        public string DisplayLabel
        {
            get
            {
                int edges = OrderedOpenings.Count(o => o.DimensionStyle == BA_DimensionStyle.Edges);
                int axis = OrderedOpenings.Count - edges;
                return $"{WallName} ({OrderedOpenings.Count} openings: {edges} Edges, {axis} Axis)";
            }
        }
    }

    public enum BA_DimensionSkipReason
    {
        WallIsCurved,
        NonBasicWallType,
        FewerThanTwoOpenings,
        NoValidOpeningReference,
        ViewIsNotPlan,
        Unknown
    }

    public sealed class BA_DimensionSkip
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public ElementId WallId { get; set; }
        public string WallName { get; set; }
        public BA_DimensionSkipReason Reason { get; set; }
        public string Detail { get; set; }
    }

    public sealed class BA_DimensionPlacementOutcome
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public ElementId WallId { get; set; }
        public bool Success { get; set; }
        public ElementId CreatedDimensionId { get; set; }
        public string FailureMessage { get; set; }
        public BA_DimensionCandidate SourceCandidate { get; set; }
    }
}