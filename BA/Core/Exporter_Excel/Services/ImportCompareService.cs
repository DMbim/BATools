using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA_Tools.ScheduleExporter.Helpers;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.Services
{
    /// <summary>
    /// Diffs imported Excel data against the current live document state.
    ///
    /// COMPARISON LOGIC:
    ///   For each import row, the element is looked up by ElementId (primary) or UniqueId (fallback).
    ///   Missing elements are recorded in DeletedElementIds.
    ///   For each non-read-only cell, the current parameter value is read from the live element
    ///   and compared as string against the imported raw value (same format used during export).
    ///   This string-level comparison may produce false positives for double parameters
    ///   if the user reformatted the number (e.g. "3.5 m" vs "3.500 m"), but the write
    ///   operation is idempotent in those cases — the same internal value gets written.
    ///
    /// TYPE PARAMETER TRACKING:
    ///   Type parameter changes are grouped by (typeId, columnIndex).
    ///   If multiple rows belonging to the same type specify different new values for the same
    ///   type parameter, HasConflict is set and all conflicting values are listed.
    ///   ParameterWriteService uses last-write-wins for conflicts (user was warned).
    ///
    /// INSTANCE COUNT CACHE:
    ///   Built once via a single FilteredElementCollector scan before row processing begins.
    ///   Never re-queried inside the per-row loop.
    /// </summary>
    public class ImportCompareService
    {
        private readonly Document _doc;

        public ImportCompareService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public ImportCompareResult Compare(
            List<ScheduleFieldMeta> fields,
            List<ImportRowData> importRows)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            if (importRows == null) throw new ArgumentNullException(nameof(importRows));

            var result = new ImportCompareResult
            {
                TotalRows = importRows.Count
            };

            Dictionary<long, int> typeInstanceCounts = BuildTypeInstanceCountCache();
            Dictionary<int, ScheduleFieldMeta> fieldsByIndex = fields.ToDictionary(f => f.ColumnIndex);

            // Track type param changes: (typeId, columnIndex) -> list of (newValue, sourceElementId)
            var typeParamChanges = new Dictionary<(long typeId, int colIdx), List<(string newVal, long srcId)>>();

            foreach (ImportRowData importRow in importRows)
            {
                Element element = ResolveElement(importRow);

                if (element == null)
                {
                    result.DeletedElementIds.Add(importRow.ElementId);
                    result.TotalRows--;
                    continue;
                }

                bool rowHasAnyChange = false;

                foreach (KeyValuePair<int, ImportCellData> pair in importRow.Cells)
                {
                    int colIdx = pair.Key;
                    ImportCellData cellData = pair.Value;

                    if (cellData.State == ChangeState.Skipped)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    if (!fieldsByIndex.TryGetValue(colIdx, out ScheduleFieldMeta meta) || meta.IsReadOnly)
                    {
                        cellData.State = ChangeState.Skipped;
                        result.SkippedCount++;
                        continue;
                    }

                    Parameter param = ScheduleFieldTypeDetector.GetParameterForField(meta, _doc, element);
                    if (param == null)
                    {
                        cellData.State = ChangeState.Skipped;
                        cellData.ValidationError = "Parameter not found on element.";
                        result.SkippedCount++;
                        continue;
                    }

                    string currentValue = ParameterValueConverter
                        .ToExcelValue(param, _doc)?.ToString() ?? string.Empty;

                    if (string.Equals(currentValue, cellData.RawValue, StringComparison.Ordinal))
                    {
                        cellData.State = ChangeState.Unchanged;
                    }
                    else
                    {
                        cellData.State = ChangeState.Changed;
                        rowHasAnyChange = true;

                        if (meta.Category == FieldCategory.TypeParameter)
                        {
                            ElementId typeElementId = element.GetTypeId();
                            if (typeElementId != ElementId.InvalidElementId)
                            {
                                var key = (typeElementId.Value, colIdx);
                                if (!typeParamChanges.ContainsKey(key))
                                    typeParamChanges[key] = new List<(string, long)>();
                                typeParamChanges[key].Add((cellData.RawValue, importRow.ElementId));
                            }
                        }
                    }
                }

                if (rowHasAnyChange)
                    result.ChangedRowCount++;
                else
                    result.UnchangedRowCount++;

                result.ProcessableRows.Add(importRow);
            }

            BuildTypeParameterWarnings(result, typeParamChanges, fieldsByIndex, typeInstanceCounts);

            return result;
        }

        private Element ResolveElement(ImportRowData row)
        {
            if (row.ElementId != 0)
            {
                Element e = _doc.GetElement(new ElementId(row.ElementId));
                if (e != null) return e;
            }

            if (!string.IsNullOrEmpty(row.UniqueId))
                return _doc.GetElement(row.UniqueId);

            return null;
        }

        private void BuildTypeParameterWarnings(
            ImportCompareResult result,
            Dictionary<(long typeId, int colIdx), List<(string newVal, long srcId)>> typeParamChanges,
            Dictionary<int, ScheduleFieldMeta> fieldsByIndex,
            Dictionary<long, int> typeInstanceCounts)
        {
            foreach (KeyValuePair<(long typeId, int colIdx), List<(string newVal, long srcId)>> kvp
                     in typeParamChanges)
            {
                long typeId = kvp.Key.typeId;
                int colIdx = kvp.Key.colIdx;
                List<(string newVal, long srcId)> changes = kvp.Value;

                if (!fieldsByIndex.TryGetValue(colIdx, out ScheduleFieldMeta meta)) continue;

                Element typeElement = _doc.GetElement(new ElementId(typeId));

                // Read current value from the type element
                string currentValue = string.Empty;
                if (typeElement != null)
                {
                    foreach (Parameter p in typeElement.Parameters)
                    {
                        if (p.Id == meta.ParameterId)
                        {
                            currentValue = ParameterValueConverter
                                .ToExcelValue(p, _doc)?.ToString() ?? string.Empty;
                            break;
                        }
                    }
                }

                List<string> distinctValues = changes
                    .Select(c => c.newVal)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                typeInstanceCounts.TryGetValue(typeId, out int instanceCount);

                result.TypeParameterWarnings.Add(new TypeParameterWarning
                {
                    ParameterName       = meta.DisplayName,
                    CurrentValue        = currentValue,
                    NewValue            = distinctValues[0],
                    ElementTypeName     = typeElement?.Name ?? typeId.ToString(),
                    ElementTypeId       = typeId,
                    AffectedInstanceCount = instanceCount,
                    HasConflict         = distinctValues.Count > 1,
                    ConflictingValues   = distinctValues
                });
            }
        }

        /// <summary>
        /// Builds a type-id to instance-count map in a single collector pass.
        /// Must not be called inside any per-element or per-field loop.
        /// </summary>
        private Dictionary<long, int> BuildTypeInstanceCountCache()
        {
            var cache = new Dictionary<long, int>();

            IList<Element> allElements = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (Element e in allElements)
            {
                ElementId typeId = e.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) continue;
                long id = typeId.Value;
                cache.TryGetValue(id, out int count);
                cache[id] = count + 1;
            }

            return cache;
        }
    }
}
