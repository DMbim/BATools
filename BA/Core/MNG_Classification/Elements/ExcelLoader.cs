// File: BA.Classification/ExcelRuleLoader.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using ClosedXML.Excel;

namespace BA.Classification
{
    public static class ExcelRuleLoader
    {
        public static List<ClassificationRule> LoadRules(string excelPath, string sheetName = "Rules_vNext")
        {
            var rules = new List<ClassificationRule>();

            using var wb = new XLWorkbook(excelPath);
            var ws = wb.Worksheet(sheetName);

            var used = ws.RangeUsed();
            if (used == null) return rules;

            // Build header map: "ColumnName" -> column index
            var headerRow = used.FirstRow();
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var cell in headerRow.CellsUsed())
            {
                var name = (cell.GetString() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    headerMap[name] = cell.Address.ColumnNumber;
            }

            string GetStr(IXLRangeRow r, string col, string def = "")
            {
                if (!headerMap.TryGetValue(col, out var c)) return def;
                return (r.Cell(c).GetString() ?? def).Trim();
            }

            bool GetBool(IXLRangeRow r, string col, bool def = false)
            {
                if (!headerMap.TryGetValue(col, out var c)) return def;
                var cell = r.Cell(c);
                if (cell.IsEmpty()) return def;

                if (cell.TryGetValue<bool>(out var b)) return b;

                var s = (cell.GetString() ?? "").Trim();
                if (bool.TryParse(s, out var pb)) return pb;

                // Excel sometimes stores TRUE/FALSE as 1/0
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return i != 0;

                return def;
            }

            int GetInt(IXLRangeRow r, string col, int def = 0)
            {
                if (!headerMap.TryGetValue(col, out var c)) return def;
                var cell = r.Cell(c);
                if (cell.IsEmpty()) return def;

                if (cell.TryGetValue<double>(out var d)) return (int)Math.Round(d);

                var s = (cell.GetString() ?? "").Trim();
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : def;
            }

            double? GetDoubleNullable(IXLRangeRow r, string col)
            {
                if (!headerMap.TryGetValue(col, out var c)) return null;
                var cell = r.Cell(c);
                if (cell.IsEmpty()) return null;

                if (cell.TryGetValue<double>(out var d)) return d;

                var s = (cell.GetString() ?? "").Trim();
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : null;
            }

            foreach (var row in used.RowsUsed().Skip(1))
            {
                var enabled = GetBool(row, "Enabled", true);
                if (!enabled) continue;

                var rule = new ClassificationRule
                {
                    RuleId = GetStr(row, "RuleId"),
                    Enabled = enabled,
                    RulePriority = GetInt(row, "RulePriority", 0),
                    StopOnMatch = GetBool(row, "StopOnMatch", false),

                    TargetLabel_EN = GetStr(row, "TargetLabel_EN"),
                    TargetLabel_CZ = GetStr(row, "TargetLabel_CZ"),

                    TargetLevelCode = GetStr(row, "TargetLevelCode"),
                    RevitCategory = GetStr(row, "RevitCategory"),
                    RevitCategoryMatchMode = GetStr(row, "RevitCategoryMatchMode", "Equals"),

                    FamilyName = GetStr(row, "FamilyName"),
                    FamilyMatchMode = GetStr(row, "FamilyMatchMode", "Any"),

                    TypeName = GetStr(row, "TypeName"),
                    TypeMatchMode = GetStr(row, "TypeMatchMode", "Any"),

                    ParameterName = GetStr(row, "ParameterName"),
                    ParameterScope = GetStr(row, "ParameterScope", "Any"),
                    ValueType = GetStr(row, "ValueType", "Text"),
                    Operator = GetStr(row, "Operator", "Equals"),

                    ParameterValue1 = GetStr(row, "ParameterValue1"),
                    ParameterValue2 = GetStr(row, "ParameterValue2"),
                    Tolerance = GetDoubleNullable(row, "Tolerance"),
                    Notes = GetStr(row, "Notes"),

                    RowOrder = row.RowNumber()
                };

                rule.ParseCode();
                rules.Add(rule);
            }

            // IMPORTANT: do NOT List.Sort() here (not stable).
            // Keep raw order; we’ll deterministically order later with RowOrder tie-break.
            return rules;
        }
    }
}
