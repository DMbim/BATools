using Autodesk.Revit.DB;
using System.Collections.Generic;

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
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_ElectricalFixtures
        };

        public bool Enabled { get; set; } = true;

        public bool UseNextLevelAsTop { get; set; } = true;

        // UI values in mm (stored in ES as strings)
        public double FallbackCutMm { get; set; } = 1200.0;
        public double TinyThresholdMm { get; set; } = 50.0;

        // proxy creation
        public double MinProxyEdgeMm { get; set; } = 0.5;

        public static OverheadSettings Default() => new OverheadSettings();

        public void Normalize()
        {
            SelectedCategories ??= new HashSet<BuiltInCategory>();
            if (FallbackCutMm <= 0) FallbackCutMm = 1200.0;
            if (TinyThresholdMm < 0) TinyThresholdMm = 0.0;
            if (MinProxyEdgeMm < 0) MinProxyEdgeMm = 0.0;
        }
    }
}