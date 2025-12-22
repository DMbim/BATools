using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace BA.Classification
{
    public static class ExcelRuleLoader
    {
        public static List<ClassificationRule> LoadRules(string excelPath)
        {
            var rules = new List<ClassificationRule>();

            using (var wb = new XLWorkbook(excelPath))
            {
                var ws = wb.Worksheet("BA_ClassRules");
                var rows = ws.RangeUsed().RowsUsed().Skip(1); // skip header

                foreach (var row in rows)
                {
                    if (!row.Cell("B").GetBoolean()) continue; // Enabled == FALSE

                    var rule = new ClassificationRule
                    {
                        RuleId = row.Cell("A").GetString(),
                        Enabled = row.Cell("B").GetBoolean(),
                        RulePriority = (int)row.Cell("C").GetDouble(),
                        StopOnMatch = row.Cell("D").GetBoolean(),
                        TargetLevelCode = row.Cell("E").GetString(),
                        TargetLabel_EN = row.Cell("F").GetString(),
                        TargetLabel_Local = row.Cell("G").GetString(),
                        RevitCategory = row.Cell("H").GetString(),
                        RevitCategoryMatchMode = row.Cell("I").GetString(),
                        FamilyName = row.Cell("J").GetString(),
                        FamilyMatchMode = row.Cell("K").GetString(),
                        TypeName = row.Cell("L").GetString(),
                        TypeMatchMode = row.Cell("M").GetString(),
                        ParameterName = row.Cell("N").GetString(),
                        ParameterScope = row.Cell("O").GetString(),
                        ValueType = row.Cell("P").GetString(),
                        Operator = row.Cell("Q").GetString(),
                        ParameterValue1 = row.Cell("R").GetString(),
                        ParameterValue2 = row.Cell("S").GetString(),
                        Tolerance = row.Cell("T").TryGetValue(out double tol) ? tol : (double?)null,
                        Unit = row.Cell("U").GetString(),
                        Notes = row.Cell("V").GetString()
                    };

                    rule.ParseCode();
                    rules.Add(rule);
                }
            }

            rules.Sort((a, b) => b.RulePriority.CompareTo(a.RulePriority)); // DESC priority
            return rules;
        }
    }
}
