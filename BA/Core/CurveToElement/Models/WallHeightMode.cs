// File: BA/Core/CurveToElement/Models/WallGroupSettings.cs
// Action: CREATE NEW

using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Models
{
    public enum WallHeightMode
    {
        Unconnected,
        UpToLevel
    }

    /// <summary>
    /// Per-group wall generation settings, configured by the user in the settings panel.
    /// Pure data only - the ViewModel layer wraps this for WPF binding (next step).
    /// </summary>
    public class WallGroupSettings
    {
        public ElementId WallTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId BaseLevelId { get; set; } = ElementId.InvalidElementId;
        public double BaseOffset { get; set; } = 0.0;

        public WallHeightMode HeightMode { get; set; } = WallHeightMode.Unconnected;
        public double UnconnectedHeight { get; set; } = 9.8425; // ~3.0 m in feet, user-editable in UI
        public ElementId TopLevelId { get; set; } = ElementId.InvalidElementId;
        public double TopOffset { get; set; } = 0.0;

        public WallLocationLine LocationLine { get; set; } = WallLocationLine.WallCenterline;

        /// <summary>
        /// Only meaningful for open (non-closed) chains, where the offset side cannot be
        /// inferred from loop winding. Ignored for closed loops.
        /// </summary>
        public bool FlipSide { get; set; } = false;

        public bool AllowEndJoins { get; set; } = true;
        public bool StructuralUsage { get; set; } = false;
    }
}