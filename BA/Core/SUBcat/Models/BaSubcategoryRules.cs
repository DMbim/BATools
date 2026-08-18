using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Standards
{
    public static class BaSubcategoryRules
    {
        public const string Prefix = "BA_";

        // ---- Semantic role subcategories ----
        public const string Primary = "BA_Primary";
        public const string Internal = "BA_Internal";
        public const string Overhead = "BA_Overhead";
        public const string Swing = "BA_Swing";
        public const string Hidden = "BA_Hidden";
        public const string Secondary = "BA_Secondary";
        public const string Cut = "BA_Cut";
        public const string Symbolic = "BA_Symbolic";
        public const string Annotation = "BA_Annotation";

        private static readonly string[] DefaultRequired =
        {
            Primary,
            Internal
        };

        /// <summary>
        /// Always allowed globally. These should not become violations even in strict mode.
        /// </summary>
        private static readonly HashSet<string> AlwaysAllowedNonBaNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "<Hidden Lines>",
                "Hidden Lines"
            };

        /// <summary>
        /// Tolerated vendor/template/common names by category.
        /// These are allowed in normal mode, but can be disallowed in strict mode.
        /// </summary>
        private static readonly Dictionary<BuiltInCategory, HashSet<string>> CategorySpecificAllowedNonBaNames =
            new Dictionary<BuiltInCategory, HashSet<string>>
            {
                {
                    BuiltInCategory.OST_Windows,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Frame/Mullion",
                        "Frame\\Mullion",
                        "Glass",
                        "Panel",
                        "Panels",
                        "Mullion",
                        "Mullions",
                        "Opening Cut",
                        "Trim"
                    }
                },
                {
                    BuiltInCategory.OST_Doors,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Panel",
                        "Panels",
                        "Glass",
                        "Opening Cut",
                        "Trim"
                    }
                },
                {
                    BuiltInCategory.OST_DetailComponents,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Symbolic Lines"
                    }
                }
            };

        public static IReadOnlyList<string> GetRequiredSubcategories(Category category)
        {
            if (category == null)
                return DefaultRequired;

            BuiltInCategory? bic = TryGetBuiltInCategory(category);

            if (bic == null)
                return DefaultRequired;

            switch (bic.Value)
            {
                case BuiltInCategory.OST_Doors:
                    return new[]
                    {
                        Primary,
                        Internal,
                        Overhead,
                        Swing
                    };

                case BuiltInCategory.OST_Windows:
                    return new[]
                    {
                        Primary,
                        Internal,
                        Overhead
                    };

                case BuiltInCategory.OST_Furniture:
                case BuiltInCategory.OST_Casework:
                case BuiltInCategory.OST_GenericModel:
                case BuiltInCategory.OST_PlumbingFixtures:
                case BuiltInCategory.OST_SpecialityEquipment:
                case BuiltInCategory.OST_MechanicalEquipment:
                    return new[]
                    {
                        Primary,
                        Internal
                    };

                case BuiltInCategory.OST_DetailComponents:
                    return new[]
                    {
                        Primary,
                        Internal,
                        Symbolic
                    };

                default:
                    return DefaultRequired;
            }
        }

        public static bool IsBaName(string subcategoryName)
        {
            if (string.IsNullOrWhiteSpace(subcategoryName))
                return false;

            return subcategoryName.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAlwaysAllowedNonBaName(string subcategoryName)
        {
            if (string.IsNullOrWhiteSpace(subcategoryName))
                return false;

            return AlwaysAllowedNonBaNames.Contains(subcategoryName.Trim());
        }

        public static bool IsCategoryAllowedNonBaName(Category familyCategory, string subcategoryName)
        {
            if (familyCategory == null || string.IsNullOrWhiteSpace(subcategoryName))
                return false;

            BuiltInCategory? bic = TryGetBuiltInCategory(familyCategory);
            if (bic == null)
                return false;

            if (!CategorySpecificAllowedNonBaNames.TryGetValue(bic.Value, out HashSet<string> allowed))
                return false;

            return allowed.Contains(subcategoryName.Trim());
        }

        public static List<string> GetValidBaNamesFound(IEnumerable<string> names)
        {
            return (names ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(IsBaName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetAllowedNonBaNamesFound(
            Category familyCategory,
            IEnumerable<string> names,
            bool strictMode)
        {
            return (names ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x =>
                    IsAlwaysAllowedNonBaName(x) ||
                    (!strictMode && IsCategoryAllowedNonBaName(familyCategory, x)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetNonCompliantCustomNames(
            Category familyCategory,
            IEnumerable<string> names,
            bool strictMode)
        {
            return (names ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !IsBaName(x))
                .Where(x => !IsAlwaysAllowedNonBaName(x))
                .Where(x => strictMode || !IsCategoryAllowedNonBaName(familyCategory, x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetMissingRequired(IEnumerable<string> existingNames, IEnumerable<string> requiredNames)
        {
            HashSet<string> existing = new HashSet<string>(
                (existingNames ?? Enumerable.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()),
                StringComparer.OrdinalIgnoreCase);

            return (requiredNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(req => !existing.Contains(req))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static BuiltInCategory? TryGetBuiltInCategory(Category category)
        {
            if (category == null || category.Id == null)
                return null;

            long raw = category.Id.Value;

            if (raw < int.MinValue || raw > int.MaxValue)
                return null;

            int intValue = (int)raw;
            BuiltInCategory bic = (BuiltInCategory)intValue;

            if (!Enum.IsDefined(typeof(BuiltInCategory), bic))
                return null;

            return bic;
        }
    }
}