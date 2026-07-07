// File: BA.Core/ViewFilters/ParameterEnumerationService.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.ViewFilters
{
    public static class ParameterEnumerationService
    {
        private static readonly Random _rng = new Random();

        public static CategoryInfo[] GetFilterableCategories(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var catIds = ParameterFilterUtilities.GetAllFilterableCategories();
            var list = new List<CategoryInfo>();

            foreach (var id in catIds)
            {
                var cat = Category.GetCategory(doc, id);
                if (cat == null) continue;
                list.Add(new CategoryInfo(id, cat.Name));
            }

            return list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static ParameterInfo[] GetFilterableParameters(Document doc, ElementId categoryId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (categoryId == null || categoryId == ElementId.InvalidElementId)
                return Array.Empty<ParameterInfo>();

            var category = Category.GetCategory(doc, categoryId);
            if (category == null) return Array.Empty<ParameterInfo>();

            var categoryIds = new List<ElementId> { categoryId };
            var filterableParamIds = ParameterFilterUtilities.GetFilterableParametersInCommon(doc, categoryIds);
            if (filterableParamIds == null || filterableParamIds.Count == 0)
                return Array.Empty<ParameterInfo>();

            Element sampleInstance = new FilteredElementCollector(doc)
                .OfCategoryId(categoryId)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            Element sampleType = null;
            if (sampleInstance != null)
            {
                var typeId = sampleInstance.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                    sampleType = doc.GetElement(typeId);
            }
            else
            {
                sampleType = new FilteredElementCollector(doc)
                    .OfCategoryId(categoryId)
                    .WhereElementIsElementType()
                    .FirstOrDefault();
            }

            if (sampleInstance == null && sampleType == null)
                return Array.Empty<ParameterInfo>();

            var results = new List<ParameterInfo>();

            foreach (var paramId in filterableParamIds)
            {
                bool isInstance = true;
                Parameter found = sampleInstance != null ? FindParameterById(sampleInstance, paramId) : null;

                if (found == null && sampleType != null)
                {
                    found = FindParameterById(sampleType, paramId);
                    isInstance = false;
                }

                if (found == null || found.Definition == null)
                    continue;

                results.Add(new ParameterInfo(paramId, found.Definition.Name, found.StorageType, isInstance));
            }

            return results.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static List<(string Value, int Count)> DiscoverDistinctValues(
            Document doc, ElementId categoryId, ParameterInfo parameterInfo)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (parameterInfo == null) throw new ArgumentNullException(nameof(parameterInfo));

            var elements = new FilteredElementCollector(doc)
                .OfCategoryId(categoryId)
                .WhereElementIsNotElementType()
                .ToElements();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in elements)
            {
                Element target = parameterInfo.IsInstance ? el : doc.GetElement(el.GetTypeId());
                if (target == null) continue;

                var p = FindParameterById(target, parameterInfo.Id);
                if (p == null || !p.HasValue) continue;

                var value = GetDisplayValue(p);
                if (string.IsNullOrWhiteSpace(value)) continue;

                counts[value] = counts.TryGetValue(value, out var c) ? c + 1 : 1;
            }

            return counts
                .Select(kv => (kv.Key, kv.Value))
                .OrderBy(t => t.Item1, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static (double Min, double Max) DiscoverRangeExtent(
            Document doc, ElementId categoryId, ParameterInfo parameterInfo)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (parameterInfo == null) throw new ArgumentNullException(nameof(parameterInfo));

            if (parameterInfo.StorageType != StorageType.Double && parameterInfo.StorageType != StorageType.Integer)
                throw new NotSupportedException(
                    $"Range extent discovery only supports Double or Integer parameters, not {parameterInfo.StorageType}.");

            var elements = new FilteredElementCollector(doc)
                .OfCategoryId(categoryId)
                .WhereElementIsNotElementType()
                .ToElements();

            double min = double.MaxValue;
            double max = double.MinValue;
            bool found = false;

            foreach (var el in elements)
            {
                Element target = parameterInfo.IsInstance ? el : doc.GetElement(el.GetTypeId());
                if (target == null) continue;

                var p = FindParameterById(target, parameterInfo.Id);
                if (p == null || !p.HasValue) continue;

                double value = parameterInfo.StorageType == StorageType.Double ? p.AsDouble() : p.AsInteger();

                if (value < min) min = value;
                if (value > max) max = value;
                found = true;
            }

            if (!found)
                throw new InvalidOperationException(
                    $"No elements of this category have a value set for '{parameterInfo.Name}'.");

            return (min, max);
        }

        public static List<ColorBucket> BuildValueBuckets(IReadOnlyList<(string Value, int Count)> distinctValues)
        {
            if (distinctValues == null || distinctValues.Count == 0)
                return new List<ColorBucket>();

            var colors = AssignDistinctColors(distinctValues.Count);
            var buckets = new List<ColorBucket>();

            for (int i = 0; i < distinctValues.Count; i++)
            {
                buckets.Add(new ColorBucket
                {
                    Label = distinctValues[i].Value,
                    Value = distinctValues[i].Value,
                    R = colors[i].r,
                    G = colors[i].g,
                    B = colors[i].b
                });
            }

            return buckets;
        }

        public static List<ColorBucket> BuildDefaultRangeBuckets(double min, double max, int bucketCount = 5)
        {
            if (bucketCount < 1)
                throw new ArgumentOutOfRangeException(nameof(bucketCount), "Bucket count must be at least 1.");

            if (max <= min)
                bucketCount = 1;

            var colors = AssignDistinctColors(bucketCount);
            var buckets = new List<ColorBucket>();
            double span = max - min;

            for (int i = 0; i < bucketCount; i++)
            {
                double lo = min + span * i / bucketCount;
                double hi = i == bucketCount - 1 ? max : min + span * (i + 1) / bucketCount;

                buckets.Add(new ColorBucket
                {
                    Label = $"{lo:0.##} to {hi:0.##}",
                    RangeMin = lo,
                    RangeMax = hi,
                    R = colors[i].r,
                    G = colors[i].g,
                    B = colors[i].b
                });
            }

            return buckets;
        }

        public static List<(byte r, byte g, byte b)> AssignDistinctColors(int count)
        {
            var result = new List<(byte r, byte g, byte b)>();
            int attempts = 0;
            int maxAttempts = Math.Max(count * 200, 500);

            while (result.Count < count && attempts < maxAttempts)
            {
                attempts++;
                var candidate = ((byte)_rng.Next(0, 230), (byte)_rng.Next(0, 230), (byte)_rng.Next(0, 230));

                bool tooClose = result.Any(c => ColorDistance(c, candidate) < 60);
                if (!tooClose)
                    result.Add(candidate);
            }

            while (result.Count < count)
                result.Add(((byte)_rng.Next(0, 230), (byte)_rng.Next(0, 230), (byte)_rng.Next(0, 230)));

            return result;
        }

        // Resolves the parameter on the given element, using the same
        // instance versus type logic as enumeration and discovery, then
        // finds which bucket in the rule matches its actual value. Used by
        // ApplySelectionOverrides to color a selection directly rather than
        // through a ParameterFilterElement. // <- NEW
        public static bool TryMatchBucket(Document doc, Element element, ParameterColorRule rule, out ColorBucket matchedBucket)
        {
            matchedBucket = null;
            if (doc == null || element == null || rule == null) return false;

            Element target = element;
            if (!rule.IsInstance)
            {
                var typeId = element.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return false;
                target = doc.GetElement(typeId);
                if (target == null) return false;
            }

            var p = FindParameterById(target, rule.ParameterId);
            if (p == null || !p.HasValue) return false;

            if (rule.Method == ProcessMethod.ValueBucket)
            {
                var val = GetDisplayValue(p);
                if (string.IsNullOrWhiteSpace(val)) return false;

                matchedBucket = rule.Buckets.FirstOrDefault(b =>
                    string.Equals(b.Value, val, StringComparison.OrdinalIgnoreCase));
                return matchedBucket != null;
            }
            else
            {
                if (rule.StorageType != StorageType.Double && rule.StorageType != StorageType.Integer)
                    return false;

                double numeric = rule.StorageType == StorageType.Double ? p.AsDouble() : p.AsInteger();

                // Inclusive on both ends. A value sitting exactly on a shared
                // boundary between two adjacent buckets matches whichever
                // bucket appears first in the list, this is a real ambiguity
                // in manually entered breakpoints, not something silently
                // resolved here.
                matchedBucket = rule.Buckets.FirstOrDefault(b =>
                    b.RangeMin.HasValue && b.RangeMax.HasValue &&
                    numeric >= b.RangeMin.Value && numeric <= b.RangeMax.Value);

                return matchedBucket != null;
            }
        }

        private static string GetDisplayValue(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            if (p.StorageType == StorageType.String) return p.AsString();
            return p.AsValueString();
        }

        private static double ColorDistance((byte r, byte g, byte b) a, (byte r, byte g, byte b) b)
        {
            double dr = a.r - b.r;
            double dg = a.g - b.g;
            double db = a.b - b.b;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static Parameter FindParameterById(Element element, ElementId paramId)
        {
            foreach (Parameter p in element.Parameters)
            {
                if (p.Id == paramId) return p;
            }
            return null;
        }
    }
}