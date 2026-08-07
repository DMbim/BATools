using Autodesk.Revit.DB;
using BATools.ParamCopy.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using ParamFilterRule = BATools.ParamCopy.Models.FilterRule;
using ParamFilterSet = BATools.ParamCopy.Models.FilterSet;

namespace BATools.ParamCopy.Services
{
    public static class ElementFilterService
    {
        /// <summary>
        /// Collects elements by category, applies filter sets in memory,
        /// and resolves all display parameter values per element.
        /// </summary>
        public static List<ElementListItem> Collect(
            Document doc,
            ListSettings settings)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            IEnumerable<Element> elements = CollectByCategory(doc, settings.CategoryName);
            elements = ApplyFilterSets(elements, settings.FilterSets);

            var displayParams = settings.DisplayParameterNames
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            var result = new List<ElementListItem>();

            foreach (var e in elements)
            {
                var paramValues = new Dictionary<string, string>();
                foreach (var paramName in displayParams)
                    paramValues[paramName] = GetParamString(e, paramName);

                result.Add(new ElementListItem
                {
                    ElementId = e.Id,
                    Category = e.Category?.Name ?? string.Empty,
                    Name = e.Name ?? string.Empty,
                    ParameterValues = paramValues
                });
            }

            return result;
        }

        /// <summary>
        /// Returns the distinct, sorted set of instance parameter names present
        /// on elements of the given category. If filterSets is null, the current
        /// FilterSets are NOT applied (used for the filter-rule ParameterName
        /// dropdown itself, which cannot depend on the filter it defines). If
        /// filterSets is provided, it is applied first (used for Display Params,
        /// Pairing Parameter, and Mapping column dropdowns, which reflect only
        /// the elements that will actually be in play).
        /// </summary>
        public static List<string> CollectParameterNames(
            Document doc,
            string categoryName,
            IReadOnlyList<ParamFilterSet>? filterSets = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            IEnumerable<Element> elements = CollectByCategory(doc, categoryName);

            if (filterSets != null)
                elements = ApplyFilterSets(elements, filterSets);

            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in elements)
            {
                // Element.Parameters is instance-only by design — this must stay
                // in sync with LookupParameter, which is what ParamCopyEngine
                // actually writes through. Type parameters are intentionally
                // excluded; see conversation notes if that scope changes.
                foreach (Parameter p in e.Parameters)
                {
                    string? n = p.Definition?.Name;
                    if (!string.IsNullOrWhiteSpace(n))
                        names.Add(n);
                }
            }

            return names.ToList();
        }

        /// <summary>
        /// Returns the sorted names of Model-type categories that have at least
        /// one instance in the active document. Uses a per-category existence
        /// probe (FirstElementId) rather than a full document scan, so this
        /// stays cheap even on large workshared models.
        /// </summary>
        public static List<string> CollectCategoryNamesInDocument(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var names = new List<string>();

            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat.CategoryType != CategoryType.Model)
                    continue;

                ElementId firstId = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementCategoryFilter(cat.Id))
                    .FirstElementId();

                if (firstId != null && firstId != ElementId.InvalidElementId)
                    names.Add(cat.Name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // ── Category collector ────────────────────────────────────────────────

        private static IEnumerable<Element> CollectByCategory(
            Document doc, string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .ToList();
            }

            Category? cat = doc.Settings.Categories
                .Cast<Category>()
                .FirstOrDefault(c =>
                    string.Equals(c.Name, categoryName,
                        StringComparison.OrdinalIgnoreCase));

            if (cat == null)
                return Enumerable.Empty<Element>();

            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementCategoryFilter(cat.Id))
                .ToList();
        }

        // ── Filter set evaluation ─────────────────────────────────────────────

        /// <summary>
        /// Applies every filter set in sequence (AND across sets). Sets with no
        /// valid rules are skipped entirely rather than treated as pass-all —
        /// this must stay identical to the logic Collect() previously had inline,
        /// now shared with CollectParameterNames() so there is one filter-matching
        /// implementation instead of two.
        /// </summary>
        private static IEnumerable<Element> ApplyFilterSets(
            IEnumerable<Element> elements,
            IReadOnlyList<ParamFilterSet> filterSets)
        {
            foreach (var set in filterSets)
            {
                var activeRules = set.Rules
                    .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName))
                    .ToList();

                if (activeRules.Count == 0)
                    continue;

                elements = elements.Where(e => EvaluateSet(e, set, activeRules));
            }

            return elements;
        }

        private static bool EvaluateSet(
            Element e,
            ParamFilterSet set,
            List<ParamFilterRule> activeRules)
        {
            if (set.Operator == FilterSetOperator.And)
                return activeRules.All(r => EvaluateRule(e, r));
            else
                return activeRules.Any(r => EvaluateRule(e, r));
        }

        private static bool EvaluateRule(Element e, ParamFilterRule rule)
        {
            string val = GetParamString(e, rule.ParameterName);
            bool hasValue = !string.IsNullOrEmpty(val);

            return rule.Condition switch
            {
                FilterCondition.HasValue => hasValue,
                FilterCondition.HasNoValue => !hasValue,
                FilterCondition.Equals => string.Equals(val, rule.Value,
                                                StringComparison.OrdinalIgnoreCase),
                FilterCondition.NotEquals => !string.Equals(val, rule.Value,
                                                StringComparison.OrdinalIgnoreCase),
                FilterCondition.Contains => val.IndexOf(rule.Value,
                                                StringComparison.OrdinalIgnoreCase) >= 0,
                FilterCondition.NotContains => val.IndexOf(rule.Value,
                                                StringComparison.OrdinalIgnoreCase) < 0,
                FilterCondition.GreaterThan => double.TryParse(val, out double gd)
                                              && double.TryParse(rule.Value, out double gv)
                                              && gd > gv,
                FilterCondition.LessThan => double.TryParse(val, out double ld)
                                              && double.TryParse(rule.Value, out double lv)
                                              && ld < lv,
                _ => true
            };
        }

        // ── Parameter string reader ───────────────────────────────────────────

        public static string GetParamString(Element e, string paramName)
        {
            Parameter? p = e.LookupParameter(paramName);
            if (p == null) return string.Empty;

            return p.StorageType switch
            {
                StorageType.String => p.AsString() ?? string.Empty,
                StorageType.Double => p.AsValueString() ?? string.Empty,
                StorageType.Integer => p.AsInteger().ToString(),
                StorageType.ElementId => p.AsElementId()?.ToString() ?? string.Empty,
                _ => string.Empty
            };
        }
    }
}