// File: BA.Core/ViewFilters/ColorRuleModels.cs
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace BA.Core.ViewFilters
{
    public enum ProcessMethod
    {
        ValueBucket,
        RangeBucket
    }

    public sealed record CategoryInfo(ElementId Id, string Name);

    public sealed record ParameterInfo(ElementId Id, string Name, StorageType StorageType, bool IsInstance);

    // New. Represents a fill pattern selectable per bucket. // <- NEW
    public sealed record FillPatternInfo(ElementId Id, string Name);

    public sealed class ColorBucket
    {
        public string Label { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public double? RangeMin { get; set; }
        public double? RangeMax { get; set; }

        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        // Null or ElementId.InvalidElementId means solid fill, the existing
        // default behavior. Any other id is resolved against the document's
        // real FillPatternElement collection at apply time. // <- NEW
        public ElementId FillPatternId { get; set; }
    }

    public sealed class ParameterColorRule
    {
        public ElementId CategoryId { get; set; } = ElementId.InvalidElementId;
        public string CategoryName { get; set; } = string.Empty;

        public ElementId ParameterId { get; set; } = ElementId.InvalidElementId;
        public string ParameterName { get; set; } = string.Empty;
        public StorageType StorageType { get; set; }
        public bool IsInstance { get; set; }

        public ProcessMethod Method { get; set; }

        public List<ColorBucket> Buckets { get; set; } = new();
    }
}