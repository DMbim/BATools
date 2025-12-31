using Autodesk.Revit.DB;

namespace BA.Core.Overhead
{
    internal static class ElementIdValue
    {
        public static long Of(ElementId id)
            => id == null ? -1 : id.Value;

        public static bool IsValid(ElementId id)
            => id != null && id != ElementId.InvalidElementId;
    }
}
