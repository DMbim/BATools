using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.Core.Content.Services
{
    /// <summary>
    /// Classifies a loaded category's display name into (Tab, Subgroup).
    /// Matching is by category name, case-insensitive, since BuiltInCategory
    /// enum membership varies across custom/loadable categories that don't
    /// map to a BuiltInCategory at all. Falls back to Architecture/Site &amp;
    /// Other for anything unmatched so no category silently disappears.
    ///
    /// Classification table locked 2026-08-13 per user confirmation:
    /// - Datum Elements placed under Annotation despite being CategoryType.Model
    /// - Structural is a subgroup within Architecture, not a 4th tab
    /// - Plumbing Fixtures placed under Architecture/Fixtures, not MEP
    /// - Generic Models is its own subgroup within Architecture
    /// </summary>
    public static class LoadedFamilyDisciplineClassifier
    {
        public const string TabAnnotation = "Annotation";
        public const string TabArchitecture = "Architecture";
        public const string TabMep = "MEP";

        private const string SubTags = "Tags";
        private const string SubDatumElements = "Datum Elements";
        private const string SubDetailItems = "Detail Items";
        private const string SubAnnotationOther = "Other";

        private const string SubBuildingElements = "Building Elements";
        private const string SubFixturesEquipment = "Fixtures / Equipment";
        private const string SubStructuralElements = "Structural Elements";
        private const string SubGenericModels = "Generic Models";
        private const string SubSiteOther = "Site / Other";

        private const string SubMechanical = "Mechanical";
        private const string SubElectrical = "Electrical";
        private const string SubFireProtection = "Fire Protection";

        private static readonly HashSet<string> DatumElementNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Levels", "Grids", "Reference Planes", "Reference Lines", "Reference Points"
        };

        private static readonly HashSet<string> DetailItemNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Detail Items"
        };

        private static readonly HashSet<string> BuildingElementNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Walls", "Floors", "Ceilings", "Roofs", "Doors", "Windows",
            "Curtain Panels", "Curtain Wall Mullions", "Curtain Systems",
            "Stairs", "Stair Landings", "Stair Runs", "Railings", "Ramps"
        };

        private static readonly HashSet<string> FixturesEquipmentNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Casework", "Furniture", "Furniture Systems", "Specialty Equipment", "Plumbing Fixtures"
        };

        private static readonly HashSet<string> StructuralNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Structural Framing", "Structural Columns", "Structural Foundations",
            "Structural Stiffeners", "Structural Connections", "Structural Trusses"
        };

        private static readonly HashSet<string> GenericModelNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Generic Models"
        };

        private static readonly HashSet<string> MechanicalNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Mechanical Equipment", "Air Terminals", "Duct Accessories", "Duct Fittings"
        };

        private static readonly HashSet<string> ElectricalNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Lighting Fixtures", "Lighting Devices", "Electrical Equipment", "Electrical Fixtures",
            "Communication Devices", "Data Devices", "Fire Alarm Devices",
            "Nurse Call Devices", "Security Devices", "Telephone Devices"
        };

        private static readonly HashSet<string> FireProtectionNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Sprinklers", "Fire Protection"
        };

        public static (string Tab, string Subgroup) Classify(string categoryName, CategoryType categoryType)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return (TabArchitecture, SubSiteOther);

            // Tags: any category name ending in "Tags", regardless of BuiltInCategory
            // membership, so custom/shared tag categories are caught automatically.
            if (categoryName.EndsWith("Tags", StringComparison.OrdinalIgnoreCase))
                return (TabAnnotation, SubTags);

            if (DatumElementNames.Contains(categoryName))
                return (TabAnnotation, SubDatumElements);

            if (DetailItemNames.Contains(categoryName))
                return (TabAnnotation, SubDetailItems);

            if (categoryType == CategoryType.Annotation)
                return (TabAnnotation, SubAnnotationOther);

            if (MechanicalNames.Contains(categoryName))
                return (TabMep, SubMechanical);

            if (ElectricalNames.Contains(categoryName))
                return (TabMep, SubElectrical);

            if (FireProtectionNames.Contains(categoryName))
                return (TabMep, SubFireProtection);

            if (BuildingElementNames.Contains(categoryName))
                return (TabArchitecture, SubBuildingElements);

            if (FixturesEquipmentNames.Contains(categoryName))
                return (TabArchitecture, SubFixturesEquipment);

            if (StructuralNames.Contains(categoryName))
                return (TabArchitecture, SubStructuralElements);

            if (GenericModelNames.Contains(categoryName))
                return (TabArchitecture, SubGenericModels);

            // Fallback: nothing silently disappears.
            return (TabArchitecture, SubSiteOther);
        }
    }
}