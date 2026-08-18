using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;

namespace BA.Commands.Rooms
{
    public static class ParameterUtil
    {
        /// <summary>
        /// Reads parameter value as string.
        /// Tries instance first; if missing and allowTypeFallback==true, tries element type.
        /// </summary>
        public static string ReadAsString(Document doc, Element e, string paramName, bool allowTypeFallback = true)
        {
            if (doc == null) return "";
            if (e == null) return "";
            if (string.IsNullOrWhiteSpace(paramName)) return "";

            // 1) Instance
            var p = e.LookupParameter(paramName);
            if (p != null)
            {
                var s = ReadParamToString(p);
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            if (!allowTypeFallback)
                return "";

            // 2) Type fallback
            try
            {
                var typeId = e.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var type = doc.GetElement(typeId);
                    var tp = type?.LookupParameter(paramName);
                    if (tp != null)
                    {
                        var ts = ReadParamToString(tp);
                        if (!string.IsNullOrWhiteSpace(ts))
                            return ts;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return "";
        }

        private static string ReadParamToString(Parameter p)
        {
            if (p == null) return "";

            try
            {
                if (p.StorageType == StorageType.String)
                    return p.AsString() ?? "";

                var vs = p.AsValueString();
                if (!string.IsNullOrWhiteSpace(vs))
                    return vs;

                return p.StorageType switch
                {
                    StorageType.Integer => p.AsInteger().ToString(),
                    StorageType.Double => p.AsDouble().ToString("G"),
                    StorageType.ElementId => p.AsElementId().Value.ToString(),
                    _ => ""
                };
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Writes to a Room instance parameter.
        /// Tries LookupParameter by name first; if not found, falls back to BuiltInParameter for Ceiling/Floor Finish.
        /// </summary>
        public static bool WriteToRoom(Room room, string targetParamName, string value, bool onlyIfEmpty)
        {
            if (room == null) return false;
            if (string.IsNullOrWhiteSpace(targetParamName)) return false;

            Parameter p = room.LookupParameter(targetParamName);

            // Built-in fallback (handles localization)
            if (p == null)
            {
                var key = targetParamName.Trim().ToLowerInvariant();

                if (key == "ceiling finish" || key == "ceilingfinish" || key == "ceiling_finish")
                    p = room.get_Parameter(BuiltInParameter.ROOM_FINISH_CEILING);

                if (key == "floor finish" || key == "floorfinish" || key == "floor_finish")
                    p = room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR);
            }

            if (p == null || p.IsReadOnly) return false;

            if (onlyIfEmpty)
            {
                var existing = SafeRead(p);
                if (!string.IsNullOrWhiteSpace(existing))
                    return false;
            }

            try
            {
                if (p.StorageType == StorageType.String)
                    return p.Set(value);

                return p.SetValueString(value);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeRead(Parameter p)
        {
            try
            {
                if (p.StorageType == StorageType.String)
                    return p.AsString() ?? "";

                var vs = p.AsValueString();
                return vs ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}