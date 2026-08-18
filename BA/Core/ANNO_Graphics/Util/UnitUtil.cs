using Autodesk.Revit.DB;

namespace BA.UI.TextHub
{
    public static class UnitUtil
    {
        public static double InternalToMm(Document doc, double internalLength)
        {
#if REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024
            // For older multi-targeting you might use UnitUtils.ConvertFromInternalUnits(internalLength, DisplayUnitType.DUT_MILLIMETERS)
            return UnitUtils.ConvertFromInternalUnits(internalLength, DisplayUnitType.DUT_MILLIMETERS);
#else
            return UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Millimeters);
#endif
        }

        public static double MmToInternal(Document doc, double mm)
        {
#if REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024
            return UnitUtils.ConvertToInternalUnits(mm, DisplayUnitType.DUT_MILLIMETERS);
#else
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
#endif
        }
    }
}