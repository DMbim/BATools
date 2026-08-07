// File: BA.Core/ViewFilters/ColorSchemeModels.cs
using System;
using System.Collections.Generic;

namespace BA.Core.ViewFilters
{
    // Promoted out of BAViewFilterColorManagerVm's private nested SchemeDto/BucketDto.
    // A BA.Core service cannot depend on a private type nested in a BA.UI view model,
    // and BA.Core must not depend on BA.UI at all. SchemeName is new, the old nested
    // version had no name field since saving went through a raw SaveFileDialog. // <- NEW
    public sealed class SchemeDto
    {
        public string SchemeName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string ParameterName { get; set; } = string.Empty;
        public string StorageType { get; set; } = string.Empty;
        public bool IsInstance { get; set; }
        public string Method { get; set; } = string.Empty;
        public List<BucketDto> Buckets { get; set; } = new();
    }

    public sealed class BucketDto
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public double? RangeMin { get; set; }
        public double? RangeMax { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public string PatternName { get; set; } = string.Empty;
    }

    // Lightweight row for the "Load Scheme" picker, avoids deserializing every
    // bucket in every scheme file just to populate a combo box. // <- NEW
    public sealed record SchemeSummary(string SchemeName, string CategoryName, string ParameterName, string FileName);
}