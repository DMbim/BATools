using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanFilledRegionUtils
    {
        public static FilledRegionType FindFilledRegionTypeByName(Document doc, string name)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}