using Autodesk.Revit.DB;

namespace BA.UI.KeyplanGrid
{
    /// <summary>
    /// Represents a single level entry in the level picker dialog,
    /// annotated with whether a usable KP_GrossArea(KeyPlan) source exists.
    /// </summary>
    public sealed class KeyplanLevelOption
    {
        public Level Level { get; set; }
        public string LevelName { get; set; } = string.Empty;
        public double Elevation { get; set; }

        /// <summary>
        /// True if a KP_GrossArea(KeyPlan) area plan view exists for this level
        /// AND it has a resolvable outer boundary loop.
        /// </summary>
        public bool IsReady { get; set; }

        /// <summary>
        /// True if no KP_GrossArea(KeyPlan) view exists for this level yet,
        /// so the "Create Area Plan View" button should be shown.
        /// </summary>
        public bool CanCreateView { get; set; }

        /// <summary>
        /// The resolved area plan view for this level, if available.
        /// Null if no matching view exists.
        /// </summary>
        public ViewPlan SourceView { get; set; }

        /// <summary>
        /// The resolved outer boundary loop, if IsReady is true.
        /// Null otherwise.
        /// </summary>
        public CurveLoop OuterLoop { get; set; }

        /// <summary>
        /// Human-readable reason this level is not ready.
        /// Empty when IsReady is true.
        /// </summary>
        public string NotReadyReason { get; set; } = string.Empty;

        public string DisplayName => IsReady
            ? $"{LevelName}  —  ready"
            : $"{LevelName}  —  not set up";
    }
}