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
        public static List<LoadedDisciplineTabNode> BuildGrouped(Document doc, LoadedFamilyBrowserSettings settings)
        {
            List<LoadedCategoryNode> flatCategories = Build(doc, settings);

            var tabs = new Dictionary<string, LoadedDisciplineTabNode>(StringComparer.OrdinalIgnoreCase);
            var subgroupLookup = new Dictionary<(string Tab, string Sub), LoadedSubgroupNode>();

            // Fixed tab order regardless of which tabs actually have content,
            // so the UI doesn't reorder itself between refreshes.
            string[] tabOrder =
            {
                LoadedFamilyDisciplineClassifier.TabAnnotation,
                LoadedFamilyDisciplineClassifier.TabArchitecture,
                LoadedFamilyDisciplineClassifier.TabMep
            };

            foreach (string tabName in tabOrder)
            {
                tabs[tabName] = new LoadedDisciplineTabNode { Name = tabName };
            }

            foreach (LoadedCategoryNode category in flatCategories)
            {
                CategoryType categoryType = ResolveCategoryType(doc, category.CategoryId);

                (string tabName, string subgroupName) =
                    LoadedFamilyDisciplineClassifier.Classify(category.Name, categoryType);

                if (!tabs.TryGetValue(tabName, out LoadedDisciplineTabNode? tab))
                {
                    tab = new LoadedDisciplineTabNode { Name = tabName };
                    tabs[tabName] = tab;
                }

                var key = (tabName, subgroupName);
                if (!subgroupLookup.TryGetValue(key, out LoadedSubgroupNode? subgroup))
                {
                    subgroup = new LoadedSubgroupNode { Name = subgroupName };
                    subgroupLookup[key] = subgroup;
                    tab.Subgroups.Add(subgroup);
                }

                subgroup.Categories.Add(category);
            }

            var result = new List<LoadedDisciplineTabNode>();
            foreach (string tabName in tabOrder)
            {
                LoadedDisciplineTabNode tab = tabs[tabName];

                // Sort subgroups alphabetically within each tab, categories
                // within each subgroup are already alphabetically sorted by
                // the existing Build() call.
                var sortedSubgroups = new List<LoadedSubgroupNode>(tab.Subgroups);
                sortedSubgroups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                tab.Subgroups.Clear();
                foreach (var sub in sortedSubgroups)
                    tab.Subgroups.Add(sub);

                result.Add(tab);
            }
            result.Add(BuildSystemFamilyTab(doc, settings));
            return result;
        }
        private static readonly BuiltInCategory[] SystemFamilyCategories =
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Ceilings
        };

        /// <summary>
        /// Builds the System Families tab (Walls, Floors, Roofs, Ceilings).
        /// These are ElementType-based, not FamilySymbol-based, so they get
        /// their own collection path. Each category gets exactly one
        /// synthetic LoadedFamilyNode (FamilyId = InvalidElementId, name
        /// matches the category) so the existing Category -> Family -> Type
        /// tree shape, XAML templates, and purge/favorite logic work
        /// unchanged. Stairs and Railings deliberately excluded for now.
        /// </summary>
        public static LoadedDisciplineTabNode BuildSystemFamilyTab(Document doc, LoadedFamilyBrowserSettings settings)
        {
            var tab = new LoadedDisciplineTabNode { Name = "System Families" };
            var subgroup = new LoadedSubgroupNode { Name = "System Types" };
            tab.Subgroups.Add(subgroup);

            foreach (BuiltInCategory bic in SystemFamilyCategories)
            {
                Category? category = Category.GetCategory(doc, bic);
                if (category == null)
                    continue;

                string categoryName = category.Name;
                if (settings.CategoryFilter.TryGetValue(categoryName, out bool enabled) && !enabled)
                    continue;

                var instanceCounts = new Dictionary<long, int>();

                var instanceCollector = new FilteredElementCollector(doc)
                    .OfCategoryId(category.Id)
                    .WhereElementIsNotElementType();

                foreach (Element instance in instanceCollector)
                {
                    ElementId typeId = instance.GetTypeId();
                    if (typeId == ElementId.InvalidElementId)
                        continue;

                    long key = typeId.Value;
                    instanceCounts[key] = instanceCounts.TryGetValue(key, out int existing) ? existing + 1 : 1;
                }

                var typeCollector = new FilteredElementCollector(doc)
                    .OfCategoryId(category.Id)
                    .WhereElementIsElementType();

                var familyNode = new LoadedFamilyNode
                {
                    Name = categoryName,
                    FamilyId = ElementId.InvalidElementId,
                    CategoryName = categoryName
                };

                foreach (ElementType elementType in typeCollector.Cast<ElementType>())
                {
                    int count = instanceCounts.TryGetValue(elementType.Id.Value, out int c) ? c : 0;

                    familyNode.Types.Add(new LoadedTypeNode
                    {
                        TypeId = elementType.Id,
                        FamilyId = ElementId.InvalidElementId,
                        ParentFamilyName = categoryName,
                        CategoryName = categoryName,
                        Name = elementType.Name,
                        InstanceCount = count
                    });
                }

                if (familyNode.Types.Count == 0)
                    continue;

                var sortedTypes = familyNode.Types
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                familyNode.Types.Clear();
                foreach (var t in sortedTypes)
                    familyNode.Types.Add(t);

                familyNode.RecomputeUnusedState();

                var categoryNode = new LoadedCategoryNode
                {
                    Name = categoryName,
                    CategoryId = category.Id
                };
                categoryNode.Families.Add(familyNode);

                subgroup.Categories.Add(categoryNode);
            }

            return tab;
        }
        private static CategoryType ResolveCategoryType(Document doc, ElementId categoryId)
        {
            if (categoryId == ElementId.InvalidElementId)
                return CategoryType.Model;

            Category? category = Category.GetCategory(doc, categoryId);
            return category?.CategoryType ?? CategoryType.Model;
        }
        public static IEnumerable<string> GetDistinctCategoryNames(List<LoadedCategoryNode> tree)
        {
            return tree.Select(c => c.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        }
    }
}