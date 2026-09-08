// File: BA_Tools/VisibilityDiagnostics/Services/Checks/ViewRangeVisibilityCheck.cs
using System;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Determines whether the element's model-space geometry falls entirely
    /// outside the view's View Range (Top / Cut / Bottom / View Depth), for plan
    /// view types only.
    ///
    /// There is no direct Revit API call for "is this element clipped by View
    /// Range" - this is reconstructed manually from PlanViewRange plus the
    /// element's own bounding box. Two deliberate implementation choices:
    ///
    /// 1. Uses Element.get_BoundingBox(null) (model space), NOT
    ///    get_BoundingBox(view). The view-specific overload returns null for an
    ///    element that is not currently visible in that view - which is exactly
    ///    the case this tool exists to diagnose - so it cannot be used here.
    ///
    /// 2. PlanViewRange.GetLevelId(PlanViewPlane.ViewDepthPlane) returning
    ///    ElementId.InvalidElementId is treated as "View Depth level unset /
    ///    Unlimited" -> no lower bound. This matches the PlanViewRange API's own
    ///    documented remark that InvalidElementId represents an unset plane, but
    ///    was not independently confirmed against a live "Unlimited" View Depth
    ///    view in this session. Verify against a real test view before relying on
    ///    this for a genuinely unlimited-depth view. // <- ASSUMPTION, VERIFY
    /// </summary>
    public sealed class ViewRangeVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "View Range";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            if (view is not ViewPlan viewPlan || !IsPlanTypeWithViewRange(viewPlan.ViewType))
            {
                return VisibilityCheckItem.NotApplicable(CheckName,
                    "View Range only applies to Floor Plan, Ceiling Plan, Structural/Engineering Plan, and Area Plan views. " +
                    $"The active view '{view.Name}' is a {view.ViewType}, so View Range cannot be hiding this element.");
            }

            PlanViewRange range;
            try
            {
                range = viewPlan.GetViewRange();
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not read the View Range for '{view.Name}': {ex.Message}");
            }

            double? topElev = ResolvePlaneElevation(document, range, PlanViewPlane.TopClipPlane, out string? topError);
            double? bottomElev = ResolvePlaneElevation(document, range, PlanViewPlane.BottomClipPlane, out string? bottomError);

            if (topElev is null || bottomElev is null)
            {
                return VisibilityCheckItem.Error(CheckName,
                    $"Could not resolve View Range levels for '{view.Name}' (Top: {topError ?? "OK"}, Bottom: {bottomError ?? "OK"}).");
            }

            bool viewDepthUnlimited = range.GetLevelId(PlanViewPlane.ViewDepthPlane) == ElementId.InvalidElementId;
            double? viewDepthElev = null;
            if (!viewDepthUnlimited)
            {
                viewDepthElev = ResolvePlaneElevation(document, range, PlanViewPlane.ViewDepthPlane, out string? viewDepthError);
                if (viewDepthElev is null)
                {
                    return VisibilityCheckItem.Error(CheckName, $"Could not resolve View Depth level: {viewDepthError}");
                }
            }

            // Effective lower bound is the lower of View Depth and Bottom Clip
            // (View Depth is only ever set at or below Bottom in a valid view).
            double? lowerBoundElev = viewDepthUnlimited ? null : Math.Min(viewDepthElev!.Value, bottomElev.Value);

            BoundingBoxXYZ? bbox = element.get_BoundingBox(null);
            if (bbox is null)
            {
                return VisibilityCheckItem.Warning(CheckName,
                    "Element has no model-space bounding box (e.g. a non-graphical element, or an element with no solid " +
                    "geometry). View Range comparison cannot be evaluated for it.");
            }

            double elemMinZ = bbox.Min.Z;
            double elemMaxZ = bbox.Max.Z;

            bool aboveTop = elemMinZ > topElev.Value;
            bool belowLowerBound = lowerBoundElev.HasValue && elemMaxZ < lowerBoundElev.Value;

            if (aboveTop)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"Element's lowest point (Z={FeetToDisplay(elemMinZ)}) is above the view's Top Clip Plane " +
                    $"(Z={FeetToDisplay(topElev.Value)}). The element is entirely above the View Range.",
                    recommendation: "Raise the view's Top level/offset in View Range settings (View Properties > View Range > Edit).");
            }

            if (belowLowerBound)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"Element's highest point (Z={FeetToDisplay(elemMaxZ)}) is below the view's effective lower bound " +
                    $"(View Depth/Bottom, Z={FeetToDisplay(lowerBoundElev!.Value)}). The element is entirely below the View Range.",
                    recommendation: "Lower the View Depth (or Bottom Clip Plane, if View Depth equals Bottom) in View Range settings.");
            }

            return VisibilityCheckItem.Ok(CheckName,
                $"Element spans Z={FeetToDisplay(elemMinZ)} to Z={FeetToDisplay(elemMaxZ)}, which intersects the view's " +
                $"range (Top={FeetToDisplay(topElev.Value)}, " +
                (lowerBoundElev.HasValue ? $"Lower bound={FeetToDisplay(lowerBoundElev.Value)}" : "Depth=Unlimited") +
                "). View Range is not hiding this element.");
        }

        private static bool IsPlanTypeWithViewRange(ViewType viewType) =>
            viewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.AreaPlan;

        private static double? ResolvePlaneElevation(Document document, PlanViewRange range, PlanViewPlane plane, out string? error)
        {
            error = null;
            ElementId levelId = range.GetLevelId(plane);
            if (levelId == ElementId.InvalidElementId)
            {
                error = "no level assigned (unset or Unlimited)";
                return null;
            }

            if (document.GetElement(levelId) is not Level level)
            {
                error = $"level id {levelId.Value} did not resolve to a Level element";
                return null;
            }

            double offset = range.GetOffset(plane);
            return level.Elevation + offset;
        }

        private static string FeetToDisplay(double feet) => $"{feet:F2}ft";
    }
}
