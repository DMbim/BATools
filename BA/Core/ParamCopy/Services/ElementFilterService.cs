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

            // Apply each filter set — sets with no valid rules are skipped entirely
            // rather than treating them as pass-all, which was the original bug.
            foreach (var set in settings.FilterSets)
            {
                var activeRules = set.Rules
                    .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName))
                    .ToList();

                if (activeRules.Count == 0)
                    continue; // <- CHANGED: skip empty sets rather than pass-all

                elements = elements.Where(e => EvaluateSet(e, set, activeRules));
            }

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
        /// Evaluates a set using only the pre-filtered active rules.
        /// Passing activeRules avoids re-filtering inside the per-element loop.
        /// </summary>
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