// File: BA.Core/ViewFilters/ParameterFilterGenerationService.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.ViewFilters
{
    public static class ParameterFilterGenerationService
    {
        public static int GenerateAndApply(Document doc, ElementId templateId, ParameterColorRule rule)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (rule.Buckets == null || rule.Buckets.Count == 0) return 0;

            var template = doc.GetElement(templateId) as View;
            if (template == null || !template.IsTemplate)
                throw new InvalidOperationException("Selected element is not a valid view template.");

            var categoryIds = new List<ElementId> { rule.CategoryId };
            var assignments = new List<FilterColorAssignment>();

            foreach (var bucket in rule.Buckets)
            {
                var rules = BuildRules(rule, bucket);
                var elemFilter = new ElementParameterFilter(rules);

                string rawName = $"BA_{rule.CategoryName}_{rule.ParameterName}_{bucket.Label}";
                string filterName = SanitizeFilterName(rawName);

                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => f.Name.Equals(filterName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    var currentFilters = template.GetFilters();
                    if (currentFilters != null && currentFilters.Contains(existing.Id))
                        template.RemoveFilter(existing.Id);

                    doc.Delete(existing.Id);
                }

                var created = ParameterFilterElement.Create(doc, filterName, categoryIds, elemFilter);

                assignments.Add(new FilterColorAssignment(
                    created.Id,
                    bucket.R, bucket.G, bucket.B,
                    bucket.R, bucket.G, bucket.B,
                    bucket.FillPatternId)); // <- NEW, carries the bucket's chosen pattern through to the override
            }

            ViewFilterColorManagerService.ApplyFilterOverrides(doc, templateId, assignments);

            return assignments.Count;
        }

        private static IList<FilterRule> BuildRules(ParameterColorRule rule, ColorBucket bucket)
        {
            var rules = new List<FilterRule>();

            if (rule.Method == ProcessMethod.ValueBucket)
            {
                switch (rule.StorageType)
                {
                    case StorageType.String:
                        rules.Add(ParameterFilterRuleFactory.CreateEqualsRule(rule.ParameterId, bucket.Value ?? string.Empty));
                        break;

                    case StorageType.Integer:
                        if (!int.TryParse(bucket.Value, out var intVal))
                            throw new InvalidOperationException(
                                $"Bucket value '{bucket.Value}' is not a valid integer for parameter '{rule.ParameterName}'.");
                        rules.Add(ParameterFilterRuleFactory.CreateEqualsRule(rule.ParameterId, intVal));
                        break;

                    case StorageType.Double:
                        if (!double.TryParse(bucket.Value, out var dblVal))
                            throw new InvalidOperationException(
                                $"Bucket value '{bucket.Value}' is not a valid number for parameter '{rule.ParameterName}'.");
                        rules.Add(ParameterFilterRuleFactory.CreateEqualsRule(rule.ParameterId, dblVal, 1e-6));
                        break;

                    case StorageType.ElementId:
                        if (!long.TryParse(bucket.Value, out var idVal))
                            throw new InvalidOperationException(
                                $"Bucket value '{bucket.Value}' is not a valid element id for parameter '{rule.ParameterName}'.");
                        rules.Add(ParameterFilterRuleFactory.CreateEqualsRule(rule.ParameterId, new ElementId(idVal)));
                        break;

                    default:
                        throw new NotSupportedException(
                            $"Storage type {rule.StorageType} is not supported for value bucket filters.");
                }
            }
            else
            {
                if (!bucket.RangeMin.HasValue || !bucket.RangeMax.HasValue)
                    throw new InvalidOperationException(
                        $"Bucket '{bucket.Label}' is a range bucket but is missing a min or max breakpoint.");

                switch (rule.StorageType)
                {
                    case StorageType.Double:
                        rules.Add(ParameterFilterRuleFactory.CreateGreaterOrEqualRule(rule.ParameterId, bucket.RangeMin.Value, 1e-6));
                        rules.Add(ParameterFilterRuleFactory.CreateLessRule(rule.ParameterId, bucket.RangeMax.Value, 1e-6));
                        break;

                    case StorageType.Integer:
                        rules.Add(ParameterFilterRuleFactory.CreateGreaterOrEqualRule(rule.ParameterId, (int)bucket.RangeMin.Value));
                        rules.Add(ParameterFilterRuleFactory.CreateLessRule(rule.ParameterId, (int)bucket.RangeMax.Value));
                        break;

                    default:
                        throw new NotSupportedException(
                            $"Range bucketing is only supported for Double or Integer parameters, not {rule.StorageType}.");
                }
            }

            return rules;
        }

        private static string SanitizeFilterName(string name)
        {
            var invalid = new[] { '{', '}', '[', ']', ':', '\\', '|', '?', '/', '<', '>', '*', '"' };
            foreach (var c in invalid)
                name = name.Replace(c.ToString(), "");
            return name.Trim();
        }
    }
}