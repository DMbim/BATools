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
    /// FORMAT VERSIONS:
    ///   v1 (original): single data sheet, data starts at row 2, separate "Legend" sheet
    ///   v2 (current):  single data sheet, row 2 = data types, data starts at row 3,
    ///                  legend + export info written below data on the same sheet
    ///   Auto-detection: if column A row 2 contains a valid ElementId the file is v1;
    ///   otherwise it is treated as v2.
    ///
    /// COLUMN MATCHING:
    ///   Columns matched by header name (row 1) against originalFields.DisplayName.
    ///   Unknown columns in the Excel file are silently ignored.
    ///   Missing columns are also silently skipped (partial imports are valid).
    ///
    /// DATA ROW TERMINATION:
    ///   Reading stops at the first row whose column A (__ElementId) is blank or
    ///   non-numeric — this is where the legend/info block begins.
    ///
    /// HIDDEN SYSTEM COLUMNS:
    ///   Column A (__ElementId): stored as double on export; read as double → long.
    ///   Column B (__UniqueId):  used as fallback if ElementId lookup fails.
    /// </summary>
    public class ExcelImportService
    {
        public (List<ScheduleFieldMeta> Fields, List<ImportRowData> Rows) Import(
            string                  filePath,
            List<ScheduleFieldMeta> originalFields)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Excel file not found: {filePath}");
            if (originalFields == null || originalFields.Count == 0)
                throw new ArgumentException("originalFields must not be empty.", nameof(originalFields));

            try
            {
                using var workbook = new XLWorkbook(filePath);
                IXLWorksheet ws = FindDataWorksheet(workbook);

                int dataStartRow = DetectDataStartRow(ws);
                Dictionary<int, ScheduleFieldMeta> columnToField =
                    MapColumnsToFields(ws, originalFields);
                List<ImportRowData> rows = ParseDataRows(ws, columnToField, dataStartRow);

                return (originalFields, rows);
            }
            catch (Exception ex) when (!(ex is FileNotFoundException || ex is InvalidOperationException))
            {
                throw new InvalidOperationException(
                    $"Failed to read Excel file '{Path.GetFileName(filePath)}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Reads the schedule name from the first (data) worksheet.
        /// Used by ImportScheduleCommand to locate the matching Revit schedule.
        /// </summary>
        public static string GetScheduleNameFromFile(string filePath)
        {
            using var workbook = new XLWorkbook(filePath);
            return FindDataWorksheet(workbook)?.Name
                ?? Path.GetFileNameWithoutExtension(filePath);
        }

        // ─── Worksheet detection ───────────────────────────────────────────────

        private static IXLWorksheet FindDataWorksheet(XLWorkbook workbook)
        {
            if (!workbook.Worksheets.Any())
                throw new InvalidOperationException(
                    "The workbook contains no worksheets. " +
                    "Ensure the file was produced by BA Schedule Exporter.");

            // v2: single sheet (no separate Legend sheet)
            // v1: first sheet is data, second sheet is "Legend" — still take first
            return workbook.Worksheets.First();
        }

        // ─── Format version detection ──────────────────────────────────────────

        /// <summary>
        /// Returns the row index where data begins.
        /// v1 format: row 2 (no data type row).
        /// v2 format: row 3 (row 2 is the data type label row).
        /// Detection: if column A row 2 contains a valid ElementId → v1; otherwise v2.
        /// </summary>
        private static int DetectDataStartRow(IXLWorksheet ws)
        {
            IXLCell cell = ws.Cell(2, 1);
            if (cell.DataType == XLDataType.Number)
            {
                double d = cell.GetDouble();
                if (d != 0) return 2; // v1 format: ElementId in row 2
            }
            else
            {
                string s = cell.GetString();
                if (!string.IsNullOrWhiteSpace(s) && long.TryParse(s, out long id) && id != 0)
                    return 2; // v1 format
            }
            return 3; // v2 format: row 2 is data type row
        }

        // ─── Column mapping ────────────────────────────────────────────────────

        private Dictionary<int, ScheduleFieldMeta> MapColumnsToFields(
            IXLWorksheet            ws,
            List<ScheduleFieldMeta> originalFields)
        {
            var fieldsByName = new Dictionary<string, ScheduleFieldMeta>(
                StringComparer.OrdinalIgnoreCase);
            foreach (ScheduleFieldMeta meta in originalFields)
                fieldsByName[meta.DisplayName] = meta;

            var map     = new Dictionary<int, ScheduleFieldMeta>();
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (int col = 3; col <= lastCol; col++)
            {
                string header = ws.Cell(1, col).GetString();
                if (string.IsNullOrWhiteSpace(header)) continue;

                if (fieldsByName.TryGetValue(header, out ScheduleFieldMeta meta))
                    map[col] = meta;
                // Unknown columns (user annotations, extra columns) are silently ignored
            }

            return map;
        }

        // ─── Data row parsing ──────────────────────────────────────────────────

        private List<ImportRowData> ParseDataRows(
            IXLWorksheet                     ws,
            Dictionary<int, ScheduleFieldMeta> columnToField,
            int                              dataStartRow)
        {
            var rows   = new List<ImportRowData>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? dataStartRow;

            for (int r = dataStartRow; r <= lastRow; r++)
            {
                // Stop reading on the first row with no valid ElementId —
                // this is where the legend / export info block begins
                long elementId = ReadElementId(ws.Cell(r, 1));
                if (elementId == 0) break;

                string uniqueId = ws.Cell(r, 2).GetString();

                var importRow = new ImportRowData
                {
                    ElementId = elementId,
                    UniqueId  = uniqueId
                };

                foreach (KeyValuePair<int, ScheduleFieldMeta> kvp in columnToField)
                {
                    ScheduleFieldMeta meta     = kvp.Value;
                    string            rawValue = ws.Cell(r, kvp.Key).GetString();

                    importRow.Cells[meta.ColumnIndex] = new ImportCellData
                    {
                        RawValue = rawValue,
                        State    = meta.IsReadOnly ? ChangeState.Skipped : ChangeState.Unchanged
                    };
                }

                rows.Add(importRow);
            }

            return rows;
        }

        // ─── ElementId reading ─────────────────────────────────────────────────

        /// <summary>
        /// Reads the __ElementId cell.
        /// On export it is stored as a double (cast from long).
        /// Returns 0 if parsing fails — caller must treat row as end-of-data.
        /// </summary>
        private static long ReadElementId(IXLCell cell)
        {
            if (cell.DataType == XLDataType.Number)
            {
                double d = cell.GetDouble();
                return d >= long.MinValue && d <= long.MaxValue && d != 0 ? (long)d : 0L;
            }

            string s = cell.GetString();
            return long.TryParse(s, out long id) ? id : 0L;
        }
    }
}
