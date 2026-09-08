using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace BA.Subcategories.Services
{
    /// <summary>
    /// Defines the built-in BA subcategory sets per family category.
    /// </summary>
    public static class BaSubcategoryCatalog
    {
        public static readonly IReadOnlyList<string> Core = new List<string>
        {
            "BA_Frame",
            "BA_Panel",
            "BA_Glass",
            "BA_Main",
            "BA_Structure",
            "BA_Hardware",
            "BA_Secondary",
            "BA_Finish",

        };

        private static readonly Dictionary<string, IReadOnlyList<string>> ExtrasByName =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Doors",               new List<string> { "BA_Threshold", "BA_SwingSymbol", "BA_Trim" } },
                { "Windows",             new List<string> { "BA_Sill", "BA_Reveal", "BA_Sash" } },
                { "Casework",            new List<string> { "BA_Carcass", "BA_DrawerFront", "BA_Kickboard","BA_Drawers","BA_Shelves","BA_Countertop" } },
                { "Furniture",           new List<string> { "BA_Upholstery", "BA_Backrest", "BA_Drawers", "BA_Shelves", "BA_Countertop" } },
                { "Curtain Panels",      new List<string> { "BA_Panel_Vision", "BA_Panel_Spandrel", "BA_Panel_Louver" } },
                { "Curtain Wall Mullions", new List<string> { "BA_Mullion_Vertical", "BA_Mullion_Horizontal", "BA_PressurePlate" } },
            };

        private static readonly Dictionary<BuiltInCategory, IReadOnlyList<string>> ExtrasByBic =
            new()
            {
                { BuiltInCategory.OST_Doors,               new List<string> { "BA_Threshold", "BA_SwingSymbol", "BA_Trim" } },
                { BuiltInCategory.OST_Windows,             new List<string> { "BA_Sill", "BA_Reveal", "BA_Sash" } },
                { BuiltInCategory.OST_Casework,            new List<string> { "BA_Carcass", "BA_DrawerFront", "BA_Kickboard","BA_Drawers","BA_Shelves","BA_Countertop" } },
                { BuiltInCategory.OST_Furniture,           new List<string> { "BA_Upholstery", "BA_Backrest", "BA_Drawers", "BA_Shelves", "BA_Countertop" } },
                { BuiltInCategory.OST_CurtainWallPanels,   new List<string> { "BA_Panel_Vision", "BA_Panel_Spandrel", "BA_Panel_Louver" } },
                { BuiltInCategory.OST_CurtainWallMullions, new List<string> { "BA_Mullion_Vertical", "BA_Mullion_Horizontal", "BA_PressurePlate" } },
            };

        public static IEnumerable<string> GetExtrasForFamilyCategory(Category famCat)
        {
            if (famCat == null) yield break;

            // Try name match first
            if (ExtrasByName.TryGetValue(famCat.Name ?? string.Empty, out var byName))
            {
                foreach (var s in byName) yield return s;
                yield break;
            }

            // Fall back to BuiltInCategory
            IReadOnlyList<string>? byBic = null;
            try
            {
                var bic = (BuiltInCategory)famCat.Id.Value;
                ExtrasByBic.TryGetValue(bic, out byBic);
            }
            catch
            {
                // unrecognised category — no extras
            }

            if (byBic != null)
            {
                for (int i = 0; i < byBic.Count; i++)
                {
                    string? s = byBic[i];
                    yield return s;
                }
            }
        }
    }
}
