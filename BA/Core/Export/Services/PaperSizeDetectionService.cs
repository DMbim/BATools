using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Detects paper size and orientation per sheet from the placed title
    /// block's Sheet Width / Sheet Height built-in parameters. Must be
    /// called from a valid Revit API thread context, never directly from
    /// WPF UI code.
    ///
    /// Title blocks are collected document wide and grouped by
    /// OwnerViewId, not queried per sheet with a view scoped collector.
    /// A view scoped collector silently excludes hidden elements, and a
    /// title block can be hidden in its own sheet view, same trap already
    /// confirmed for the Change Monitor work. This also means one collector
    /// pass total regardless of sheet count, not one pass per sheet.
    ///
    /// The "+" oversized architectural sizes (A0+, A1+, A2+) are not
    /// formally standardized, the values below are common office defaults,
    /// adjust DefaultSizes if your title block dimensions differ.
    /// </summary>
    public static class PaperSizeDetectionService
    {
        private const double DefaultToleranceMm = 3.0;
        private const double OrientationEqualToleranceMm = 2.0;

        public static readonly List<PaperSizeDefinition> DefaultSizes = new List<PaperSizeDefinition>
        {
            new PaperSizeDefinition { Name = "A0", WidthMm = 841, HeightMm = 1189 },
            new PaperSizeDefinition { Name = "A1", WidthMm = 594, HeightMm = 841 },
            new PaperSizeDefinition { Name = "A2", WidthMm = 420, HeightMm = 594 },
            new PaperSizeDefinition { Name = "A3", WidthMm = 297, HeightMm = 420 },
            new PaperSizeDefinition { Name = "A4", WidthMm = 210, HeightMm = 297 },
            new PaperSizeDefinition { Name = "A0+", WidthMm = 914, HeightMm = 1220 },
            new PaperSizeDefinition { Name = "A1+", WidthMm = 609, HeightMm = 914 },
            new PaperSizeDefinition { Name = "A2+", WidthMm = 420, HeightMm = 610 }
        };

        public static Dictionary<string, PaperSizeInfo> DetectForSheets(
            Document doc,
            IList<string> sheetNumbers,
            IList<PaperSizeDefinition> customSizeTable = null)
        {
            var result = new Dictionary<string, PaperSizeInfo>(StringComparer.OrdinalIgnoreCase);

            var sheets = ResolveSheets(doc, sheetNumbers);

            if (sheets.Count == 0)
            {
                return result;
            }

            var sizeTable = customSizeTable != null && customSizeTable.Count > 0 ? customSizeTable : DefaultSizes;
            var titleBlocksByOwner = BuildTitleBlockLookup(doc);

            foreach (var sheet in sheets)
            {
                result[sheet.SheetNumber ?? string.Empty] = DetectOneSheet(sheet, titleBlocksByOwner, sizeTable);
            }

            return result;
        }

        private static PaperSizeInfo DetectOneSheet(
            ViewSheet sheet,
            Dictionary<ElementId, List<FamilyInstance>> titleBlocksByOwner,
            IList<PaperSizeDefinition> sizeTable)
        {
            if (!titleBlocksByOwner.TryGetValue(sheet.Id, out var titleBlocks) || titleBlocks.Count == 0)
            {
                return new PaperSizeInfo { HasNoTitleBlock = true };
            }

            var candidates = new List<(double WidthMm, double HeightMm)>();

            foreach (var titleBlock in titleBlocks)
            {
                var widthParam = titleBlock.get_Parameter(BuiltInParameter.SHEET_WIDTH);
                var heightParam = titleBlock.get_Parameter(BuiltInParameter.SHEET_HEIGHT);

                if (widthParam == null || heightParam == null || !widthParam.HasValue || !heightParam.HasValue)
                {
                    continue;
                }

                var widthMmM = UnitUtils.ConvertFromInternalUnits(widthParam.AsDouble(), UnitTypeId.Millimeters);
                var heightMmM = UnitUtils.ConvertFromInternalUnits(heightParam.AsDouble(), UnitTypeId.Millimeters);

                candidates.Add((widthMmM, heightMmM));
            }

            if (candidates.Count == 0)
            {
                // Title block placed, but this family does not populate
                // Sheet Width / Sheet Height, functionally the same as no
                // title block for detection purposes.
                return new PaperSizeInfo { HasNoTitleBlock = true };
            }

            var distinctRounded = candidates
                .Select(c => (Width: Math.Round(c.WidthMm), Height: Math.Round(c.HeightMm)))
                .Distinct()
                .ToList();

            if (distinctRounded.Count > 1)
            {
                // Multiple title blocks disagree, a value is not silently
                // picked here, this needs a manual resolution, not a guess.
                return new PaperSizeInfo
                {
                    IsAmbiguous = true,
                    WidthMm = candidates[0].WidthMm,
                    HeightMm = candidates[0].HeightMm
                };
            }

            var (widthMm, heightMm) = candidates[0];

            return new PaperSizeInfo
            {
                ResolvedSizeName = MatchSize(widthMm, heightMm, sizeTable),
                WidthMm = widthMm,
                HeightMm = heightMm,
                Orientation = DetermineOrientation(widthMm, heightMm)
            };
        }

        private static string MatchSize(double widthMm, double heightMm, IList<PaperSizeDefinition> sizeTable)
        {
            foreach (var definition in sizeTable)
            {
                var tolerance = definition.MatchingToleranceMm > 0 ? definition.MatchingToleranceMm : DefaultToleranceMm;

                var straightMatch = Math.Abs(widthMm - definition.WidthMm) <= tolerance &&
                                     Math.Abs(heightMm - definition.HeightMm) <= tolerance;

                var rotatedMatch = Math.Abs(widthMm - definition.HeightMm) <= tolerance &&
                                    Math.Abs(heightMm - definition.WidthMm) <= tolerance;

                if (straightMatch || rotatedMatch)
                {
                    return definition.Name;
                }
            }

            return null;
        }

        private static PaperOrientation DetermineOrientation(double widthMm, double heightMm)
        {
            if (Math.Abs(widthMm - heightMm) <= OrientationEqualToleranceMm)
            {
                return PaperOrientation.Unspecified;
            }

            return widthMm > heightMm ? PaperOrientation.Landscape : PaperOrientation.Portrait;
        }

        private static Dictionary<ElementId, List<FamilyInstance>> BuildTitleBlockLookup(Document doc)
        {
            var lookup = new Dictionary<ElementId, List<FamilyInstance>>();

            var titleBlocks = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>();

            foreach (var titleBlock in titleBlocks)
            {
                var ownerId = titleBlock.OwnerViewId;

                if (ownerId == null || ownerId == ElementId.InvalidElementId)
                {
                    continue;
                }

                if (!lookup.TryGetValue(ownerId, out var list))
                {
                    list = new List<FamilyInstance>();
                    lookup[ownerId] = list;
                }

                list.Add(titleBlock);
            }

            return lookup;
        }

        private static List<ViewSheet> ResolveSheets(Document doc, IList<string> sheetNumbers)
        {
            if (sheetNumbers == null || sheetNumbers.Count == 0)
            {
                return new List<ViewSheet>();
            }

            var wanted = new HashSet<string>(sheetNumbers, StringComparer.OrdinalIgnoreCase);

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .OfType<ViewSheet>()
                .Where(s => wanted.Contains(s.SheetNumber ?? string.Empty))
                .ToList();
        }
    }
}
