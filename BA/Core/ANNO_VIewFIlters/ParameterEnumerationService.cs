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

        // Changed. No longer caps the sample scan. Some parameters, Wall's
        // "Unconnected Height" being the case that surfaced this, are conditionally
        // absent from Element.Parameters entirely depending on another parameter's
        // value (Top Constraint must be "Unconnected" for Unconnected Height to
        // even appear as a Parameter object, not just be read-only). If most
        // instances in the model have their top constrained to a level, which is
        // typical, a bounded sample can miss every instance where the parameter
        // is actually present, even if such instances exist. Scanning the full
        // category guarantees correctness. This runs once per category/parameter
        // selection, not per element per frame, so the cost is acceptable even
        // though it is no longer bounded. // <- CHANGED
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

            var instances = new FilteredElementCollector(doc)
                .OfCategoryId(categoryId)
                .WhereElementIsNotElementType()
                .ToList();

            var types = new FilteredElementCollector(doc)
                .OfCategoryId(categoryId)
                .WhereElementIsElementType()
                .ToList();

            if (instances.Count == 0 && types.Count == 0)
                return Array.Empty<ParameterInfo>();

            var results = new List<ParameterInfo>();

            foreach (var paramId in filterableParamIds)
            {
                Parameter found = null;
                bool isInstance = true;

                foreach (var inst in instances)
                {
                    found = FindParameterById(inst, paramId);
                    if (found != null) break;
                }

                if (found == null)
                {
                    isInstance = false;
                    foreach (var typeElem in types)
                    {
                        found = FindParameterById(typeElem, paramId);
                        if (found != null) break;
                    }
                }

                if (found == null || found.Definition == null)
                    continue;

                results.Add(new ParameterInfo(paramId, found.Definition.Name, found.StorageType, isInstance));
            }

            return results.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        // Super Selector's own category and parameter enumeration.
        // Deliberately does not use ParameterFilterUtilities, which scopes
        // itself to categories/parameters usable in a ParameterFilterElement
        // - a constraint Super Selector has no reason to inherit since it
        // never creates one, it picks elements directly via ISelectionFilter.
        // // <- NEW
        public static CategoryInfo[] GetAllSelectableCategories(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var list = new List<CategoryInfo>();

            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat == null) continue;
                if (cat.CategoryType != CategoryType.Model && cat.CategoryType != CategoryType.Annotation)
                    continue;

                // Deliberately does not filter on AllowsBoundParameters.
                // That property means "can a shared/project parameter be
                // bound here," not "is this a legitimate pick target" - Tag
                // categories are false for it (they display values from the
                // tagged element, not their own bound parameters) but are
                // completely valid pick targets.
                list.Add(new CategoryInfo(cat.Id, cat.Name));
            }

            return list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        // New. // <- NEW
        public static ParameterInfo[] GetCommonParametersForCategories(Document doc, ICollection<ElementId> categoryIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (categoryIds == null || categoryIds.Count == 0)
                return Array.Empty<ParameterInfo>();

            Dictionary<ElementId, ParameterInfo> intersection = null;

            foreach (var catId in categoryIds)
            {
                var perCategory = new Dictionary<ElementId, ParameterInfo>();

                var instances = new FilteredElementCollector(doc)
                    .OfCategoryId(catId)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var el in instances)
                {
                    foreach (Parameter p in el.Parameters)
                    {
                        if (p?.Definition == null) continue;
                        var defId = (p.Definition as InternalDefinition)?.Id;
                        if (defId == null || perCategory.ContainsKey(defId)) continue;
                        perCategory[defId] = new ParameterInfo(defId, p.Definition.Name, p.StorageType, true);
                    }

                    var typeId = el.GetTypeId();
                    if (typeId == null || typeId == ElementId.InvalidElementId) continue;
                    var type = doc.GetElement(typeId);
                    if (type == null) continue;

                    foreach (Parameter p in type.Parameters)
                    {
                        if (p?.Definition == null) continue;
                        var defId = (p.Definition as InternalDefinition)?.Id;
                        if (defId == null || perCategory.ContainsKey(defId)) continue;
                        perCategory[defId] = new ParameterInfo(defId, p.Definition.Name, p.StorageType, false);
                    }
                }

                if (perCategory.Count == 0)
                {
                    // No instances of this category exist yet in the model.
                    // Skipped rather than collapsing the whole intersection
                    // to empty, so checking one empty category alongside
                    // populated ones doesn't wipe out the result.
                    continue;
                }

                if (intersection == null)
                {
                    intersection = perCategory;
                }
                else
                {
                    foreach (var key in intersection.Keys.Where(k => !perCategory.ContainsKey(k)).ToList())
                        intersection.Remove(key);
                }
            }

            if (intersection == null || intersection.Count == 0)
                return Array.Empty<ParameterInfo>();

            return intersection.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static List<(string Value, int Count)> DiscoverDistinctValues(
            Document doc, ElementId categoryId, ParameterInfo parameterInfo, ElementId viewId = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (parameterInfo == null) throw new ArgumentNullException(nameof(parameterInfo));

            var collector = viewId != null
                ? new FilteredElementCollector(doc, viewId)
                : new FilteredElementCollector(doc);

            var elements = collector
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

        // Changed. Same viewId scoping as DiscoverDistinctValues, same reasoning. // <- CHANGED
        public static (double Min, double Max) DiscoverRangeExtent(
            Document doc, ElementId categoryId, ParameterInfo parameterInfo, ElementId viewId = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (parameterInfo == null) throw new ArgumentNullException(nameof(parameterInfo));

            if (parameterInfo.StorageType != StorageType.Double && parameterInfo.StorageType != StorageType.Integer)
                throw new NotSupportedException(
                    $"Range extent discovery only supports Double or Integer parameters, not {parameterInfo.StorageType}.");

            var collector = viewId != null
                ? new FilteredElementCollector(doc, viewId)
                : new FilteredElementCollector(doc);

            var elements = collector
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
                    $"No elements of this category have a value set for '{parameterInfo.Name}'" +
                    (viewId != null ? " in the active view." : "."));

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

                matchedBucket = rule.Buckets.FirstOrDefault(b =>
                    b.RangeMin.HasValue && b.RangeMax.HasValue &&
                    numeric >= b.RangeMin.Value && numeric <= b.RangeMax.Value);

                return matchedBucket != null;
            }
        }

        public static List<FillPatternInfo> GetAvailableFillPatterns(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .Select(f => new FillPatternInfo(f.Id, f.Name))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
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