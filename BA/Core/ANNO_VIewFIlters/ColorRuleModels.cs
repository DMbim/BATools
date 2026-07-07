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

    public sealed class ColorBucket
    {
        // Display label shown in the UI and used in filter/legend naming.
        public string Label { get; set; } = string.Empty;

        // Used for ValueBucket only. Raw string form of the discrete value,
        // parsed against the parameter's actual StorageType when the filter
        // rule is built, not assumed to be a string parameter.
        public string Value { get; set; } = string.Empty;

        // Used for RangeBucket only. Both must be set for a range bucket,
        // these are the manual breakpoints the user entered, never computed.
        public double? RangeMin { get; set; }
        public double? RangeMax { get; set; }

        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
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