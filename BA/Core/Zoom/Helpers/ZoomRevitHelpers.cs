using System;
using Autodesk.Revit.DB;
using BA.Zoom.Settings;

namespace BA.Zoom.Helpers
{
    /// <summary>
    /// Revit API parameter resolution for room ID lookup and view type validation.
    /// No geometry, no UI, no settings mutation — read-only Revit API access only.
    /// </summary>
    internal static class ZoomRevitHelper
    {
        /// <summary>
        /// Returns true when the room's resolved ID parameter value matches the expected string.
        /// Comparison is case-insensitive and trims whitespace on both sides.
        /// </summary>
        public static bool ParameterMatches(Element e, ZoomToRoomSettings settings, string expected)
        {
            var p = GetStringParameter(e, settings);
            if (p == null) return false;
            var val = p.AsString() ?? string.Empty;
            return string.Equals(val.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves the room ID parameter from the element according to the configured mode.
        /// Resolution priority:
        ///   1. Shared GUID (when mode == "Shared" and GUID parses)
        ///   2. BuiltIn ROOM_NUMBER (when mode == "BuiltIn" or unset)
        ///   3. By name (when mode == "ByName" and RoomIdName is set)
        /// Fallbacks (always attempted if above fails): ROOM_NUMBER, then BA_ID.
        /// Returns null only when no string-typed parameter is found at all.
        /// </summary>
        public static Parameter? GetStringParameter(Element e, ZoomToRoomSettings settings)
        {
            // 1. Shared GUID
            try
            {
                if (string.Equals(settings.RoomIdParamMode, "Shared", StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParse(settings.RoomIdSharedGuid, out Guid g))
                {
                    var pS = e.get_Parameter(g);
                    if (pS != null && pS.StorageType == StorageType.String) return pS;
                }
            }
            catch { }

            // 2. BuiltIn ROOM_NUMBER
            try
            {
                if (string.IsNullOrWhiteSpace(settings.RoomIdParamMode) ||
                    string.Equals(settings.RoomIdParamMode, "BuiltIn", StringComparison.OrdinalIgnoreCase))
                {
                    var pB = e.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                    if (pB != null && pB.StorageType == StorageType.String) return pB;
                }
            }
            catch { }

            // 3. By name
            try
            {
                if (!string.IsNullOrWhiteSpace(settings.RoomIdName))
                {
                    var pN = e.LookupParameter(settings.RoomIdName);
                    if (pN != null && pN.StorageType == StorageType.String) return pN;
                }
            }
            catch { }

            // Fallback: ROOM_NUMBER
            var pFallback = e.get_Parameter(BuiltInParameter.ROOM_NUMBER);
            if (pFallback != null && pFallback.StorageType == StorageType.String) return pFallback;

            // Fallback: BA_ID
            var pBA = e.LookupParameter("BA_ID");
            if (pBA != null && pBA.StorageType == StorageType.String) return pBA;

            return null;
        }

        /// <summary>
        /// Returns true for view types that support ZoomAndCenterRectangle.
        /// 3D, Section, Elevation, Schedule and drafting views are excluded.
        /// </summary>
        public static bool IsPlanLike(Autodesk.Revit.DB.View v)
        {
            if (v == null) return false;
            return v.ViewType == ViewType.FloorPlan
                || v.ViewType == ViewType.CeilingPlan
                || v.ViewType == ViewType.EngineeringPlan
                || v.ViewType == ViewType.AreaPlan;
        }
    }
}