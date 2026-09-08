using Autodesk.Revit.DB;

namespace BA.Core.Classification
{
    public static class ParameterUtils
    {
        public static Parameter? GetParam(Element e, string name)
        {
            if (e == null || string.IsNullOrWhiteSpace(name))
                return null;

            return e.LookupParameter(name);
        }

        public static bool IsEmpty(Parameter? p)
        {
            if (p == null) return true;

            switch (p.StorageType)
            {
                case StorageType.String:
                    return string.IsNullOrWhiteSpace(p.AsString());

                case StorageType.Integer:
                    // For classification subcodes 0 is still a value => never "empty".
                    return false;

                case StorageType.Double:
                    return false;

                case StorageType.ElementId:
                    return p.AsElementId() == ElementId.InvalidElementId;

                default:
                    return true;
            }
        }

        // NEW: reads a parameter's display/string value regardless of storage type. Added so that
        // BA.RoomClassification's ParameterWriteUtil.GetString could delegate here instead of
        // duplicating this exact StorageType switch in a second class. Behavior matches what that
        // class already did: String storage returns AsString(), everything else falls back to
        // AsValueString(). Null parameter returns "" rather than throwing, since callers (e.g.
        // Room Classification's upsert-by-code lookup) need to treat "not set yet" the same as
        // "blank", not as an error.
        public static string GetString(Parameter? p)
        {
            if (p == null) return string.Empty;
            return p.StorageType == StorageType.String
                ? p.AsString() ?? string.Empty
                : p.AsValueString() ?? string.Empty;
        }

        public static bool SetString(Parameter? p, string value)
        {
            if (p == null || p.IsReadOnly) return false;
            if (p.StorageType != StorageType.String) return false;
            return p.Set(value ?? string.Empty);
        }

        public static bool SetIntOrString(Parameter? p, int value)
        {
            if (p == null || p.IsReadOnly) return false;

            if (p.StorageType == StorageType.Integer)
                return p.Set(value);

            if (p.StorageType == StorageType.String)
                return p.Set(value.ToString());

            return false;
        }
    }
}
