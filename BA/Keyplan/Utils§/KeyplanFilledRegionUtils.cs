using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanFilledRegionUtils
    {
        public static ElementId FindFilledRegionTypeIdByName(Document doc, string typeName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(typeName)) return ElementId.InvalidElementId;

            FilledRegionType frt = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(x => string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));

            return frt?.Id ?? ElementId.InvalidElementId;
        }

        public static string GetZoneCodeFromFilledRegion(FilledRegion fr)
        {
            if (fr == null) return string.Empty;

            Parameter p = fr.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (p == null) return string.Empty;
            if (p.StorageType != StorageType.String) return string.Empty;

            return (p.AsString() ?? string.Empty).Trim();
        }
    }
}