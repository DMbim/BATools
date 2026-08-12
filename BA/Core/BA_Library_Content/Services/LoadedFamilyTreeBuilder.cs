using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Content.Models;

namespace BA.Core.Content.Services
{
    /// <summary>
    /// Must be executed inside Revit API context (via AppExternalInvoker).
    /// Builds Category -> Family -> Type tree from currently loaded
    /// FamilySymbols, with per-type instance counts.
    ///
    /// Instance counting is based on FilteredElementCollector over
    /// FamilyInstance, grouped by GetTypeId(). This covers standard
    /// component families placed as FamilyInstance. Annotation-type
    /// families that use a different instance element class (tag types,
    /// text note types, dimension types) are not accurately counted here
    /// and will show as unused even if placed. This matches the same
    /// heuristic limitation already agreed for purge.
    /// </summary>
    public static class LoadedFamilyTreeBuilder
    {
        public static List<LoadedCategoryNode> Build(Document doc, LoadedFamilyBrowserSettings settings)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var instanceCounts = new Dictionary<long, int>();

            var instanceCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance));

            foreach (FamilyInstance instance in instanceCollector.Cast<FamilyInstance>())
            {
                ElementId typeId = instance.GetTypeId();
                if (typeId == ElementId.InvalidElementId)
                    continue;

                long key = typeId.Value;
                instanceCounts[key] = instanceCounts.TryGetValue(key, out int existing) ? existing + 1 : 1;
            }

            var symbolCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol));

            var categories = new Dictionary<string, LoadedCategoryNode>(StringComparer.OrdinalIgnoreCase);
            var familiesByCategory = new Dictionary<string, Dictionary<long, LoadedFamilyNode>>(StringComparer.OrdinalIgnoreCase);

            foreach (FamilySymbol symbol in symbolCollector.Cast<FamilySymbol>())
            {
                Category? category = symbol.Category;
                string categoryName = category?.Name ?? "Uncategorized";

                if (settings.CategoryFilter.TryGetValue(categoryName, out bool enabled) && !enabled)
                    continue;

                if (!categories.TryGetValue(categoryName, out LoadedCategoryNode? categoryNode))
                {
                    categoryNode = new LoadedCategoryNode
                    {
                        Name = categoryName,
                        CategoryId = category?.Id ?? ElementId.InvalidElementId
                    };
                    categories[categoryName] = categoryNode;
                    familiesByCategory[categoryName] = new Dictionary<long, LoadedFamilyNode>();
                }

                Family family = symbol.Family;
                long familyKey = family.Id.Value;

                var familyLookup = familiesByCategory[categoryName];
                if (!familyLookup.TryGetValue(familyKey, out LoadedFamilyNode? familyNode))
                {
                    familyNode = new LoadedFamilyNode
                    {
                        Name = family.Name,
                        FamilyId = family.Id,
                        CategoryName = categoryName
                    };
                    familyLookup[familyKey] = familyNode;
                    categoryNode.Families.Add(familyNode);
                }

                int instanceCount = instanceCounts.TryGetValue(symbol.Id.Value, out int count) ? count : 0;

                var typeNode = new LoadedTypeNode
                {
                    TypeId = symbol.Id,
                    FamilyId = family.Id,
                    ParentFamilyName = family.Name,
                    CategoryName = categoryName,
                    Name = symbol.Name,
                    InstanceCount = instanceCount
                };

                familyNode.Types.Add(typeNode);
            }

            foreach (var categoryNode in categories.Values)
            {
                foreach (var familyNode in categoryNode.Families)
                {
                    SortTypes(familyNode);
                    familyNode.RecomputeUnusedState();
                }

                SortFamilies(categoryNode);
            }

            return categories.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void SortTypes(LoadedFamilyNode familyNode)
        {
            var sorted = familyNode.Types
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            familyNode.Types.Clear();
            foreach (var t in sorted)
                familyNode.Types.Add(t);
        }

        private static void SortFamilies(LoadedCategoryNode categoryNode)
        {
            var sorted = categoryNode.Families
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            categoryNode.Families.Clear();
            foreach (var f in sorted)
                categoryNode.Families.Add(f);
        }

        public static IEnumerable<string> GetDistinctCategoryNames(List<LoadedCategoryNode> tree)
        {
            return tree.Select(c => c.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        }
    }
}