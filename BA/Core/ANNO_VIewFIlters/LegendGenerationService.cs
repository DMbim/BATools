// File: BA.Core/ViewFilters/LegendGenerationService.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.ViewFilters
{
    // Generic row description for a legend, decoupled from ParameterColorRule.
    // CreateLegend(rule) below just maps buckets into these and forwards to
    // CreateLegendFromEntries, which is the one place that actually builds
    // Revit geometry. The View Template tab's "Create Legend From Selected"
    // workflow builds its own entry list from a mix of native and BA managed
    // filters and calls CreateLegendFromEntries directly, it never goes
    // through a ParameterColorRule at all. // <- NEW
    public sealed record LegendEntry(string Label, byte R, byte G, byte B, ElementId PatternId);

    public static class LegendGenerationService
    {
        public static ElementId CreateLegend(Document doc, ParameterColorRule rule)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (rule == null || rule.Buckets == null || rule.Buckets.Count == 0)
                throw new InvalidOperationException("Rule has no buckets to place on a legend.");

            string title = $"{rule.CategoryName} / {rule.ParameterName}";

            var entries = rule.Buckets
                .Select(b => new LegendEntry(b.Label, b.R, b.G, b.B, b.FillPatternId))
                .ToList();

            return CreateLegendFromEntries(doc, title, entries);
        }

        // New. Callable with any label/color/pattern list, not just buckets
        // from a ParameterColorRule. This is what the View Template tab's
        // "Create Legend From Selected" button drives, feeding it entries
        // built from a mix of native Revit filters and BA managed filters,
        // each one's color read back off the template's own filter
        // overrides rather than off a stored rule. // <- NEW
        public static ElementId CreateLegendFromEntries(Document doc, string title, IReadOnlyList<LegendEntry> entries)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentNullException(nameof(title));
            if (entries == null || entries.Count == 0)
                throw new InvalidOperationException("No entries were supplied to place on a legend.");

            var existingLegend = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.ViewType == ViewType.Legend)
                .FirstOrDefault(v => !v.Name.StartsWith("BA_Legend", StringComparison.OrdinalIgnoreCase));

            if (existingLegend == null)
                throw new InvalidOperationException(
                    "No usable template legend view was found. Every legend view in the project appears to already be a BA generated legend. Create a fresh, untouched legend view manually first, then retry.");

            var newLegendId = existingLegend.Duplicate(ViewDuplicateOption.Duplicate);
            var newLegend = doc.GetElement(newLegendId) as View;

            if (newLegend.CropBoxActive)
            {
                newLegend.CropBoxActive = false;
            }
            newLegend.CropBoxVisible = false;
            doc.Regenerate();

            string baseName = SanitizeName($"BA_Legend - {title}");
            newLegend.Name = MakeUniqueViewName(doc, baseName);

            var textNoteType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .FirstOrDefault();

            if (textNoteType == null)
                throw new InvalidOperationException("No TextNoteType found in the document, cannot place labels on the legend.");

            var filledRegionTypeId = GetOrCreateSolidFilledRegionType(doc);

            var solidFillPatternId = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id;

            double yPos = 0;
            double spacing = 0.1;

            var titleOrigin = new XYZ(0, yPos, 0);
            var titleNote = TextNote.Create(doc, newLegend.Id, titleOrigin, title, textNoteType.Id);
            doc.Regenerate();

            var titleBbox = titleNote.get_BoundingBox(newLegend);
            double titleHeight = titleBbox.Max.Y - titleBbox.Min.Y;
            double titleSpacing = titleHeight * 0.75;

            yPos = titleBbox.Min.Y - (titleHeight + titleSpacing);

            var rowBottoms = new List<double>();
            var rowHeights = new List<double>();
            double maxTextRight = 0;

            foreach (var entry in entries)
            {
                var origin = new XYZ(0, yPos, 0);

                var textNote = TextNote.Create(doc, newLegend.Id, origin, entry.Label, textNoteType.Id);
                doc.Regenerate();

                var bbox = textNote.get_BoundingBox(newLegend);
                double height = bbox.Max.Y - bbox.Min.Y;
                spacing = height * 0.25;

                maxTextRight = Math.Max(maxTextRight, bbox.Max.X);
                rowBottoms.Add(bbox.Min.Y);
                rowHeights.Add(height);

                yPos = bbox.Min.Y - (height + spacing);
            }

            double swatchX = maxTextRight + spacing;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                double rowHeight = rowHeights[i];
                if (rowHeight <= 0) rowHeight = spacing * 2;

                double rowWidth = rowHeight * 2;
                double y = rowBottoms[i];

                var p0 = new XYZ(swatchX, y, 0);
                var p1 = new XYZ(swatchX, y + rowHeight, 0);
                var p2 = new XYZ(swatchX + rowWidth, y + rowHeight, 0);
                var p3 = new XYZ(swatchX + rowWidth, y, 0);

                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p0));

                var region = FilledRegion.Create(doc, filledRegionTypeId, newLegend.Id, new List<CurveLoop> { loop });

                var ogs = new OverrideGraphicSettings();
                var color = new Color(entry.R, entry.G, entry.B);
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetCutForegroundPatternColor(color);

                var patternId = (entry.PatternId != null && entry.PatternId != ElementId.InvalidElementId)
                    ? entry.PatternId
                    : solidFillPatternId;

                if (patternId != null)
                {
                    ogs.SetSurfaceForegroundPatternId(patternId);
                    ogs.SetCutForegroundPatternId(patternId);
                }

                newLegend.SetElementOverrides(region.Id, ogs);
            }

            return newLegend.Id;
        }

        private static ElementId GetOrCreateSolidFilledRegionType(Document doc)
        {
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .ToList();

            foreach (var t in types)
            {
                var pattern = doc.GetElement(t.ForegroundPatternId) as FillPatternElement;
                if (pattern != null && pattern.GetFillPattern().IsSolidFill && t.ForegroundPatternColor.IsValid)
                    return t.Id;
            }

            if (types.Count == 0)
                throw new InvalidOperationException("No FilledRegionType exists in the document to base a new solid fill type on.");

            var baseType = types[0];
            string newTypeName = MakeUniqueFilledRegionTypeName(doc, "BA_SolidFill");
            var newType = baseType.Duplicate(newTypeName) as FilledRegionType;

            var solidPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

            if (solidPattern == null)
                throw new InvalidOperationException("No solid fill pattern exists in the document.");

            newType.ForegroundPatternId = solidPattern.Id;
            return newType.Id;
        }

        private static string MakeUniqueFilledRegionTypeName(Document doc, string baseName)
        {
            var existingNames = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .Select(t => t.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(baseName)) return baseName;

            for (int i = 1; i < 1000; i++)
            {
                var candidate = $"{baseName}_{i}";
                if (!existingNames.Contains(candidate)) return candidate;
            }

            throw new InvalidOperationException("Could not generate a unique filled region type name.");
        }

        private static string MakeUniqueViewName(Document doc, string baseName)
        {
            var existingNames = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(baseName)) return baseName;

            for (int i = 1; i < 1000; i++)
            {
                var candidate = $"{baseName}_{i}";
                if (!existingNames.Contains(candidate)) return candidate;
            }

            throw new InvalidOperationException("Could not generate a unique legend view name.");
        }

        private static string SanitizeName(string name)
        {
            var invalid = new[] { '{', '}', '[', ']', ':', '\\', '|', '?', '/', '<', '>', '*', '"' };
            foreach (var c in invalid)
                name = name.Replace(c.ToString(), "");
            return name.Trim();
        }
    }
}