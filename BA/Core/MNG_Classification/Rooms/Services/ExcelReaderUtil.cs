using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.RoomClassification.Services
{
    // Shared low-level helpers used by both the Matrix reader and the FinishPresets reader.
    internal static class ExcelReaderUtil
    {
        public static Dictionary<string, int> BuildHeaderMap(IXLWorksheet ws)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int col = 1; col <= lastCol; col++)
            {
                string header = ws.Cell(1, col).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(header))
                    map[header] = col;
            }
            return map;
        }

        public static void EnsureRequiredHeaders(Dictionary<string, int> headerMap, string[] required, string sheetName)
        {
            List<string> missing = required.Where(x => !headerMap.ContainsKey(x)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"Worksheet '{sheetName}' is missing required columns: " + string.Join(", ", missing));
        }

        public static bool IsEntireRowEmpty(IXLWorksheet ws, int row, Dictionary<string, int> headerMap)
        {
            foreach (KeyValuePair<string, int> kvp in headerMap)
            {
                if (!string.IsNullOrWhiteSpace(ws.Cell(row, kvp.Value).GetString()))
                    return false;
            }
            return true;
        }

        public static string GetCellString(IXLWorksheet ws, int row, Dictionary<string, int> headerMap, string header)
        {
            return headerMap.TryGetValue(header, out int col)
                ? ws.Cell(row, col).GetString().Trim()
                : string.Empty;
        }

        public static bool ParseBooleanLike(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            string n = value.Trim().ToLowerInvariant();
            if (n == "1" || n == "true" || n == "yes" || n == "y") return true;
            if (n == "0" || n == "false" || n == "no" || n == "n") return false;
            return defaultValue;
        }
    }
}
