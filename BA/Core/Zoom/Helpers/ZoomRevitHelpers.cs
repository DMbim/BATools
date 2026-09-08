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
        /// Resolves the room ID parameter from the element strictly according to the configured
        /// mode. No fallback to ROOM_NUMBER or BA_ID on failure: if the configured mode fails to
        /// resolve a valid string parameter, this returns null and the caller treats the room as
        /// not matching, rather than silently matching against a parameter the user did not
        /// actually configure in Zoom to Room Settings.
        ///   "Shared"  -> the shared parameter identified by settings.RoomIdSharedGuid
        ///   "ByName"  -> the parameter named settings.RoomIdName
        ///   "BuiltIn" or unset -> BuiltInParameter.ROOM_NUMBER
        /// </summary>
        public static Parameter? GetStringParameter(Element e, ZoomToRoomSettings settings) // <- CHANGED, full rewrite below
        {
            if (string.Equals(settings.RoomIdParamMode, "Shared", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (Guid.TryParse(settings.RoomIdSharedGuid, out Guid g))
                    {
                        var pS = e.get_Parameter(g);
                        if (pS != null && pS.StorageType == StorageType.String) return pS;
                    }
                }
                catch { }

                return null;
            }

            if (string.Equals(settings.RoomIdParamMode, "ByName", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(settings.RoomIdName))
                    {
                        var pN = e.LookupParameter(settings.RoomIdName);
                        if (pN != null && pN.StorageType == StorageType.String) return pN;
                    }
                }
                catch { }

                return null;
            }

            // "BuiltIn" or unset/unrecognized mode: BuiltInParameter.ROOM_NUMBER only, no fallback.
            try
            {
                var pB = e.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                if (pB != null && pB.StorageType == StorageType.String) return pB;
            }
            catch { }

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