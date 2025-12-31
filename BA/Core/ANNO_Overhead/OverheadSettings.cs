using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.Core.Overhead
{
    public sealed class OverheadSettings
    {
        public HashSet<BuiltInCategory> SelectedCategories { get; set; } = new()
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_SpecialityEquipment,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_CurtainWallPanels,
            BuiltInCategory.OST_CurtainWallMullions,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Floors,
        };

        public bool UseNextLevelAsTop { get; set; } = true;
        public double FallbackCutMm { get; set; } = 1200.0;
        public double TinyThresholdMm { get; set; } = 50.0;
        public double MinProxyEdgeMm { get; set; } = 0.5;

        public static OverheadSettings Default() => new OverheadSettings();

        public void Normalize()
        {
            if (FallbackCutMm <= 0) FallbackCutMm = 1200.0;
            if (TinyThresholdMm < 0) TinyThresholdMm = 0.0;
            if (MinProxyEdgeMm < 0) MinProxyEdgeMm = 0.0;
            SelectedCategories ??= new HashSet<BuiltInCategory>();
        }
    }
}
