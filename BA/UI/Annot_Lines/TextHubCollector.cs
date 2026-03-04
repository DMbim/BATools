using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.TextHub
{
    public static class TextHubCollector
    {
        public static List<TextStyleRow> Collect(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var rows = new List<TextStyleRow>();

            rows.AddRange(CollectTextNoteTypes(doc));
            rows.AddRange(CollectDimensionTypes(doc));
            rows.AddRange(CollectSpotDimensionTypes(doc));
            rows.AddRange(CollectTagSymbols(doc));

            return rows;
        }

        private static IEnumerable<TextStyleRow> CollectTextNoteTypes(Document doc)
        {
            var list = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();

            foreach (var t in list)
            {
                var size = ParamUtil.TryGetTextSizeMm(t, out var sizeMm) ? sizeMm : (double?)null;
                var font = ParamUtil.TryGetTextFont(t, out var f) ? f : "N/A";

                yield return new TextStyleRow(
                    kind: "TextNoteType",
                    familyName: "",
                    typeName: t.Name,
                    typeId: t.Id,
                    textSizeMm: size,
                    textFont: font,
                    hasTextSize: ParamUtil.HasWritableTextSize(t),
                    hasTextFont: ParamUtil.HasWritableTextFont(t),
                    notes: "");
            }
        }

        private static IEnumerable<TextStyleRow> CollectDimensionTypes(Document doc)
        {
            var list = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .ToList();

            foreach (var t in list)
            {
                var size = ParamUtil.TryGetTextSizeMm(t, out var sizeMm) ? sizeMm : (double?)null;
                var font = ParamUtil.TryGetTextFont(t, out var f) ? f : "N/A";

                yield return new TextStyleRow(
                    kind: "DimensionType",
                    familyName: "",
                    typeName: t.Name,
                    typeId: t.Id,
                    textSizeMm: size,
                    textFont: font,
                    hasTextSize: ParamUtil.HasWritableTextSize(t),
                    hasTextFont: ParamUtil.HasWritableTextFont(t),
                    notes: "");
            }
        }

        private static IEnumerable<TextStyleRow> CollectSpotDimensionTypes(Document doc)
        {
            var list = new FilteredElementCollector(doc)
                .OfClass(typeof(SpotDimensionType))
                .Cast<SpotDimensionType>()
                .ToList();

            foreach (var t in list)
            {
                var size = ParamUtil.TryGetTextSizeMm(t, out var sizeMm) ? sizeMm : (double?)null;
                var font = ParamUtil.TryGetTextFont(t, out var f) ? f : "N/A";

                yield return new TextStyleRow(
                    kind: "SpotDimensionType",
                    familyName: "",
                    typeName: t.Name,
                    typeId: t.Id,
                    textSizeMm: size,
                    textFont: font,
                    hasTextSize: ParamUtil.HasWritableTextSize(t),
                    hasTextFont: ParamUtil.HasWritableTextFont(t),
                    notes: "");
            }
        }

        private static IEnumerable<TextStyleRow> CollectTagSymbols(Document doc)
        {
            // We want tag types loaded in the project. These are FamilySymbols in annotation tag categories.
            var allSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => s.Category != null && s.Category.CategoryType == CategoryType.Annotation)
                .ToList();

            // Heuristic: prefer categories that are "tag" categories.
            // Many built-in tag categories end with "Tags" in UI, but localized names exist.
            // So we also try BuiltInCategory checks via Category.Id where possible.
            foreach (var s in allSymbols)
            {
                if (!IsLikelyTagCategory(doc, s.Category))
                    continue;

                var famName = s.FamilyName ?? s.Family?.Name ?? "";

                bool hasSize = ParamUtil.HasWritableTextSize(s);
                bool hasFont = ParamUtil.HasWritableTextFont(s);

                double? size = ParamUtil.TryGetTextSizeMm(s, out var sizeMm) ? sizeMm : (double?)null;
                string font = ParamUtil.TryGetTextFont(s, out var f) ? f : "N/A";

                var note = "";
                if (!hasSize && !hasFont)
                    note = "Tag label text is often controlled inside the family (not editable here).";

                yield return new TextStyleRow(
                    kind: "TagType (FamilySymbol)",
                    familyName: famName,
                    typeName: s.Name,
                    typeId: s.Id,
                    textSizeMm: size,
                    textFont: font,
                    hasTextSize: hasSize,
                    hasTextFont: hasFont,
                    notes: note);
            }
        }

        private static bool IsLikelyTagCategory(Document doc, Category cat)
        {
            if (cat == null) return false;

            // Strong signal: BuiltInCategory name match for common tag categories
            // (works even if UI is localized).
            var bic = (BuiltInCategory)cat.Id.Value;

            // Common tag categories list; extend any time.
            if (bic == BuiltInCategory.OST_DoorTags ||
                bic == BuiltInCategory.OST_WindowTags ||
                bic == BuiltInCategory.OST_WallTags ||
                bic == BuiltInCategory.OST_RoomTags ||
                bic == BuiltInCategory.OST_AreaTags ||
                bic == BuiltInCategory.OST_FloorTags ||
                bic == BuiltInCategory.OST_CeilingTags ||
                bic == BuiltInCategory.OST_RoofTags ||
                bic == BuiltInCategory.OST_GenericModelTags ||
                bic == BuiltInCategory.OST_MaterialTags ||
                bic == BuiltInCategory.OST_PipeTags ||
                bic == BuiltInCategory.OST_DuctTags ||
                bic == BuiltInCategory.OST_CableTrayTags ||
                bic == BuiltInCategory.OST_ConduitTags ||
                bic == BuiltInCategory.OST_StructuralFramingTags ||
                bic == BuiltInCategory.OST_StructuralColumnTags ||
                bic == BuiltInCategory.OST_MechanicalEquipmentTags ||
                bic == BuiltInCategory.OST_PlumbingFixtureTags ||
                bic == BuiltInCategory.OST_ElectricalEquipmentTags ||
                bic == BuiltInCategory.OST_LightingFixtureTags ||
                bic == BuiltInCategory.OST_StairsTags ||
                bic == BuiltInCategory.OST_FurnitureTags)
                return true;

            // Fallback: name heuristic (localized may not contain "Tag", but often does)
            var n = (cat.Name ?? "").ToLowerInvariant();
            if (n.Contains("tag") || n.Contains("štítek") || n.Contains("popisek"))
                return true;

            return false;
        }
    }
}