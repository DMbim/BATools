using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.Services
{
    /// <summary>
    /// Reads a previously exported .xlsx file back into ImportRowData structures.
    ///
    /// MATCHING STRATEGY:
    ///   Columns are matched by header display name against originalFields.
    ///   Unknown columns in the Excel file (user-added) are silently ignored.
    ///   Columns missing from the Excel file relative to originalFields are also skipped.
    ///   This makes the import tolerant of reordered or partially modified files.
    ///
    /// HIDDEN SYSTEM COLUMNS:
    ///   Column A (__ElementId): stored as a number (double) during export. Read as double -> long.
    ///   Column B (__UniqueId):  stored as string. Used as fallback during compare if ElementId lookup fails.
    ///
    /// VALUE READING:
    ///   All values read as strings via GetString(). For numeric cells, ClosedXML's GetString()
    ///   returns the numeric representation (e.g. "42" for an integer cell). This is consistent
    ///   with how ParameterValueConverter.ToExcelValue() produces strings during export.
    /// </summary>
    public class ExcelImportService
    {
        public (List<ScheduleFieldMeta> Fields, List<ImportRowData> Rows) Import(
            string filePath,
            List<ScheduleFieldMeta> originalFields)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Excel file not found: {filePath}");
            if (originalFields == null || originalFields.Count == 0)
                throw new ArgumentException("originalFields must not be empty.", nameof(originalFields));

            using var workbook = new XLWorkbook(filePath);
            IXLWorksheet ws = FindDataWorksheet(workbook);

            Dictionary<int, ScheduleFieldMeta> columnToField = MapColumnsToFields(ws, originalFields);
            List<ImportRowData> rows = ParseDataRows(ws, columnToField);

            return (originalFields, rows);
        }

        /// <summary>Returns the schedule name from the first non-Legend worksheet.</summary>
        public static string GetScheduleNameFromFile(string filePath)
        {
            using var workbook = new XLWorkbook(filePath);
            IXLWorksheet ws = FindDataWorksheet(workbook);
            return ws?.Name ?? Path.GetFileNameWithoutExtension(filePath);
        }

        // ─── Internal ──────────────────────────────────────────────────────────

        private static IXLWorksheet FindDataWorksheet(XLWorkbook workbook)
        {
            foreach (IXLWorksheet sheet in workbook.Worksheets)
            {
                if (!sheet.Name.Equals("Legend", StringComparison.OrdinalIgnoreCase))
                    return sheet;
            }
            throw new InvalidOperationException(
                "No valid data worksheet found in the workbook. " +
                "Ensure the file was produced by the BA Schedule Exporter.");
        }

        /// <summary>
        /// Reads the header row (row 1) and builds a map from Excel column number to ScheduleFieldMeta.
        /// Columns A and B are system columns; data columns start at C (index 3).
        /// </summary>
        private Dictionary<int, ScheduleFieldMeta> MapColumnsToFields(
            IXLWorksheet ws,
            List<ScheduleFieldMeta> originalFields)
        {
            var fieldsByName = new Dictionary<string, ScheduleFieldMeta>(
                StringComparer.OrdinalIgnoreCase);
            foreach (ScheduleFieldMeta meta in originalFields)
                fieldsByName[meta.DisplayName] = meta;

            var map = new Dictionary<int, ScheduleFieldMeta>();
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (int col = 3; col <= lastCol; col++) // Skip A (__ElementId) and B (__UniqueId)
            {
                string header = ws.Cell(1, col).GetString();
                if (string.IsNullOrWhiteSpace(header)) continue;

                if (fieldsByName.TryGetValue(header, out ScheduleFieldMeta meta))
                    map[col] = meta;
                // Unrecognized columns (user-added annotations) are silently ignored
            }

            return map;
        }

        private List<ImportRowData> ParseDataRows(
            IXLWorksheet ws,
            Dictionary<int, ScheduleFieldMeta> columnToField)
        {
            var rows = new List<ImportRowData>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (int r = 2; r <= lastRow; r++) // Row 1 is header
            {
                long elementId = ReadElementId(ws.Cell(r, 1));
                if (elementId == 0) continue; // Skip rows with no valid ElementId

                string uniqueId = ws.Cell(r, 2).GetString();

                var importRow = new ImportRowData
                {
                    ElementId = elementId,
                    UniqueId = uniqueId
                };

                foreach (KeyValuePair<int, ScheduleFieldMeta> kvp in columnToField)
                {
                    int col = kvp.Key;
                    ScheduleFieldMeta meta = kvp.Value;
                    string rawValue = ws.Cell(r, col).GetString();

                    importRow.Cells[meta.ColumnIndex] = new ImportCellData
                    {
                        RawValue = rawValue,
                        State = meta.IsReadOnly ? ChangeState.Skipped : ChangeState.Unchanged
                    };
                }

                rows.Add(importRow);
            }

            return rows;
        }

        /// <summary>
        /// Reads the __ElementId cell. During export it was stored as a double (long cast).
        /// Handles both numeric cells (primary) and string cells (edge case).
        /// Returns 0 if parsing fails — caller must skip the row.
        /// </summary>
        private static long ReadElementId(IXLCell cell)
        {
            if (cell.DataType == XLDataType.Number)
            {
                double d = cell.GetDouble();
                return d >= long.MinValue && d <= long.MaxValue ? (long)d : 0L;
            }

            string s = cell.GetString();
            return long.TryParse(s, out long id) ? id : 0L;
        }
    }
}
