using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA_Tools.ScheduleExporter.Helpers;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.Services
{
    public class ScheduleReaderService
    {
        private readonly Document _doc;

        public ScheduleReaderService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <summary>
        /// Reads the schedule definition, all element rows, and schedule metadata.
        /// Returns fields, rows, and an export context capturing filter/sort info.
        /// </summary>
        public (List<ScheduleFieldMeta> Fields,
                List<ScheduleRowData>   Rows,
                ScheduleExportContext   Context)
            ReadSchedule(ViewSchedule schedule)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));

            ValidateScheduleType(schedule);

            if (!schedule.Definition.IsItemized)
                throw new NotSupportedException(
                    $"Schedule '{schedule.Name}' is not itemized (rows are grouped). " +
                    "Switch to itemized in Revit first, or the exported row count " +
                    "will differ from the schedule on screen.");

            List<ScheduleFieldMeta> fields   = BuildFieldMetas(schedule);
            List<Element>           elements = CollectElements(schedule);

            // Probe all elements per field — single-element probe fails for shared
            // parameters not bound to every category in a multi-category schedule
            foreach (ScheduleFieldMeta meta in fields)
            {
                if (meta.Category != FieldCategory.Calculated
                    && meta.Category != FieldCategory.Hidden)
                {
                    ScheduleFieldTypeDetector.UpdateStorageType(meta, _doc, elements);
                }

                // Populate data type label after StorageType is known
                meta.DataTypeLabel = BuildDataTypeLabel(meta);
            }

            List<ScheduleRowData> rows = BuildRows(fields, elements);

            // Second pass: fill calculated column values from rendered table data
            FillCalculatedColumnValues(schedule, fields, elements, rows);

            ScheduleExportContext context = BuildExportContext(schedule, elements.Count);

            return (fields, rows, context);
        }

        // ─── Schedule validation ───────────────────────────────────────────────

        private static void ValidateScheduleType(ViewSchedule schedule)
        {
            // IsMaterialsSchedule was removed from ScheduleDefinition in Revit 2026 API.
            // Key schedules produce non-element rows and are blocked.
            if (schedule.Definition.IsKeySchedule)
                throw new NotSupportedException(
                    $"Schedule '{schedule.Name}' is a Key Schedule and cannot be exported.");
        }

        // ─── Field metadata ────────────────────────────────────────────────────

        private List<ScheduleFieldMeta> BuildFieldMetas(ViewSchedule schedule)
        {
            ScheduleDefinition def       = schedule.Definition;
            int                total     = def.GetFieldCount();
            var                metas     = new List<ScheduleFieldMeta>(total);
            int                colIndex  = 0;

            for (int i = 0; i < total; i++)
            {
                ScheduleField field = def.GetField(i);
                if (field.IsHidden) continue;

                string displayName = field.ColumnHeading;
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = field.GetName();

                metas.Add(new ScheduleFieldMeta
                {
                    ColumnIndex  = colIndex++,
                    FieldId      = field.FieldId,
                    ParameterId  = field.ParameterId,
                    DisplayName  = displayName,
                    Category     = ScheduleFieldTypeDetector.DetermineCategory(field),
                    StorageType  = StorageType.None,
                    DataTypeLabel = string.Empty
                });
            }

            return metas;
        }

        // ─── Element collection ────────────────────────────────────────────────

        private List<Element> CollectElements(ViewSchedule schedule)
        {
            return new FilteredElementCollector(_doc, schedule.Id)
                .WhereElementIsNotElementType()
                .ToElements()
                .OrderBy(e => e.Id.Value)
                .ToList();
        }

        // ─── Row building ──────────────────────────────────────────────────────

        private List<ScheduleRowData> BuildRows(
            List<ScheduleFieldMeta> fields,
            List<Element>           elements)
        {
            var rows = new List<ScheduleRowData>(elements.Count);

            foreach (Element element in elements)
            {
                var row = new ScheduleRowData
                {
                    ElementId = element.Id.Value,
                    UniqueId  = element.UniqueId
                };

                foreach (ScheduleFieldMeta meta in fields)
                {
                    if (meta.Category == FieldCategory.Hidden)
                    {
                        row.Values[meta.ColumnIndex] = string.Empty;
                        continue;
                    }

                    // Calculated fields: attempt param read — will be empty for pure formulas.
                    // FillCalculatedColumnValues overwrites with table data in a second pass.
                    Parameter param = ScheduleFieldTypeDetector.GetParameterForField(
                        meta, _doc, element);
                    row.Values[meta.ColumnIndex] =
                        ParameterValueConverter.ToExcelValue(param, _doc);
                }

                rows.Add(row);
            }

            return rows;
        }

        // ─── Calculated column values from table data ──────────────────────────

        /// <summary>
        /// Reads formula/calculated field values from the schedule's rendered TableData.
        /// Also reads calculated values (e.g. counts, percentages) that have backing data
        /// in the table but no accessible parameter on elements.
        /// Positional matching is used after re-sorting rows to match the schedule sort order.
        /// Silently skips if row counts don't match or the table API is unavailable.
        /// </summary>
        private void FillCalculatedColumnValues(
            ViewSchedule            schedule,
            List<ScheduleFieldMeta> fields,
            List<Element>           elements,
            List<ScheduleRowData>   rows)
        {
            var calcFields = fields
                .Where(f => f.Category == FieldCategory.Calculated)
                .ToList();
            if (calcFields.Count == 0) return;

            try
            {
                TableData        tableData = schedule.GetTableData();
                TableSectionData body      = tableData.GetSectionData(SectionType.Body);
                int tableRowCount = body.NumberOfRows;

                // Row count mismatch means grouping headers or grand totals are present —
                // positional matching would be unreliable; skip gracefully
                if (tableRowCount != rows.Count) return;

                Dictionary<int, ScheduleFieldMeta> tableColMap =
                    BuildTableColumnMap(schedule, fields);

                List<ScheduleRowData> sortedRows =
                    ApplyScheduleSortOrder(schedule, fields, rows);

                for (int i = 0; i < tableRowCount; i++)
                {
                    ScheduleRowData row = sortedRows[i];
                    foreach (KeyValuePair<int, ScheduleFieldMeta> kvp in tableColMap)
                    {
                        if (kvp.Value.Category != FieldCategory.Calculated) continue;
                        string cellText = body.GetCellText(i, kvp.Key) ?? string.Empty;
                        row.Values[kvp.Value.ColumnIndex] = cellText;
                    }
                }
            }
            catch
            {
                // Best-effort; calculated cells stay empty on failure
            }
        }

        private Dictionary<int, ScheduleFieldMeta> BuildTableColumnMap(
            ViewSchedule            schedule,
            List<ScheduleFieldMeta> fields)
        {
            var map           = new Dictionary<int, ScheduleFieldMeta>();
            var metaByFieldId = fields.ToDictionary(f => f.FieldId.IntegerValue);

            ScheduleDefinition def      = schedule.Definition;
            int                tableCol = 0;
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                ScheduleField field = def.GetField(i);
                if (field.IsHidden) continue;
                if (metaByFieldId.TryGetValue(field.FieldId.IntegerValue, out var meta))
                    map[tableCol] = meta;
                tableCol++;
            }
            return map;
        }

        private List<ScheduleRowData> ApplyScheduleSortOrder(
            ViewSchedule            schedule,
            List<ScheduleFieldMeta> fields,
            List<ScheduleRowData>   rows)
        {
            int sgCount = schedule.Definition.GetSortGroupFieldCount();
            if (sgCount == 0)
                return rows.OrderBy(r => r.ElementId).ToList();

            var fieldByFieldId = fields.ToDictionary(f => f.FieldId.IntegerValue);
            var sortSpecs      = new List<(int colIdx, bool ascending)>();

            for (int i = 0; i < sgCount; i++)
            {
                ScheduleSortGroupField sgf = schedule.Definition.GetSortGroupField(i);
                if (!fieldByFieldId.TryGetValue(sgf.FieldId.IntegerValue, out var meta))
                    continue;
                sortSpecs.Add((meta.ColumnIndex,
                    sgf.SortOrder == Autodesk.Revit.DB.ScheduleSortOrder.Ascending));
            }

            if (sortSpecs.Count == 0)
                return rows.OrderBy(r => r.ElementId).ToList();

            IOrderedEnumerable<ScheduleRowData> sorted = null;
            for (int i = 0; i < sortSpecs.Count; i++)
            {
                var (colIdx, asc) = sortSpecs[i];
                if (i == 0)
                    sorted = asc
                        ? rows.OrderBy(r => GetSortKey(r, colIdx))
                        : rows.OrderByDescending(r => GetSortKey(r, colIdx));
                else
                    sorted = asc
                        ? sorted.ThenBy(r => GetSortKey(r, colIdx))
                        : sorted.ThenByDescending(r => GetSortKey(r, colIdx));
            }

            return sorted.ToList();
        }

        private static string GetSortKey(ScheduleRowData row, int colIdx)
            => row.Values.TryGetValue(colIdx, out object v)
                ? v?.ToString() ?? string.Empty
                : string.Empty;

        // ─── Data type label ───────────────────────────────────────────────────

        /// <summary>
        /// Builds the human-readable data type label shown in Excel row 2.
        /// For Double parameters, attempts to read the spec type name via LabelUtils.
        /// </summary>
        private string BuildDataTypeLabel(ScheduleFieldMeta meta)
        {
            string typePrefix = meta.Category == FieldCategory.TypeParameter ? "TYPE · " : string.Empty;

            switch (meta.Category)
            {
                case FieldCategory.Calculated:    return "Calculated (formula)";
                case FieldCategory.ElementIdType: return "Reference";
                case FieldCategory.Hidden:        return string.Empty;
            }

            switch (meta.StorageType)
            {
                case StorageType.String:
                    return typePrefix + "Text";

                case StorageType.Integer:
                    return typePrefix + "Integer";

                case StorageType.Double:
                {
                    if (meta.SpecTypeId == null || meta.SpecTypeId.Empty())
                        return typePrefix + "Number";
                    try
                    {
                        string specLabel = LabelUtils.GetLabelForSpec(meta.SpecTypeId);

                        // Attempt to read the project unit symbol for this spec type
                        FormatOptions fmtOpts = _doc.GetUnits()
                                                    .GetFormatOptions(meta.SpecTypeId);
                        ForgeTypeId symbolId = fmtOpts.GetSymbolTypeId();
                        if (symbolId != null && !symbolId.Empty())
                        {
                            string symbol = LabelUtils.GetLabelForSymbol(symbolId);
                            if (!string.IsNullOrWhiteSpace(symbol))
                                return $"{typePrefix}{specLabel} ({symbol})";
                        }

                        return typePrefix + specLabel;
                    }
                    catch
                    {
                        return typePrefix + "Number";
                    }
                }

                case StorageType.ElementId:
                    return "Reference";

                default:
                    return typePrefix + "Value";
            }
        }

        // ─── Export context (filters + sort) ───────────────────────────────────

        private ScheduleExportContext BuildExportContext(
            ViewSchedule schedule,
            int          elementCount)
        {
            var ctx = new ScheduleExportContext
            {
                ScheduleName  = schedule.Name,
                ExportedAt    = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss"),
                IsItemized    = schedule.Definition.IsItemized,
                TotalElements = elementCount
            };

            // Filters
            try
            {
                IList<ScheduleFilter> filters = schedule.Definition.GetFilters();
                foreach (ScheduleFilter filter in filters)
                    ctx.FilterDescriptions.Add(BuildFilterDescription(schedule, filter));
            }
            catch { /* GetFilters may not be available for all schedule types */ }

            // Sort/group fields
            try
            {
                int sgCount = schedule.Definition.GetSortGroupFieldCount();
                for (int i = 0; i < sgCount; i++)
                {
                    ScheduleSortGroupField sgf  = schedule.Definition.GetSortGroupField(i);
                    ScheduleField          field = schedule.Definition.GetField(sgf.FieldId);
                    string fieldName = field?.GetName() ?? "Unknown field";
                    string arrow     = sgf.SortOrder == Autodesk.Revit.DB.ScheduleSortOrder.Ascending
                        ? "↑" : "↓";
                    ctx.SortDescriptions.Add($"{fieldName} {arrow}");
                }
            }
            catch { }

            return ctx;
        }

        private static string BuildFilterDescription(
            ViewSchedule   schedule,
            ScheduleFilter filter)
        {
            string fieldName;
            try
            {
                ScheduleField field = schedule.Definition.GetField(filter.FieldId);
                fieldName = field?.GetName() ?? "Unknown field";
            }
            catch { fieldName = "Unknown field"; }

            string op = GetFilterTypeLabel(filter.FilterType);

            if (filter.FilterType == ScheduleFilterType.HasValue
                || filter.FilterType == ScheduleFilterType.HasNoValue)
                return $"{fieldName}  {op}";

            string value = GetFilterValueDisplay(filter);
            return $"{fieldName}  {op}  {value}";
        }

        private static string GetFilterTypeLabel(ScheduleFilterType type)
        {
            switch (type)
            {
                case ScheduleFilterType.Equal:          return "=";
                case ScheduleFilterType.NotEqual:       return "≠";
                case ScheduleFilterType.GreaterThan:    return ">";
                case ScheduleFilterType.GreaterThanOrEqual: return "≥";
                case ScheduleFilterType.LessThan:       return "<";
                case ScheduleFilterType.LessThanOrEqual:    return "≤";
                case ScheduleFilterType.Contains:       return "contains";
                case ScheduleFilterType.NotContains:    return "does not contain";
                case ScheduleFilterType.BeginsWith:     return "begins with";
                case ScheduleFilterType.EndsWith:       return "ends with";
                case ScheduleFilterType.HasValue:       return "has a value";
                case ScheduleFilterType.HasNoValue:     return "has no value";
                default:                                return type.ToString();
            }
        }

        private static string GetFilterValueDisplay(ScheduleFilter filter)
        {
            // Try each value type — only one getter will succeed per filter
            try { string s = filter.GetStringValue(); if (s != null) return $"\"{s}\""; }
            catch { }
            try { return filter.GetDoubleValue().ToString("G6"); }
            catch { }
            try { return filter.GetIntegerValue().ToString(); }
            catch { }
            try
            {
                ElementId id = filter.GetElementIdValue();
                return id != null ? id.Value.ToString() : "(none)";
            }
            catch { }
            return "?";
        }
    }
}
