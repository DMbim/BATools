using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA_Tools.ScheduleExporter.Helpers;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.Services
{
    /// <summary>
    /// Reads a ViewSchedule definition and all its element data into serializable models.
    ///
    /// ELEMENT COLLECTION STRATEGY:
    ///   FilteredElementCollector(doc, schedule.Id).WhereElementIsNotElementType()
    ///   This is the only reliable API to get exactly the elements a schedule shows,
    ///   respecting all schedule filters. It does NOT work for:
    ///     - Material Takeoff schedules (material rows are not elements)
    ///     - Note Block schedules
    ///   These are detected and rejected before collection.
    ///
    /// CALCULATED FIELD VALUES:
    ///   Formula fields (e.g. Area*Area) have no backing parameter. Their values are
    ///   read from the schedule's rendered TableData after element rows are built.
    ///   Matching is positional after re-sorting our element list to match the schedule's
    ///   defined sort/group order. If row counts don't match (grouped schedule) the
    ///   calculated cells are left empty — the cells are still gray/locked in Excel.
    ///
    /// STORAGETYPE DETECTION:
    ///   Probes all collected elements per field (not just the first) to handle mixed-
    ///   category schedules where the first element may not have every shared parameter.
    /// </summary>
    public class ScheduleReaderService
    {
        private readonly Document _doc;

        public ScheduleReaderService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public (List<ScheduleFieldMeta> Fields, List<ScheduleRowData> Rows) ReadSchedule(
            ViewSchedule schedule)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));

            ValidateScheduleType(schedule);

            if (!schedule.Definition.IsItemized)
                throw new NotSupportedException(
                    $"Schedule '{schedule.Name}' is not itemized (rows are grouped). " +
                    "Switch the schedule to itemized in Revit first, or the exported row " +
                    "count will differ from what the schedule shows on screen.");

            List<ScheduleFieldMeta> fields  = BuildFieldMetas(schedule);
            List<Element>           elements = CollectElements(schedule);

            // Probe all elements per field — single-element probe fails for shared
            // parameters not bound to every category in a multi-category schedule
            foreach (ScheduleFieldMeta meta in fields)
            {
                if (meta.Category != FieldCategory.Calculated && meta.Category != FieldCategory.Hidden)
                    ScheduleFieldTypeDetector.UpdateStorageType(meta, _doc, elements);
            }

            List<ScheduleRowData> rows = BuildRows(fields, elements);

            // Second pass: fill calculated column values from schedule table data
            FillCalculatedColumnValues(schedule, fields, elements, rows);

            return (fields, rows);
        }

        // ─── Schedule validation ───────────────────────────────────────────────
        private static void ValidateScheduleType(ViewSchedule schedule)
        {
            // IsMaterialsSchedule was removed from ScheduleDefinition in Revit 2026 API.
            // Material takeoff schedules will fall through — the collector returns host elements
            // which is acceptable. Key schedules produce unrelated rows and are blocked.
            if (schedule.Definition.IsKeySchedule)
                throw new NotSupportedException(
                    $"Schedule '{schedule.Name}' is a Key Schedule. Key schedules are not supported.");
        }

        // ─── Field metadata ────────────────────────────────────────────────────

        private List<ScheduleFieldMeta> BuildFieldMetas(ViewSchedule schedule)
        {
            ScheduleDefinition definition = schedule.Definition;
            int totalFields = definition.GetFieldCount();
            var metas = new List<ScheduleFieldMeta>(totalFields);
            int columnIndex = 0;

            for (int i = 0; i < totalFields; i++)
            {
                ScheduleField field = definition.GetField(i);
                if (field.IsHidden) continue;

                FieldCategory category = ScheduleFieldTypeDetector.DetermineCategory(field);

                string displayName = field.ColumnHeading;
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = field.GetName();

                metas.Add(new ScheduleFieldMeta
                {
                    ColumnIndex  = columnIndex++,
                    FieldId      = field.FieldId,
                    ParameterId  = field.ParameterId,
                    DisplayName  = displayName,
                    Category     = category,
                    StorageType  = StorageType.None
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
            List<Element> elements)
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

                    // Calculated fields: attempt param read (will be empty for pure formulas).
                    // FillCalculatedColumnValues will overwrite with table data on second pass.
                    Parameter param = ScheduleFieldTypeDetector.GetParameterForField(
                        meta, _doc, element);
                    row.Values[meta.ColumnIndex] = ParameterValueConverter.ToExcelValue(param, _doc);
                }

                rows.Add(row);
            }

            return rows;
        }

        // ─── Calculated column values from table data ──────────────────────────

        /// <summary>
        /// Reads formula/calculated field values from the schedule's rendered TableData.
        /// Matching is positional after re-sorting our rows to match the schedule's
        /// defined sort/group order. Silently skips if row counts don't match or if
        /// the table API throws (e.g. schedule not yet regenerated in session).
        /// </summary>
        private void FillCalculatedColumnValues(
            ViewSchedule schedule,
            List<ScheduleFieldMeta> fields,
            List<Element> elements,
            List<ScheduleRowData> rows)
        {
            var calculatedFields = fields
                .Where(f => f.Category == FieldCategory.Calculated)
                .ToList();
            if (calculatedFields.Count == 0) return;

            try
            {
                TableData        tableData = schedule.GetTableData();
                TableSectionData body      = tableData.GetSectionData(SectionType.Body);
                int tableRowCount = body.NumberOfRows;

                // If row count mismatches, schedule has group headers or grand totals —
                // we cannot reliably match rows to elements positionally
                if (tableRowCount != rows.Count) return;

                Dictionary<int, ScheduleFieldMeta> tableColMap =
                    BuildTableColumnMap(schedule, fields);

                // Re-sort our rows to match the schedule's displayed order
                List<ScheduleRowData> sortedRows =
                    ApplyScheduleSortOrder(schedule, fields, rows);

                for (int i = 0; i < tableRowCount; i++)
                {
                    ScheduleRowData row = sortedRows[i];
                    foreach (KeyValuePair<int, ScheduleFieldMeta> kvp in tableColMap)
                    {
                        if (kvp.Value.Category != FieldCategory.Calculated) continue;
                        string cellText = body.GetCellText(i, kvp.Key);
                        row.Values[kvp.Value.ColumnIndex] = cellText ?? string.Empty;
                    }
                }
            }
            catch
            {
                // Best-effort — silent failure leaves calculated cells empty
            }
        }

        /// <summary>
        /// Maps table column index (0-based, visible fields only) to ScheduleFieldMeta.
        /// Table column order matches ScheduleDefinition field order skipping hidden fields.
        /// </summary>
        private Dictionary<int, ScheduleFieldMeta> BuildTableColumnMap(
            ViewSchedule schedule,
            List<ScheduleFieldMeta> fields)
        {
            var map          = new Dictionary<int, ScheduleFieldMeta>();
            var metaByFieldId = fields.ToDictionary(f => f.FieldId.IntegerValue);

            ScheduleDefinition def = schedule.Definition;
            int tableCol = 0;
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                ScheduleField field = def.GetField(i);
                if (field.IsHidden) continue;
                if (metaByFieldId.TryGetValue(field.FieldId.IntegerValue, out ScheduleFieldMeta meta))
                    map[tableCol] = meta;
                tableCol++;
            }
            return map;
        }

        /// <summary>
        /// Sorts the row list to match the schedule's defined sort/group field order
        /// so that positional matching against TableSectionData rows is valid.
        /// Falls back to ElementId ascending when no sort fields are defined.
        /// Uses string comparison as sort key — sufficient for Family/Type/string fields
        /// which are the most common sort criteria.
        /// </summary>
        private List<ScheduleRowData> ApplyScheduleSortOrder(
            ViewSchedule schedule,
            List<ScheduleFieldMeta> fields,
            List<ScheduleRowData> rows)
        {
            int sgCount = schedule.Definition.GetSortGroupFieldCount();
            if (sgCount == 0)
                return rows.OrderBy(r => r.ElementId).ToList();

            var fieldByFieldId = fields.ToDictionary(f => f.FieldId.IntegerValue);

            var sortSpecs = new List<(int colIdx, bool ascending)>();
            for (int i = 0; i < sgCount; i++)
            {
                ScheduleSortGroupField sgf  = schedule.Definition.GetSortGroupField(i);
                if (!fieldByFieldId.TryGetValue(sgf.FieldId.IntegerValue, out ScheduleFieldMeta meta))
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
                        ? rows.OrderBy(r => GetStringValue(r, colIdx))
                        : rows.OrderByDescending(r => GetStringValue(r, colIdx));
                else
                    sorted = asc
                        ? sorted.ThenBy(r => GetStringValue(r, colIdx))
                        : sorted.ThenByDescending(r => GetStringValue(r, colIdx));
            }

            return sorted.ToList();
        }

        private static string GetStringValue(ScheduleRowData row, int colIdx)
            => row.Values.TryGetValue(colIdx, out object v) ? v?.ToString() ?? string.Empty : string.Empty;
    }
}
