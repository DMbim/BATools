using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.BIM.Core.Dimensioning.Models
{
    public sealed class BA_DimensionCandidate
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public ElementId WallId { get; set; }
        public string WallName { get; set; }
        public List<ElementId> OrderedOpeningIds { get; set; } = new List<ElementId>();
        public XYZ WallStart { get; set; }
        public XYZ WallEnd { get; set; }
        public XYZ WallOrientation { get; set; }
        public bool IsSelected { get; set; } = true;
        public string DisplayLabel => $"{WallName} ({OrderedOpeningIds.Count} openings)";
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
    }
}