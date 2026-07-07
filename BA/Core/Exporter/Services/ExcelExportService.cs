using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.Services
{
    /// <summary>
    /// Writes schedule data to a ClosedXML Excel workbook (.xlsx).
    ///
    /// WORKBOOK STRUCTURE:
    ///   Sheet 1: Schedule data (named after the schedule, max 31 chars)
    ///   Sheet 2: "Legend" — color key and usage instructions
    ///
    /// COLUMN LAYOUT (Schedule sheet):
    ///   Column A: __ElementId  — hidden, locked, stores ElementId.Value as number
    ///   Column B: __UniqueId   — hidden, locked, stores UniqueId string
    ///   Column C+: schedule fields in definition order (visible fields only)
    ///
    /// CELL PROTECTION:
    ///   Instance parameters:    unlocked, no fill  — fully editable
    ///   Type parameters:        unlocked, blue fill — editable with warning on import
    ///   Calculated/ElementId:   locked,  gray fill + italic
    ///   Hidden system columns:  locked, hidden
    ///   Sheet protection enabled (no password) with Sort, AutoFilter, FormatColumns/Rows allowed.
    /// </summary>
    public class ExcelExportService
    {
        // Excel column indices
        private const int ElementIdCol = 1;  // Column A
        private const int UniqueIdCol = 2;   // Column B
        private const int DataColStart = 3;  // First schedule field column

        // Color palette
        private static readonly XLColor ColorTypeParam  = XLColor.FromArgb(0xBD, 0xD7, 0xEE); // Light blue
        private static readonly XLColor ColorCalculated = XLColor.FromArgb(0xD6, 0xD6, 0xD6); // Light gray
        private static readonly XLColor ColorHeader     = XLColor.FromArgb(0x1F, 0x49, 0x7D); // Dark blue
        private static readonly XLColor ColorHeaderFont = XLColor.White;

        public void Export(
            string filePath,
            string scheduleName,
            List<ScheduleFieldMeta> fields,
            List<ScheduleRowData> rows)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var workbook = new XLWorkbook();
            IXLWorksheet ws = workbook.Worksheets.Add(SanitizeSheetName(scheduleName));

            WriteHeaders(ws, fields);
            WriteDataRows(ws, fields, rows);
            UnlockEditableCells(ws, fields, rows.Count);
            ApplySheetProtection(ws);
            ApplyAutoFilterAndFreeze(ws, fields.Count);
            SetColumnWidths(ws, fields);
            AddLegendSheet(workbook);

            workbook.SaveAs(filePath);
        }

        // ─── Header row ────────────────────────────────────────────────────────

        private void WriteHeaders(IXLWorksheet ws, List<ScheduleFieldMeta> fields)
        {
            // System columns — hidden, no styling needed
            ws.Cell(1, ElementIdCol).Value = "__ElementId";
            ws.Cell(1, UniqueIdCol).Value  = "__UniqueId";

            ws.Column(ElementIdCol).Hide();
            ws.Column(UniqueIdCol).Hide();

            // Schedule field headers
            foreach (ScheduleFieldMeta meta in fields)
            {
                int col = meta.ColumnIndex + DataColStart;
                IXLCell cell = ws.Cell(1, col);
                cell.Value = meta.DisplayName;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ColorHeaderFont;
                cell.Style.Fill.BackgroundColor = ColorHeader;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Apply column color band in header based on field category
                ApplyHeaderCategoryIndicator(cell, meta);
            }
        }

        private void ApplyHeaderCategoryIndicator(IXLCell headerCell, ScheduleFieldMeta meta)
        {
            // Apply a slightly tinted header color for type param and calculated columns
            // so the band is visible even on the dark header row
            switch (meta.Category)
            {
                case FieldCategory.TypeParameter:
                    headerCell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x17, 0x5A, 0x9C); // Slightly lighter blue
                    break;
                case FieldCategory.Calculated:
                case FieldCategory.ElementIdType:
                    headerCell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x59, 0x59, 0x59); // Gray header
                    break;
            }
        }

        // ─── Data rows ─────────────────────────────────────────────────────────

        private void WriteDataRows(
            IXLWorksheet ws,
            List<ScheduleFieldMeta> fields,
            List<ScheduleRowData> rows)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                int excelRow = r + 2; // Row 1 is header
                ScheduleRowData row = rows[r];

                // System hidden columns — store as numbers for reliable roundtrip
                ws.Cell(excelRow, ElementIdCol).Value = (double)row.ElementId;
                ws.Cell(excelRow, UniqueIdCol).Value  = row.UniqueId ?? string.Empty;

                foreach (ScheduleFieldMeta meta in fields)
                {
                    int col = meta.ColumnIndex + DataColStart;
                    IXLCell cell = ws.Cell(excelRow, col);

                    object value = row.Values.TryGetValue(meta.ColumnIndex, out object v) ? v : string.Empty;
                    SetCellValue(cell, value);
                    ApplyDataCellStyle(cell, meta);
                }
            }
        }

        private static void SetCellValue(IXLCell cell, object value)
        {
            switch (value)
            {
                case int i:
                    cell.Value = i;
                    break;
                case long l:
                    cell.Value = (double)l;
                    break;
                case double d:
                    cell.Value = d;
                    break;
                case bool b:
                    cell.Value = b;
                    break;
                case string s:
                    cell.Value = s;
                    break;
                case null:
                    cell.Value = Blank.Value;
                    break;
                default:
                    cell.Value = value.ToString();
                    break;
            }
        }

        private void ApplyDataCellStyle(IXLCell cell, ScheduleFieldMeta meta)
        {
            switch (meta.Category)
            {
                case FieldCategory.TypeParameter:
                    cell.Style.Fill.BackgroundColor = ColorTypeParam;
                    break;
                case FieldCategory.Calculated:
                case FieldCategory.ElementIdType:
                    cell.Style.Fill.BackgroundColor = ColorCalculated;
                    cell.Style.Font.Italic = true;
                    break;
                // Instance: no fill
            }
        }

        // ─── Protection ────────────────────────────────────────────────────────

        /// <summary>
        /// In ClosedXML, all cells are locked=true by default when sheet protection is active.
        /// We explicitly unlock only the editable cells (Instance and TypeParameter).
        /// Hidden system columns A/B remain locked (default). Calculated columns remain locked.
        /// </summary>
        private void UnlockEditableCells(
            IXLWorksheet ws,
            List<ScheduleFieldMeta> fields,
            int dataRowCount)
        {
            foreach (ScheduleFieldMeta meta in fields)
            {
                if (meta.IsReadOnly) continue; // Calculated, ElementIdType — stay locked

                int col = meta.ColumnIndex + DataColStart;
                // Unlock data cells for this column (rows 2 to dataRowCount+1)
                for (int r = 2; r <= dataRowCount + 1; r++)
                    ws.Cell(r, col).Style.Protection.Locked = false;
            }
        }

        private void ApplySheetProtection(IXLWorksheet ws)
        {
            ws.Protect()
              .AllowElement(XLSheetProtectionElements.SelectLockedCells)
              .AllowElement(XLSheetProtectionElements.SelectUnlockedCells)
              .AllowElement(XLSheetProtectionElements.Sort)
              .AllowElement(XLSheetProtectionElements.AutoFilter)
              .AllowElement(XLSheetProtectionElements.FormatColumns)
              .AllowElement(XLSheetProtectionElements.FormatRows);
        }

        private void ApplyAutoFilterAndFreeze(IXLWorksheet ws, int fieldCount)
        {
            // Freeze header row
            ws.SheetView.FreezeRows(1);

            // AutoFilter on visible header columns only
            if (fieldCount > 0)
            {
                int lastCol = DataColStart + fieldCount - 1;
                ws.Range(1, DataColStart, 1, lastCol).SetAutoFilter();
            }
        }

        private void SetColumnWidths(IXLWorksheet ws, List<ScheduleFieldMeta> fields)
        {
            // Hidden columns — width doesn't matter but set minimal
            ws.Column(ElementIdCol).Width = 0;
            ws.Column(UniqueIdCol).Width  = 0;

            foreach (ScheduleFieldMeta meta in fields)
            {
                int col = meta.ColumnIndex + DataColStart;
                // Width based on display name length, clamped to a readable range
                int width = Math.Max(14, Math.Min(meta.DisplayName.Length + 6, 45));
                ws.Column(col).Width = width;
            }
        }

        // ─── Legend sheet ──────────────────────────────────────────────────────

        private void AddLegendSheet(XLWorkbook workbook)
        {
            IXLWorksheet ls = workbook.Worksheets.Add("Legend");

            ls.Cell(1, 1).Value = "BA Schedule Exporter — Column Legend";
            ls.Cell(1, 1).Style.Font.Bold = true;
            ls.Cell(1, 1).Style.Font.FontSize = 13;
            ls.Range(1, 1, 1, 4).Merge();

            ls.Cell(2, 2).Value = "Column Type";
            ls.Cell(2, 3).Value = "Fill Color";
            ls.Cell(2, 4).Value = "Import Behavior";
            ls.Row(2).Style.Font.Bold = true;

            WriteLegendRow(ls, 3,
                XLColor.White,
                "Instance Parameter",
                "(no fill)",
                "Values are written back per element on import.");

            WriteLegendRow(ls, 4,
                ColorTypeParam,
                "Type Parameter",
                "Blue",
                "Values are written to the element TYPE, affecting ALL instances of that type. " +
                "A warning dialog lists affected types and instance counts before import commits.");

            WriteLegendRow(ls, 5,
                ColorCalculated,
                "Calculated / Read-only",
                "Gray / Italic",
                "Column is locked. Values are derived or complex (formulas, counts, ElementId references). " +
                "Any edits are ignored on import.");

            ls.Column(1).Width = 3;
            ls.Column(2).Width = 24;
            ls.Column(3).Width = 14;
            ls.Column(4).Width = 72;

            ls.SheetView.FreezeRows(2);
        }

        private static void WriteLegendRow(
            IXLWorksheet ws,
            int row,
            XLColor swatchColor,
            string typeName,
            string colorLabel,
            string description)
        {
            IXLCell swatch = ws.Cell(row, 1);
            swatch.Style.Fill.BackgroundColor = swatchColor;
            swatch.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            ws.Cell(row, 2).Value = typeName;
            ws.Cell(row, 2).Style.Font.Bold = true;

            ws.Cell(row, 3).Value = colorLabel;

            ws.Cell(row, 4).Value = description;
            ws.Cell(row, 4).Style.Alignment.WrapText = true;
        }

        // ─── Utility ───────────────────────────────────────────────────────────

        private static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Schedule";
            char[] invalid = { ':', '\\', '/', '?', '*', '[', ']' };
            foreach (char c in invalid)
                name = name.Replace(c, '_');
            return name.Length > 31 ? name.Substring(0, 31) : name;
        }
    }
}
