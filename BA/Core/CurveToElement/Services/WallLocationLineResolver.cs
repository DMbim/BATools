// File: BA/Core/CurveToElement/Services/WallLocationLineResolver.cs
// Action: CREATE NEW

using System;
using Autodesk.Revit.DB;
using BA.BAApplication;

namespace BA.Core.CurveToElement.Services
{
    /// <summary>
    /// Applies the WallGroupSettings.LocationLine choice to a just-created wall via
    /// WALL_KEY_REF_PARAM. Must be called inside an open transaction, immediately after
    /// Wall.Create, before the transaction commits. Revit's regeneration shifts the wall
    /// geometry so the newly assigned reference line lands on the wall's existing Location
    /// Curve - this is Revit-native behavior and correctly handles both straight and
    /// arc-based walls without any manual curve offsetting.
    /// </summary>
    public class WallLocationLineResolver
    {
        /// <summary>
        /// Attempts to apply the given WallLocationLine to the wall. Returns false with a
        /// human-readable reason if the wall type does not support location line reassignment
        /// (curtain walls, stacked walls) or the parameter is missing/read-only for any other
        /// reason. Caller decides whether a failure here should abort the group's generation
        /// or just skip re-referencing for that one wall.
        /// </summary>
        public bool TryApplyLocationLine(Wall wall, WallLocationLine locationLine, out string failureReason)
        {
            if (wall == null) throw new ArgumentNullException(nameof(wall));

            failureReason = null;

            WallType wallType = wall.WallType;
            if (wallType == null)
            {
                failureReason = $"Wall {wall.Id.Value} has no resolvable WallType.";
                AppLogger.LogInfo($"[CurveToElement] {failureReason}");
                return false;
            }

            if (wallType.Kind != WallKind.Basic)
            {
                failureReason =
                    $"Wall {wall.Id.Value}: location line reassignment is only supported for Basic walls. " +
                    $"WallType '{wallType.Name}' is {wallType.Kind}. Wall was created at its default centerline placement.";
                AppLogger.LogInfo($"[CurveToElement] {failureReason}");
                return false;
            }

            Parameter locationLineParam = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
            if (locationLineParam == null)
            {
                failureReason = $"Wall {wall.Id.Value} has no WALL_KEY_REF_PARAM parameter.";
                AppLogger.LogInfo($"[CurveToElement] {failureReason}");
                return false;
            }

            if (locationLineParam.IsReadOnly)
            {
                failureReason = $"Wall {wall.Id.Value}: WALL_KEY_REF_PARAM is read-only in this context.";
                AppLogger.LogInfo($"[CurveToElement] {failureReason}");
                return false;
            }

            int targetValue = (int)locationLine;
            if (locationLineParam.AsInteger() == targetValue)
                return true; // already the requested location line, nothing to do

            bool setResult = locationLineParam.Set(targetValue);
            if (!setResult)
            {
                failureReason = $"Wall {wall.Id.Value}: Revit rejected WALL_KEY_REF_PARAM value {targetValue}.";
                AppLogger.LogInfo($"[CurveToElement] {failureReason}");
                return false;
            }

            return true;
        }
    }
}