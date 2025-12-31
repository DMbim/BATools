// File: BA.Core/Standards/ViewTemplateStandardFile.cs
using System;
using System.Collections.Generic;

namespace BA.Core.Standards
{
    public sealed class ViewTemplateStandardFile
    {
        public string TemplateName { get; set; } = "";
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
        public ViewTemplateSnapshot Snapshot { get; set; } = new();
    }

    public sealed class ViewTemplateSnapshot
    {
        // View template “Include” controls: Non-controlled template parameter IDs
        // NOTE: These can be negative (built-in) -> store long and reconstruct ElementId(int).
        public List<long> NonControlledTemplateParamIds { get; set; } = new();

        // All view parameters
        public List<ViewParamSnapshot> Parameters { get; set; } = new();

        // Categories: key = CategoryId (can be negative!)
        public Dictionary<long, CategoryOverrideSnapshot> Categories { get; set; } = new();

        // Filter order by filter NAME (ElementId differs across files)
        public List<string> FilterOrder { get; set; } = new();

        // Filters: key = FilterName
        public Dictionary<string, FilterOverrideSnapshot> Filters { get; set; } = new();

        // Worksets: key = WorksetId.IntegerValue stored as long
        public Dictionary<long, int> WorksetVisibility { get; set; } = new();
    }

    public sealed class CategoryOverrideSnapshot
    {
        public long CategoryId { get; set; } // can be negative
        public string CategoryName { get; set; } = "";
        public bool IsHidden { get; set; }
        public GraphicOverrideSnapshot Overrides { get; set; } = new();
    }

    public sealed class FilterOverrideSnapshot
    {
        public string FilterName { get; set; } = "";
        public bool IsVisible { get; set; }
        public GraphicOverrideSnapshot Overrides { get; set; } = new();
    }

    public sealed class ViewParamSnapshot
    {
        public long ParamId { get; set; } // can be negative (built-in params)
        public string Name { get; set; } = "";
        public int StorageType { get; set; }
        public string DisplayValue { get; set; } = "";

        public string? StringValue { get; set; }
        public int? IntValue { get; set; }
        public double? DoubleValue { get; set; }
        public long? ElementIdValue { get; set; }
    }

    public readonly struct RgbColor
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public RgbColor(byte r, byte g, byte b)
        {
            R = r; G = g; B = b;
        }

        public override string ToString() => $"{R},{G},{B}";
    }

    public sealed class GraphicOverrideSnapshot
    {
        public RgbColor? ProjectionLineColor { get; set; }
        public int? ProjectionLineWeight { get; set; }
        public long? ProjectionLinePatternId { get; set; }

        public RgbColor? CutLineColor { get; set; }
        public int? CutLineWeight { get; set; }
        public long? CutLinePatternId { get; set; }

        public long? SurfaceForegroundPatternId { get; set; }
        public RgbColor? SurfaceForegroundPatternColor { get; set; }
        public long? SurfaceBackgroundPatternId { get; set; }
        public RgbColor? SurfaceBackgroundPatternColor { get; set; }

        public long? CutForegroundPatternId { get; set; }
        public RgbColor? CutForegroundPatternColor { get; set; }
        public long? CutBackgroundPatternId { get; set; }
        public RgbColor? CutBackgroundPatternColor { get; set; }

        public int? Transparency { get; set; }
        public bool? Halftone { get; set; }
    }
}
