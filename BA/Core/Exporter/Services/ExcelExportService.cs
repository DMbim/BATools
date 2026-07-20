using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.Services
{
    /// <summary>
    /// Writes schedule data to a single-sheet ClosedXML Excel workbook (.xlsx).
    ///
    /// SHEET LAYOUT:
    ///   Row 1:       Column headers  (dark navy, white bold)
    ///   Row 2:       Data type labels (color-coded by field category)
    ///   Rows 3..N+2: Data rows        (alternating white / pale-gray stripes)
    ///   Row N+3:     (blank separator)
    ///   Row N+4+:    Legend block
    ///   Row ??+:     Export info block (schedule name, date, filters, sort)
    ///
    /// NO SHEET PROTECTION — colors are informational only.
    /// Calculated cells are italic gray to indicate they are read-only on import.
    ///
    /// FORMULA INJECTION GUARD:
    ///   Any string starting with = + - @ is prefixed with a single-quote space
    ///   to prevent Excel treating the value as a formula.
    ///
    /// CELL LENGTH GUARD:
    ///   Excel cells are capped at 32 767 characters; longer values are truncated.
    /// </summary>
    public class ExcelExportService
    {
        // ─── Layout constants ──────────────────────────────────────────────────

        private const int ColElementId = 1; // Column A — hidden system column
        private const int ColUniqueId  = 2; // Column B — hidden system column
        private const int ColDataStart = 3; // Column C — first schedule field

        private const int RowHeader   = 1;
        private const int RowDataType = 2;
        private const int RowDataFrom = 3;

        private const int ExcelMaxCellLength = 32767;

        // ─── Colour palette ────────────────────────────────────────────────────

        // Header row
        private static readonly XLColor ClrHeaderBg   = XLColor.FromArgb(0x1F, 0x38, 0x64); // deep navy
        private static readonly XLColor ClrHeaderText  = XLColor.White;

        // Data type row backgrounds (per field category)
        private static readonly XLColor ClrDtInstance = XLColor.FromArgb(0xE2, 0xEF, 0xDA); // soft green
        private static readonly XLColor ClrDtType     = XLColor.FromArgb(0xBD, 0xD7, 0xEE); // cornflower blue
        private static readonly XLColor ClrDtCalc     = XLColor.FromArgb(0xD9, 0xD9, 0xD9); // light gray

        // Data type row foreground
        private static readonly XLColor ClrDtTextDark = XLColor.FromArgb(0x26, 0x3F, 0x22); // dark green text
        private static readonly XLColor ClrDtTextBlue = XLColor.FromArgb(0x1F, 0x49, 0x7D); // dark blue text
        private static readonly XLColor ClrDtTextGray = XLColor.FromArgb(0x59, 0x59, 0x59); // dark gray text

        // Data rows
        private static readonly XLColor ClrRowEven = XLColor.White;
        private static readonly XLColor ClrRowOdd  = XLColor.FromArgb(0xF2, 0xF6, 0xFB); // pale blue-gray
        private static readonly XLColor ClrCalcRow = XLColor.FromArgb(0xF0, 0xF0, 0xF0); // light gray for calc cells
        private static readonly XLColor ClrTypeRow = XLColor.FromArgb(0xEB, 0xF5, 0xFD); // very light blue for type cells

        // Section header
        private static readonly XLColor ClrSectionBg   = XLColor.FromArgb(0xD6, 0xDC, 0xE4); // muted blue-gray
        private static readonly XLColor ClrSectionText  = XLColor.FromArgb(0x1F, 0x38, 0x64);

        // Legend swatch border
        private static readonly XLColor ClrBorder = XLColor.FromArgb(0xCC, 0xCC, 0xCC);

        // ─── Public API ────────────────────────────────────────────────────────

        public void Export(
            string                  filePath,
            List<ScheduleFieldMeta> fields,
            List<ScheduleRowData>   rows,
            ScheduleExportContext   context)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            if (rows   == null) throw new ArgumentNullException(nameof(rows));
            if (context == null) context = new ScheduleExportContext
            {
                ScheduleName = "Schedule",
                ExportedAt   = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            // Guard: Excel cannot open a file that is already open by Excel
            if (File.Exists(filePath))
            {
                try   { File.Delete(filePath); }
                catch { throw new IOException(
                    $"Cannot overwrite '{Path.GetFileName(filePath)}' — the file may be open in Excel."); }
            }

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var workbook  = new XLWorkbook();
            string    sheetName = SanitizeSheetName(context.ScheduleName);
            IXLWorksheet ws     = workbook.Worksheets.Add(sheetName);

            int dataRowCount = rows.Count;
            int lastDataRow  = RowDataFrom + dataRowCount - 1;

            // ── Write sections ───────────────────────────────────────────────
            WriteHeaderRow(ws, fields);
            WriteDataTypeRow(ws, fields);
            WriteDataRows(ws, fields, rows);
            int nextRow = WriteInlineLegend(ws, fields, lastDataRow + 2);
            WriteExportInfoBlock(ws, context, nextRow + 1, fields.Count);

            // ── Global formatting ────────────────────────────────────────────
            ApplyColumnWidths(ws, fields);
            ApplyGlobalBorders(ws, fields.Count, dataRowCount);
            ApplyAutoFilterAndFreeze(ws, fields.Count);

            // Hide system columns
            ws.Column(ColElementId).Hide();
            ws.Column(ColUniqueId).Hide();

            ws.ShowGridLines = false; // Cleaner look with custom borders

            workbook.SaveAs(filePath);
        }

        // ─── Row 1: Header ────────────────────────────────────────────────────

        private void WriteHeaderRow(IXLWorksheet ws, List<ScheduleFieldMeta> fields)
        {
            // System columns
            StyleHeader(ws.Cell(RowHeader, ColElementId), "__ElementId");
            StyleHeader(ws.Cell(RowHeader, ColUniqueId),  "__UniqueId");

            foreach (ScheduleFieldMeta meta in fields)
            {
                IXLCell cell = ws.Cell(RowHeader, meta.ColumnIndex + ColDataStart);
                StyleHeader(cell, meta.DisplayName);

                // Subtle category tint on header background so columns are
                // distinguishable even on the dark header
                switch (meta.Category)
                {
                    case FieldCategory.TypeParameter:
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x17, 0x4A, 0x89);
                        break;
                    case FieldCategory.Calculated:
                    case FieldCategory.ElementIdType:
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x45, 0x45, 0x55);
                        break;
                }
            }

            ws.Row(RowHeader).Height = 20;
        }

        private static void StyleHeader(IXLCell cell, string text)
        {
            cell.Value = SafeString(text);
            cell.Style.Font.Bold          = true;
            cell.Style.Font.FontColor     = XLColor.White;
            cell.Style.Font.FontSize      = 10;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1F, 0x38, 0x64);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        }

        // ─── Row 2: Data type labels ──────────────────────────────────────────

        private void WriteDataTypeRow(IXLWorksheet ws, List<ScheduleFieldMeta> fields)
        {
            // System columns: keep consistent style
            StyleDataType(ws.Cell(RowDataType, ColElementId), "ID",       FieldCategory.Hidden);
            StyleDataType(ws.Cell(RowDataType, ColUniqueId),  "UniqueId", FieldCategory.Hidden);

            foreach (ScheduleFieldMeta meta in fields)
            {
                IXLCell cell = ws.Cell(RowDataType, meta.ColumnIndex + ColDataStart);
                StyleDataType(cell, meta.DataTypeLabel ?? string.Empty, meta.Category);
            }

            ws.Row(RowDataType).Height = 16;
        }

        private static void StyleDataType(IXLCell cell, string label, FieldCategory category)
        {
            cell.Value = SafeString(label);
            cell.Style.Font.Italic   = true;
            cell.Style.Font.FontSize = 9;
            cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            switch (category)
            {
                case FieldCategory.Instance:
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xE2, 0xEF, 0xDA);
                    cell.Style.Font.FontColor       = XLColor.FromArgb(0x26, 0x3F, 0x22);
                    break;
                case FieldCategory.TypeParameter:
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xBD, 0xD7, 0xEE);
                    cell.Style.Font.FontColor       = XLColor.FromArgb(0x1F, 0x49, 0x7D);
                    break;
                case FieldCategory.Calculated:
                case FieldCategory.ElementIdType:
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xD9, 0xD9, 0xD9);
                    cell.Style.Font.FontColor       = XLColor.FromArgb(0x59, 0x59, 0x59);
                    break;
                default:
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xE8, 0xE8, 0xE8);
                    cell.Style.Font.FontColor       = XLColor.FromArgb(0x80, 0x80, 0x80);
                    break;
            }
        }

        // ─── Rows 3+: Data ────────────────────────────────────────────────────

        private void WriteDataRows(
            IXLWorksheet            ws,
            List<ScheduleFieldMeta> fields,
            List<ScheduleRowData>   rows)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                int excelRow = RowDataFrom + r;
                ScheduleRowData row = rows[r];
                bool isOddRow = (r % 2 == 1);

                // System columns
                ws.Cell(excelRow, ColElementId).Value = (double)row.ElementId;
                ws.Cell(excelRow, ColUniqueId).Value  = row.UniqueId ?? string.Empty;

                foreach (ScheduleFieldMeta meta in fields)
                {
                    int     col  = meta.ColumnIndex + ColDataStart;
                    IXLCell cell = ws.Cell(excelRow, col);

                    object value = row.Values.TryGetValue(meta.ColumnIndex, out object v)
                        ? v : string.Empty;
                    SetCellValue(cell, value);
                    ApplyDataCellStyle(cell, meta, isOddRow);
                }

                ws.Row(excelRow).Height = 15;
            }
        }

        private static void SetCellValue(IXLCell cell, object value)
        {
            switch (value)
            {
                case int    i: cell.Value = i; break;
                case long   l: cell.Value = (double)l; break;
                case double d: cell.Value = d; break;
                case bool   b: cell.Value = b; break;
                case null:     cell.Value = Blank.Value; break;
                case string s:
                    // Formula injection guard
                    if (s.Length > 0 && (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@'))
                        s = " " + s;
                    // Excel cell length guard
                    if (s.Length > ExcelMaxCellLength)
                        s = s.Substring(0, ExcelMaxCellLength - 3) + "...";
                    cell.Value = s;
                    break;
                default:
                    cell.Value = SafeString(value.ToString());
                    break;
            }
        }

        private void ApplyDataCellStyle(IXLCell cell, ScheduleFieldMeta meta, bool isOddRow)
        {
            cell.Style.Font.FontSize = 10;
            cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            switch (meta.Category)
            {
                case FieldCategory.Calculated:
                case FieldCategory.ElementIdType:
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xF0, 0xF0, 0xF0);
                    cell.Style.Font.Italic          = true;
                    cell.Style.Font.FontColor       = XLColor.FromArgb(0x70, 0x70, 0x70);
                    break;

                case FieldCategory.TypeParameter:
                    cell.Style.Fill.BackgroundColor = isOddRow
                        ? XLColor.FromArgb(0xDB, 0xEC, 0xF8)
                        : XLColor.FromArgb(0xEB, 0xF5, 0xFD);
                    break;

                default: // Instance
                    cell.Style.Fill.BackgroundColor = isOddRow
                        ? XLColor.FromArgb(0xF2, 0xF6, 0xFB)
                        : XLColor.White;
                    break;
            }
        }

        // ─── Inline Legend ────────────────────────────────────────────────────

        /// <summary>Returns the next available row after the legend block.</summary>
        private int WriteInlineLegend(
            IXLWorksheet            ws,
            List<ScheduleFieldMeta> fields,
            int                     startRow)
        {
            bool hasTypeParam = fields.Any(f => f.Category == FieldCategory.TypeParameter);
            bool hasCalc      = fields.Any(f => f.Category == FieldCategory.Calculated
                                             || f.Category == FieldCategory.ElementIdType);

            int col1 = ColDataStart;
            int col2 = ColDataStart + 1;
            int col3 = ColDataStart + 2;
            int lastCol = ColDataStart + Math.Max(fields.Count - 1, 3);

            // Section title
            int r = startRow;
            WriteSectionTitle(ws, r, col1, lastCol, "LEGEND");
            r++;

            // Header row for legend table
            ws.Cell(r, col1).Value = "Column Type";
            ws.Cell(r, col2).Value = "Color";
            ws.Cell(r, col3).Value = "Import Behavior";
            ws.Row(r).Style.Font.Bold = true;
            ws.Row(r).Style.Font.FontSize = 9;
            ws.Row(r).Style.Font.FontColor = XLColor.FromArgb(0x44, 0x44, 0x44);
            r++;

            // Instance row
            WriteLegendRow(ws, r, col1, col2, col3,
                "Instance Parameter",
                XLColor.White,
                "Value is written back to the element on import.",
                border: true);
            r++;

            // Type parameter row
            if (hasTypeParam)
            {
                WriteLegendRow(ws, r, col1, col2, col3,
                    "Type Parameter",
                    XLColor.FromArgb(0xEB, 0xF5, 0xFD),
                    "Value is written to the element TYPE — affects ALL instances of that type. "
                    + "A warning dialog is shown before import commits.",
                    border: true);
                r++;
            }

            // Calculated row
            if (hasCalc)
            {
                WriteLegendRow(ws, r, col1, col2, col3,
                    "Calculated / Reference",
                    XLColor.FromArgb(0xF0, 0xF0, 0xF0),
                    "Value is formula-derived or a reference (italic gray). "
                    + "Any edits are ignored on import.",
                    border: true);
                r++;
            }

            return r;
        }

        private static void WriteLegendRow(
            IXLWorksheet ws,
            int          row,
            int          col1,
            int          col2,
            int          col3,
            string       label,
            XLColor      swatchColor,
            string       description,
            bool         border)
        {
            ws.Cell(row, col1).Value = label;
            ws.Cell(row, col1).Style.Font.FontSize = 9;
            ws.Cell(row, col1).Style.Font.Bold     = false;

            // Color swatch cell
            ws.Cell(row, col2).Style.Fill.BackgroundColor = swatchColor;
            ws.Cell(row, col2).Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
            ws.Cell(row, col2).Style.Border.OutsideBorderColor = XLColor.FromArgb(0xAA, 0xAA, 0xAA);

            ws.Cell(row, col3).Value = description;
            ws.Cell(row, col3).Style.Font.FontSize       = 9;
            ws.Cell(row, col3).Style.Font.FontColor      = XLColor.FromArgb(0x44, 0x44, 0x44);
            ws.Cell(row, col3).Style.Alignment.WrapText  = true;
            ws.Row(row).Height = 30;
        }

        // ─── Export info block ────────────────────────────────────────────────

        private void WriteExportInfoBlock(
            IXLWorksheet          ws,
            ScheduleExportContext context,
            int                   startRow,
            int                   fieldCount)
        {
            int col1    = ColDataStart;
            int col2    = ColDataStart + 1;
            int lastCol = ColDataStart + Math.Max(fieldCount - 1, 3);
            int r       = startRow;

            WriteSectionTitle(ws, r, col1, lastCol, "EXPORT INFORMATION");
            r++;

            WriteInfoRow(ws, r++, col1, col2, "Schedule",
                context.ScheduleName ?? string.Empty);
            WriteInfoRow(ws, r++, col1, col2, "Exported",
                context.ExportedAt ?? string.Empty);
            WriteInfoRow(ws, r++, col1, col2, "Elements",
                context.TotalElements.ToString());
            WriteInfoRow(ws, r++, col1, col2, "Filters",
                context.FiltersDisplay);
            WriteInfoRow(ws, r++, col1, col2, "Sort order",
                context.SortDisplay);
            WriteInfoRow(ws, r,   col1, col2, "Note",
                "This file was exported by BA Schedule Exporter. "
                + "Edit white/blue cells and re-import to write values back to Revit.");
        }

        private static void WriteInfoRow(
            IXLWorksheet ws,
            int          row,
            int          keyCol,
            int          valCol,
            string       key,
            string       value)
        {
            ws.Cell(row, keyCol).Value                        = key;
            ws.Cell(row, keyCol).Style.Font.Bold              = true;
            ws.Cell(row, keyCol).Style.Font.FontSize          = 9;
            ws.Cell(row, keyCol).Style.Font.FontColor         = XLColor.FromArgb(0x44, 0x44, 0x44);

            ws.Cell(row, valCol).Value                        = SafeString(value);
            ws.Cell(row, valCol).Style.Font.FontSize          = 9;
            ws.Cell(row, valCol).Style.Font.FontColor         = XLColor.FromArgb(0x22, 0x22, 0x22);
            ws.Cell(row, valCol).Style.Alignment.WrapText     = true;
            ws.Row(row).Height = 16;
        }

        // ─── Global formatting ────────────────────────────────────────────────

        private void ApplyColumnWidths(IXLWorksheet ws, List<ScheduleFieldMeta> fields)
        {
            ws.Column(ColElementId).Width = 0;
            ws.Column(ColUniqueId).Width  = 0;

            foreach (ScheduleFieldMeta meta in fields)
            {
                int col   = meta.ColumnIndex + ColDataStart;
                int width = Math.Max(12, Math.Min(meta.DisplayName.Length + 6, 42));
                ws.Column(col).Width = width;
            }
        }

        private void ApplyGlobalBorders(
            IXLWorksheet ws,
            int          fieldCount,
            int          dataRowCount)
        {
            if (fieldCount == 0 || dataRowCount == 0) return;

            int lastCol    = ColDataStart + fieldCount - 1;
            int lastDataRow = RowDataFrom + dataRowCount - 1;

            // Data range border: thin inner, thin outer
            var dataRange = ws.Range(RowHeader, ColDataStart, lastDataRow, lastCol);
            dataRange.Style.Border.InsideBorder      = XLBorderStyleValues.Hair;
            dataRange.Style.Border.InsideBorderColor = XLColor.FromArgb(0xCC, 0xCC, 0xCC);
            dataRange.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
            dataRange.Style.Border.OutsideBorderColor = XLColor.FromArgb(0x88, 0x88, 0x88);

            // Heavier border under header + data type rows
            ws.Range(RowDataType, ColDataStart, RowDataType, lastCol)
              .Style.Border.BottomBorder      = XLBorderStyleValues.Medium;
            ws.Range(RowDataType, ColDataStart, RowDataType, lastCol)
              .Style.Border.BottomBorderColor = XLColor.FromArgb(0x88, 0x88, 0x88);
        }

        private void ApplyAutoFilterAndFreeze(IXLWorksheet ws, int fieldCount)
        {
            // Freeze first 2 rows (header + data type)
            ws.SheetView.FreezeRows(2);

            if (fieldCount > 0)
            {
                int lastCol = ColDataStart + fieldCount - 1;
                ws.Range(RowHeader, ColDataStart, RowHeader, lastCol).SetAutoFilter();
            }
        }

        // ─── Shared section title ──────────────────────────────────────────────

        private static void WriteSectionTitle(
            IXLWorksheet ws,
            int          row,
            int          fromCol,
            int          toCol,
            string       title)
        {
            ws.Cell(row, fromCol).Value = title;
            ws.Cell(row, fromCol).Style.Font.Bold          = true;
            ws.Cell(row, fromCol).Style.Font.FontSize      = 9;
            ws.Cell(row, fromCol).Style.Font.FontColor     = XLColor.FromArgb(0x1F, 0x38, 0x64);
            ws.Cell(row, fromCol).Style.Fill.BackgroundColor =
                XLColor.FromArgb(0xD6, 0xDC, 0xE4);
            ws.Range(row, fromCol, row, toCol)
              .Style.Fill.BackgroundColor = XLColor.FromArgb(0xD6, 0xDC, 0xE4);
            ws.Row(row).Height = 16;
        }

        // ─── Utilities ────────────────────────────────────────────────────────

        /// <summary>
        /// Guards against null and formula injection.
        /// Strings starting with = + - @ are prefixed with a space.
        /// Values longer than Excel's 32 767 char limit are truncated.
        /// </summary>
        private static string SafeString(string s)
        {
            if (s == null) return string.Empty;
            if (s.Length > 0 && (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@'))
                s = " " + s;
            return s.Length > ExcelMaxCellLength
                ? s.Substring(0, ExcelMaxCellLength - 3) + "..."
                : s;
        }

        private static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Schedule";
            foreach (char c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
                name = name.Replace(c, '_');
            return name.Length > 31 ? name.Substring(0, 31) : name;
        }
    }
}
