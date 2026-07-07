// File: BA.Core/ViewFilters/LegendGenerationService.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.ViewFilters
{
    public static class LegendGenerationService
    {
        public static ElementId CreateLegend(Document doc, ParameterColorRule rule)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (rule == null || rule.Buckets == null || rule.Buckets.Count == 0)
                throw new InvalidOperationException("Rule has no buckets to place on a legend.");

            var existingLegend = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.ViewType == ViewType.Legend);

            if (existingLegend == null)
                throw new InvalidOperationException(
                    "No legend view exists in the project. Create at least one legend view manually first, then retry.");

            var newLegendId = existingLegend.Duplicate(ViewDuplicateOption.Duplicate);
            var newLegend = doc.GetElement(newLegendId) as View;

            // The duplicated legend inherits whatever crop region the source
            // legend had, often a small default size. Content generated here
            // has no fixed size and can run past that boundary as bucket
            // count grows. An element sitting at or beyond the crop edge
            // reports a clipped bounding box from get_BoundingBox, not its
            // true extent, which was producing an undersized swatch for
            // whichever row landed lowest, consistently the last one. Turning
            // the crop off entirely removes this constraint rather than
            // trying to guess a crop size large enough in advance. // <- NEW
            if (newLegend.CropBoxActive)
            {
                newLegend.CropBoxActive = false;
            }
            newLegend.CropBoxVisible = false; // <- NEW, cosmetic, no visible crop boundary line in the view
            doc.Regenerate(); // <- NEW, ensure the crop change is committed before any bounding box reads below

            string baseName = SanitizeName($"BA_Legend_{rule.CategoryName}_{rule.ParameterName}");
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
            var rowBottoms = new List<double>();
            double maxTextRight = 0;

            foreach (var bucket in rule.Buckets)
            {
                var origin = new XYZ(0, yPos, 0);
                string labelText = $"{rule.CategoryName} / {rule.ParameterName} - {bucket.Label}";

                var textNote = TextNote.Create(doc, newLegend.Id, origin, labelText, textNoteType.Id);
                doc.Regenerate();

                var bbox = textNote.get_BoundingBox(newLegend);
                double height = bbox.Max.Y - bbox.Min.Y;
                spacing = height * 0.25;

                maxTextRight = Math.Max(maxTextRight, bbox.Max.X);
                rowBottoms.Add(bbox.Min.Y);

                yPos = bbox.Min.Y - (height + spacing);
            }

            double swatchX = maxTextRight + spacing;

            for (int i = 0; i < rule.Buckets.Count; i++)
            {
                var bucket = rule.Buckets[i];

                double rowHeight = i < rowBottoms.Count - 1
                    ? Math.Abs(rowBottoms[i] - rowBottoms[i + 1]) - spacing
                    : spacing * 4;

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
                var color = new Color(bucket.R, bucket.G, bucket.B);
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetCutForegroundPatternColor(color);

                if (solidFillPatternId != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFillPatternId);
                    ogs.SetCutForegroundPatternId(solidFillPatternId);
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